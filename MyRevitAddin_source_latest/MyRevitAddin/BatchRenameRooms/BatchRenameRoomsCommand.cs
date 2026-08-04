using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchRenameRooms
{
    /// <summary>
    /// 批量修改房间名称
    /// 工作流：选楼层 → 编辑规则（前后缀/查找替换/编号）→ 预览 → 一键应用
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData?.Application;
            UIDocument uidoc = uiApp?.ActiveUIDocument;
            Document doc = uidoc?.Document ?? uiApp?.Application?.Documents
                .Cast<Document>().FirstOrDefault(d => !d.IsFamilyDocument && d.IsValidObject);

            if (doc == null)
            {
                message = "请先打开一个 Revit 项目文档。";
                MiniLog.Error("NO-DOC", new Exception(message));
                SafeShow("批量修改房间名称", message, TaskDialogIcon.TaskDialogIconWarning);
                return Result.Failed;
            }

            try
            {
                MiniLog.Info("OPEN-DIALOG title=" + doc.Title);
                IWin32Window owner = GetRevitOwner(uiApp);
                BatchRenameRoomsDialog dlg = new BatchRenameRoomsDialog(doc);
                DialogResult dr;
                try
                {
                    dr = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                }
                finally { try { dlg.Dispose(); } catch { } }
                MiniLog.Info("DIALOG-RESULT=" + dr);

                if (dr != DialogResult.OK) return Result.Cancelled;

                var toApply = dlg.ItemsToApply;
                MiniLog.Info("APPLY-COUNT=" + toApply.Count);
                if (toApply.Count == 0)
                {
                    SafeShow("批量修改房间名称", "没有可应用的修改项。", TaskDialogIcon.TaskDialogIconInformation);
                    return Result.Cancelled;
                }

                int ok = 0, fail = 0;
                string errorLog = "";

                using (var tx = new Transaction(doc, "批量修改房间名称"))
                {
                    tx.Start();
                    foreach (var it in toApply)
                    {
                        try
                        {
                            Element e = doc.GetElement(it.RoomId);
                            if (e == null)
                            {
                                fail++;
                                errorLog += "[id=" + it.RoomId.IntegerValue + "] 元素不存在\n";
                                continue;
                            }
                            Parameter pName = e.get_Parameter(BuiltInParameter.ROOM_NAME);
                            if (pName == null || pName.IsReadOnly)
                            {
                                fail++;
                                errorLog += "[" + it.OriginalName + "] 名称参数不可写\n";
                                continue;
                            }
                            pName.Set(it.NewName);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            errorLog += "[" + it.OriginalName + " -> " + it.NewName + "] " + ex.Message + "\n";
                            MiniLog.Error("RENAME", ex);
                        }
                    }
                    var res = tx.Commit();
                    MiniLog.Info("TX-COMMIT=" + res);
                    if (res != TransactionStatus.Committed)
                    {
                        SafeShow("批量修改房间名称",
                            "事务未能提交，已回滚。可能存在空名称或重名冲突。",
                            TaskDialogIcon.TaskDialogIconWarning);
                        return Result.Failed;
                    }
                }

                string summary = $"批量修改完成。\n\n成功 {ok} 条，失败 {fail} 条。";
                if (!string.IsNullOrWhiteSpace(errorLog))
                {
                    try
                    {
                        string logFile = MiniLog.WriteErrorLog(errorLog);
                        if (logFile != null) summary += "\n\n失败明细已写入：" + logFile;
                    }
                    catch { }
                }
                SafeShow("批量修改房间名称", summary, TaskDialogIcon.TaskDialogIconInformation);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MiniLog.Error("EXEC-FAIL", ex);
                message = ex.Message;
                SafeShow("批量修改房间名称 - 出错",
                    ex.GetType().Name + ": " + ex.Message + "\n\n" + MiniLog.LastLogFileHint,
                    TaskDialogIcon.TaskDialogIconError);
                return Result.Failed;
            }
        }

        private static void SafeShow(string title, string text, TaskDialogIcon icon)
        {
            try
            {
                var td = new TaskDialog(title);
                td.MainInstruction = title;
                td.MainContent = text;
                switch (icon)
                {
                    case TaskDialogIcon.TaskDialogIconWarning: td.MainIcon = TaskDialogIcon.TaskDialogIconWarning; break;
                    case TaskDialogIcon.TaskDialogIconError: td.MainIcon = TaskDialogIcon.TaskDialogIconError; break;
                    default: td.MainIcon = TaskDialogIcon.TaskDialogIconInformation; break;
                }
                td.Show();
            }
            catch
            {
                try { MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            }
        }

        private static IWin32Window GetRevitOwner(UIApplication uiApp)
        {
            try
            {
                IntPtr h = Process.GetCurrentProcess().MainWindowHandle;
                if (h != IntPtr.Zero) return new Win32Window(h);
            }
            catch { }
            try
            {
                foreach (Process p in Process.GetProcessesByName("Revit"))
                {
                    IntPtr h2 = p.MainWindowHandle;
                    if (h2 != IntPtr.Zero) return new Win32Window(h2);
                }
            }
            catch { }
            return null;
        }

        private class Win32Window : IWin32Window
        {
            private readonly IntPtr _h;
            public Win32Window(IntPtr h) { _h = h; }
            public IntPtr Handle { get { return _h; } }
        }
    }

    // ============================================================
    // 简单日志：与 BatchRenameFamilies 同款，便于问题定位
    // ============================================================
    internal static class MiniLog
    {
        private static readonly object _lock = new object();
        private static string _lastFile;
        public static string LastLogFileHint
        {
            get { return _lastFile == null ? "" : "诊断日志：" + _lastFile; }
        }

        private static string EnsureFile()
        {
            if (_lastFile != null) return _lastFile;
            string dir = null;
            try { dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyRevitAddin_Logs"); }
            catch { dir = Path.Combine(Path.GetTempPath(), "MyRevitAddin_Logs"); }
            try { Directory.CreateDirectory(dir); } catch { }
            string f = Path.Combine(dir, "BatchRenameRooms_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            _lastFile = f;
            return f;
        }

        public static void Info(string msg)
        {
            try
            {
                lock (_lock)
                {
                    string f = EnsureFile();
                    File.AppendAllText(f, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] INF " + msg + "\r\n");
                }
            }
            catch { }
        }

        public static void Error(string tag, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    string f = EnsureFile();
                    File.AppendAllText(f,
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] ERR " + tag +
                        "\r\n  " + ex.GetType().Name + ": " + ex.Message +
                        "\r\n  " + (ex.StackTrace ?? "").Replace("\n", "\r\n  ") + "\r\n");
                }
            }
            catch { }
        }

        public static string WriteErrorLog(string text)
        {
            try
            {
                string f = EnsureFile();
                string dir = Path.GetDirectoryName(f);
                string name = "rename_rooms_failures_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, text);
                return path;
            }
            catch { return null; }
        }
    }
}
