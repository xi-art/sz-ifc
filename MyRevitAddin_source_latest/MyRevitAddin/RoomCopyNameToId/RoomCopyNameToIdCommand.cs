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

namespace MyRevitAddin.RoomCopyNameToId
{
    /// <summary>
    /// 房间名称 → 属性参数 参数批量赋值
    /// 工作流：自动识别 属性参数 组参数（实例/类型）→ 勾选目标 → 预览 → 一键应用
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomCopyNameToIdCommand : IExternalCommand
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
                SafeShow("房间名→属性参数", message, TaskDialogIcon.TaskDialogIconWarning);
                return Result.Failed;
            }

            try
            {
                MiniLog.Info("OPEN-DIALOG title=" + doc.Title);
                IWin32Window owner = GetRevitOwner(uiApp);
                BatchDialogResult dr;
                BatchDialogResult dlgResult;
                BatchDialogResult dlgResultLocal = default;
                dlgResult = default;

                // 显式 new + 显式接收 + finally Dispose
                RoomCopyNameToIdDialog dlg = new RoomCopyNameToIdDialog(doc);
                try
                {
                    dlgResultLocal = dlg.ShowDialogEx(owner);
                }
                finally { try { dlg.Dispose(); } catch { } }
                dlgResult = dlgResultLocal;
                dr = dlgResult;
                MiniLog.Info("DIALOG-RESULT=" + dr);

                if (dr == null || !dr.Confirmed) return Result.Cancelled;

                int totalRooms = dr.Assignments.Count;
                if (totalRooms == 0)
                {
                    SafeShow("房间名→属性参数", "没有可应用的修改项。", TaskDialogIcon.TaskDialogIconInformation);
                    return Result.Cancelled;
                }

                int ok = 0, fail = 0;
                string errorLog = "";

                using (var tx = new Transaction(doc, "房间名→属性参数"))
                {
                    tx.Start();
                    foreach (var a in dr.Assignments)
                    {
                        try
                        {
                            Element e = doc.GetElement(a.RoomId);
                            if (e == null) { fail++; errorLog += "[id=" + a.RoomId.IntegerValue + "] 元素不存在\n"; continue; }

                            // 优先写实例参数，否则写类型参数
                            Parameter p = FindParameter(e, a.ParamKey, a.IsInstance);
                            if (p == null)
                            {
                                fail++;
                                errorLog += "[" + (e.Name ?? "") + "] 未找到参数: " + a.ParamKey + "\n";
                                continue;
                            }
                            if (p.IsReadOnly)
                            {
                                fail++;
                                errorLog += "[" + (e.Name ?? "") + "] 参数只读: " + a.ParamKey + "\n";
                                continue;
                            }
                            p.Set(a.NewValue);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            errorLog += "[" + a.RoomId.IntegerValue + " " + a.ParamKey + "] " + ex.Message + "\n";
                            MiniLog.Error("SET", ex);
                        }
                    }
                    var res = tx.Commit();
                    MiniLog.Info("TX-COMMIT=" + res + " ok=" + ok + " fail=" + fail);
                    if (res != TransactionStatus.Committed)
                    {
                        SafeShow("房间名→属性参数",
                            "事务未能提交，已回滚。\n可能存在参数不可写或值非法。",
                            TaskDialogIcon.TaskDialogIconWarning);
                        return Result.Failed;
                    }
                }

                string summary = $"房间名 → 属性参数：批量赋值完成。\n\n成功 {ok} 条，失败 {fail} 条。";
                if (!string.IsNullOrWhiteSpace(errorLog))
                {
                    try
                    {
                        string logFile = MiniLog.WriteErrorLog(errorLog);
                        if (logFile != null) summary += "\n\n失败明细已写入：" + logFile;
                    }
                    catch { }
                }
                SafeShow("房间名→属性参数", summary, TaskDialogIcon.TaskDialogIconInformation);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MiniLog.Error("EXEC-FAIL", ex);
                message = ex.Message;
                SafeShow("房间名→属性参数 - 出错",
                    ex.GetType().Name + ": " + ex.Message + "\n\n" + MiniLog.LastLogFileHint,
                    TaskDialogIcon.TaskDialogIconError);
                return Result.Failed;
            }
        }

        /// <summary>
        /// 在元素上按参数名找参数；可指定找实例或类型
        /// </summary>
        private static Parameter FindParameter(Element e, string paramName, bool instance)
        {
            if (string.IsNullOrEmpty(paramName)) return null;

            // 先按 Definition.Name 找
            foreach (Parameter p in e.Parameters)
            {
                if (p.Definition != null && string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            if (instance) return null;

            // 再在 ElementType 上找（仅当需要类型参数时）
            ElementType et = e.Document.GetElement(e.GetTypeId()) as ElementType;
            if (et == null) return null;
            foreach (Parameter p in et.Parameters)
            {
                if (p.Definition != null && string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
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

    /// <summary>
    /// 单次赋值项：把 NewValue 写到 RoomId 元素的 ParamKey 上
    /// </summary>
    public class RoomParamAssignment
    {
        public ElementId RoomId { get; set; }
        public string ParamKey { get; set; }
        public bool IsInstance { get; set; }
        public string NewValue { get; set; }
    }

    /// <summary>
    /// 对话框回传结果
    /// </summary>
    public class BatchDialogResult
    {
        public bool Confirmed { get; set; }
        public List<RoomParamAssignment> Assignments { get; set; } = new List<RoomParamAssignment>();
    }

    // ============================================================
    // 简单日志
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
            string f = Path.Combine(dir, "RoomCopyNameToId_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
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
                string name = "room_name_to_id_failures_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, text);
                return path;
            }
            catch { return null; }
        }
    }
}
