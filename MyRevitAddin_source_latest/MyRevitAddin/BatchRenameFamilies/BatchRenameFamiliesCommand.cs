using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchRenameFamilies
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                MiniLog.Info("Execute:ENTER");
                try { EnsureWinFormsReady(); }
                catch (Exception wfEx)
                {
                    MiniLog.Error("EnsureWinFormsReady", wfEx);
                }

                UIApplication uiApp = commandData?.Application;
                MiniLog.Info("uiAppIsNull=" + (uiApp == null ? "YES" : "NO"));
                UIDocument uiDoc = null;
                Document doc = null;
                try { uiDoc = uiApp?.ActiveUIDocument; MiniLog.Info("uiDocIsNull=" + (uiDoc == null ? "YES" : "NO")); }
                catch (Exception ex) { MiniLog.Error("Read-UIDoc", ex); }
                try { doc = uiDoc?.Document ?? uiApp?.Application?.Documents?.Cast<Document>().FirstOrDefault(d => !d.IsFamilyDocument && d.IsValidObject); MiniLog.Info("docIsNull=" + (doc == null ? "YES" : "NO") + " title=" + (doc == null ? "" : doc.Title)); }
                catch (Exception ex) { MiniLog.Error("Read-Doc", ex); }

                if (doc == null)
                {
                    message = "当前没有可操作的项目文档。请打开一个 .rvt 项目后再试。";
                    try { TaskDialog.Show("批量族重命名", message); }
                    catch { MessageBox.Show(message, "批量族重命名", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    return Result.Failed;
                }

                try
                {
                    MiniLog.Info("RunInner:INVOKE");
                    Result r = RunInner(uiApp, doc, ref message);
                    MiniLog.Info("RunInner:RETURN=" + r);
                    return r;
                }
                catch (TypeLoadException tle)
                {
                    MiniLog.Error("JIT-TYPELOAD", tle);
                    return FailFriendly(ref message, tle, "JIT 类型加载失败：" + tle.TypeName + "\n\n请把下列诊断日志交给开发者:\n" + MiniLog.LastLogFileHint);
                }
                catch (FileNotFoundException fnf)
                {
                    MiniLog.Error("JIT-FILENOTFOUND", fnf);
                    return FailFriendly(ref message, fnf, "缺少依赖程序集：" + fnf.FileName + "\n\n" + MiniLog.LastLogFileHint);
                }
                catch (Exception ex)
                {
                    MiniLog.Error("RunInner-UNHANDLED", ex);
                    return FailFriendly(ref message, ex, MiniLog.LastLogFileHint);
                }
            }
            catch (Exception exOuter)
            {
                try { MiniLog.Error("Execute-OUTER-FATAL", exOuter); }
                catch { }
                string msg = "Execute 最外层异常：" + exOuter.GetType().Name + ": " + exOuter.Message;
                try { TaskDialog.Show("批量族重命名 - 严重错误", msg + "\n\n" + exOuter.StackTrace); }
                catch { try { MessageBox.Show(msg, "批量族重命名", MessageBoxButtons.OK, MessageBoxIcon.Stop); } catch { } }
                message = msg;
                return Result.Failed;
            }
        }

        private static Result FailFriendly(ref string message, Exception ex, string extra)
        {
            message = ex.Message;
            string full =
                "批量族重命名在启动过程中发生错误：\n\n" +
                ex.GetType().Name + ": " + ex.Message + "\n\n" +
                (string.IsNullOrEmpty(extra) ? "" : extra + "\n\n") +
                "StackTrace:\n" + ex.StackTrace;
            try { TaskDialog.Show("批量族重命名 - 出错", full); }
            catch { try { MessageBox.Show(full, "批量族重命名", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { } }
            return Result.Failed;
        }

        private static void EnsureWinFormsReady()
        {
            try
            {
                if (Application.VisualStyleState == System.Windows.Forms.VisualStyles.VisualStyleState.NoneEnabled)
                    Application.EnableVisualStyles();
            }
            catch { }
            // 注意：SetCompatibleTextRenderingDefault 必须在应用程序创建第一个 IWin32Window 之前调用。
            // 在 Revit 插件环境下，通常 Revit 主窗口或其它插件已经先创建过窗口了，因此这里故意跳过，
            // 避免抛 InvalidOperationException 造成干扰。
        }

        private static IWin32Window GetRevitOwner()
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

        private static Result RunInner(UIApplication uiApp, Document doc, ref string message)
        {
            var opArgs = new Dictionary<string, string>
            {
                { "doc_title", doc.Title ?? "" },
                { "doc_path", doc.PathName ?? "" }
            };
            MiniLog.Info("OPEN-Dialog title=" + doc.Title);

            BatchRenameFamiliesDialog dlg = null;
            DialogResult dr;
            try
            {
                MiniLog.Info("Dialog-CTOR:NEW");
                dlg = new BatchRenameFamiliesDialog(doc);
                MiniLog.Info("Dialog-CTOR:OK");
                IWin32Window owner = GetRevitOwner();
                try
                {
                    MiniLog.Info("Dialog-ShowDialog(owner=" + (owner == null ? "NULL" : "OK") + ")");
                    dr = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                    MiniLog.Info("Dialog-ShowDialog=" + dr);
                }
                finally { try { dlg?.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                MiniLog.Error("Dialog-CREATE/SHOW", ex);
                throw;
            }

            if (dr != DialogResult.OK)
            {
                MiniLog.Info("UserCancel");
                return Result.Cancelled;
            }

            var toApply = (dlg.ItemsToApply ?? new List<RenameItem>())
                .Where(i => i.Selected && (
                    (!string.IsNullOrEmpty(i.NewName) && i.NewName != i.OriginalName) ||
                    (!string.IsNullOrEmpty(i.ParentNewName) && i.ParentNewName != i.ParentName)))
                .ToList();
            MiniLog.Info("ItemsToApplyCount=" + toApply.Count);
            if (toApply.Count == 0)
            {
                try { TaskDialog.Show("批量族重命名", "没有勾选且需要修改的项。请确认已在对话框中勾选并编辑新名称或父族新名。"); }
                catch { MessageBox.Show("没有勾选且需要修改的项。", "批量族重命名", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                return Result.Cancelled;
            }

            opArgs["items_count"] = toApply.Count.ToString();
            int okSymbol = 0, okParent = 0, fail = 0;
            string errorLog = "";

            using (var tx = new Transaction(doc, "批量族类型/父族重命名"))
            {
                MiniLog.Info("TX-START");
                tx.Start();

                // 先处理父族重命名（避免子类型找不到父族）
                var parentRenames = toApply
                    .Where(i => !string.IsNullOrEmpty(i.ParentNewName) && i.ParentNewName != i.ParentName)
                    .GroupBy(i => i.ParentId.IntegerValue)
                    .Select(g => g.First())
                    .ToList();
                foreach (var it in parentRenames)
                {
                    try
                    {
                        Element e = doc.GetElement(it.ParentId);
                        if (e == null) { fail++; errorLog += "[父族 id=" + it.ParentId.IntegerValue + "] 找不到元素\n"; continue; }
                        if (e.Name == it.ParentNewName) continue;
                        e.Name = it.ParentNewName;
                        MiniLog.Info("RENAME-PARENT OK [" + e.GetType().Name + "] " + it.ParentName + " -> " + it.ParentNewName);
                        okParent++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errorLog += "[父族 " + it.ParentName + " -> " + it.ParentNewName + "] 失败: " + ex.Message + "\n";
                        MiniLog.Error("RENAME-PARENT " + it.ParentName + "->" + it.ParentNewName, ex);
                    }
                }

                // 再处理子类型/系统类型重命名
                foreach (var it in toApply)
                {
                    if (it.Kind != "FamilySymbol" && it.Kind != "SystemType") continue;
                    if (string.IsNullOrEmpty(it.NewName) || it.NewName == it.OriginalName) continue;
                    try
                    {
                        Element e = doc.GetElement(it.Id);
                        if (e == null) { fail++; errorLog += "[id=" + it.Id.IntegerValue + "] 找不到元素\n"; continue; }
                        if (e.Name == it.NewName) continue;
                        e.Name = it.NewName;
                        MiniLog.Info("RENAME-SYMBOL OK [" + it.Kind + "] " + it.OriginalName + " -> " + it.NewName);
                        okSymbol++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errorLog += "[" + it.Kind + " " + it.OriginalName + " -> " + it.NewName + "] 失败: " + ex.Message + "\n";
                        MiniLog.Error("RENAME-SYMBOL " + it.OriginalName + "->" + it.NewName, ex);
                    }
                }

                MiniLog.Info("TX-COMMIT-START");
                var res = tx.Commit();
                MiniLog.Info("TX-COMMIT-RESULT=" + res);
                if (res != TransactionStatus.Committed)
                {
                    message = "事务未提交，已回滚。可能存在非法命名（特殊字符/同名冲突等）。";
                    try { TaskDialog.Show("批量族重命名", message); }
                    catch { MessageBox.Show(message, "批量族重命名", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    return Result.Failed;
                }
            }

            string summary =
                "批量重命名完成。\n\n" +
                "子类型成功：" + okSymbol + "\n" +
                "父族成功：" + okParent + "\n" +
                "失败：" + fail + "\n" +
                "合计待处理：" + toApply.Count;

            if (!string.IsNullOrWhiteSpace(errorLog))
            {
                try
                {
                    string logFile = MiniLog.WriteErrorLog(errorLog);
                    if (logFile != null) summary += "\n\n失败明细已写入：" + logFile;
                }
                catch { }
            }

            MiniLog.Info("SUMMARY: " + summary.Replace("\n", " | "));
            try { TaskDialog.Show("批量族重命名", summary); }
            catch { MessageBox.Show(summary, "批量族重命名", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            return Result.Succeeded;
        }
    }

    internal static class MiniLog
    {
        private static readonly object _lock = new object();
        private static string _lastFile;
        public static string LastLogFileHint { get { return _lastFile == null ? "" : "最近一次诊断日志：" + _lastFile; } }

        private static string EnsureFile()
        {
            if (_lastFile != null) return _lastFile;
            string dir = null;
            try { dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyRevitAddin_Logs"); }
            catch { dir = Path.Combine(Path.GetTempPath(), "MyRevitAddin_Logs"); }
            try { Directory.CreateDirectory(dir); }
            catch { }
            string f = Path.Combine(dir, "BatchRenameFamilies_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
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
                        "\r\n  " + ex.StackTrace?.Replace("\n", "\r\n  ") + "\r\n");
                    if (ex.InnerException != null)
                        File.AppendAllText(f, "  INNER: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message + "\r\n  " + ex.InnerException.StackTrace + "\r\n");
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
                string name = "rename_failures_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, text);
                return path;
            }
            catch { return null; }
        }
    }
}
