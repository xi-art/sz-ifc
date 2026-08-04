using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchEditElementParams
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchEditElementParamsCommand : IExternalCommand
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
                SafeShow("批量修改构件属性", message, TaskDialogIcon.TaskDialogIconWarning);
                return Result.Failed;
            }

            try
            {
                MiniLog.Info("OPEN-DIALOG title=" + doc.Title);
                IWin32Window owner = GetRevitOwner(uiApp);

                var selectedIds = uidoc?.Selection.GetElementIds().ToList() ?? new List<ElementId>();

                EditDialogResult dlgResult;
                using (var dlg = new BatchEditElementParamsDialog(doc, uidoc, selectedIds))
                {
                    dlgResult = dlg.ShowDialogEx(owner);
                }

                MiniLog.Info("DIALOG-RESULT confirmed=" + (dlgResult?.Confirmed == true));

                if (dlgResult == null || !dlgResult.Confirmed) return Result.Cancelled;

                int total = dlgResult.Assignments.Count;
                if (total == 0)
                {
                    SafeShow("批量修改构件属性", "没有可应用的修改项。", TaskDialogIcon.TaskDialogIconInformation);
                    return Result.Cancelled;
                }

                int ok = 0, fail = 0;
                string errorLog = "";

                using (var tx = new Transaction(doc, "批量修改构件属性"))
                {
                    tx.Start();
                    foreach (var a in dlgResult.Assignments)
                    {
                        try
                        {
                            Element e = doc.GetElement(a.ElementId);
                            if (e == null) { fail++; errorLog += "[id=" + a.ElementId.IntegerValue + "] 元素不存在\n"; continue; }

                            Parameter p = FindParameter(e, a.ParamName, a.IsInstance);
                            if (p == null)
                            {
                                fail++;
                                errorLog += "[" + (e.Name ?? "") + "] 未找到参数: " + a.ParamName + "\n";
                                continue;
                            }
                            if (p.IsReadOnly)
                            {
                                fail++;
                                errorLog += "[" + (e.Name ?? "") + "] 参数只读: " + a.ParamName + "\n";
                                continue;
                            }

                            SetParamValue(p, a.NewValue);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            errorLog += "[" + a.ElementId.IntegerValue + " " + a.ParamName + "] " + ex.Message + "\n";
                            MiniLog.Error("SET", ex);
                        }
                    }
                    var res = tx.Commit();
                    MiniLog.Info("TX-COMMIT=" + res + " ok=" + ok + " fail=" + fail);
                    if (res != TransactionStatus.Committed)
                    {
                        SafeShow("批量修改构件属性",
                            "事务未能提交，已回滚。\n可能存在参数不可写或值非法。",
                            TaskDialogIcon.TaskDialogIconWarning);
                        return Result.Failed;
                    }
                }

                string summary = string.Format("批量修改构件属性完成。\n\n成功 {0} 条，失败 {1} 条。", ok, fail);
                if (!string.IsNullOrWhiteSpace(errorLog))
                {
                    try
                    {
                        string logFile = MiniLog.WriteErrorLog(errorLog);
                        if (logFile != null) summary += "\n\n失败明细已写入：" + logFile;
                    }
                    catch { }
                }
                SafeShow("批量修改构件属性", summary, TaskDialogIcon.TaskDialogIconInformation);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MiniLog.Error("EXEC-FAIL", ex);
                message = ex.Message;
                SafeShow("批量修改构件属性 - 出错",
                    ex.GetType().Name + ": " + ex.Message + "\n\n" + MiniLog.LastLogFileHint,
                    TaskDialogIcon.TaskDialogIconError);
                return Result.Failed;
            }
        }

        private static Parameter FindParameter(Element e, string paramName, bool instance)
        {
            if (string.IsNullOrEmpty(paramName)) return null;

            foreach (Parameter p in e.Parameters)
            {
                if (p.Definition != null && string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            if (instance) return null;

            ElementType et = e.Document.GetElement(e.GetTypeId()) as ElementType;
            if (et == null) return null;
            foreach (Parameter p in et.Parameters)
            {
                if (p.Definition != null && string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static void SetParamValue(Parameter p, string val)
        {
            // 优先直接用 string 写入（类似房间名→属性参数的成功模式）
            try
            {
                p.Set(val ?? "");
                return;
            }
            catch { }

            switch (p.StorageType)
            {
                case StorageType.String:
                    p.Set(val ?? "");
                    break;
                case StorageType.Integer:
                    if (string.IsNullOrEmpty(val)) { p.Set(0); return; }
                    int iv;
                    if (int.TryParse(val, out iv)) { p.Set(iv); return; }
                    bool bv;
                    if (bool.TryParse(val, out bv)) { p.Set(bv ? 1 : 0); return; }
                    // 最后 fallback 用 SetValueString
                    try { p.SetValueString(val); } catch { p.Set(0); }
                    break;
                case StorageType.Double:
                    if (string.IsNullOrEmpty(val)) { p.Set(0.0); return; }
                    double dv;
                    if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                    {
                        try { p.SetValueString(val); return; }
                        catch { p.Set(dv); return; }
                    }
                    try { p.SetValueString(val); } catch { p.Set(0.0); }
                    break;
                case StorageType.ElementId:
                    ElementId id = TryFindElementIdByName(p, val);
                    if (id != null) { p.Set(id); return; }
                    try { p.SetValueString(val); } catch { }
                    break;
            }
        }

        private static ElementId TryFindElementIdByName(Parameter p, string val)
        {
            if (string.IsNullOrEmpty(val)) return null;
            try
            {
                Document doc = p.Element.Document;
                BuiltInCategory bic = BuiltInCategory.INVALID;
                var def = p.Definition as InternalDefinition;
                if (def != null)
                {
                    // 参数名含"材质"尝试从材质类别找
                    string n = def.Name ?? "";
                    if (n.IndexOf("材质", StringComparison.OrdinalIgnoreCase) >= 0)
                        bic = BuiltInCategory.OST_Materials;
                }
                if (bic != BuiltInCategory.INVALID)
                {
                    var e = new FilteredElementCollector(doc).OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .Cast<Element>()
                        .FirstOrDefault(x => string.Equals(x.Name, val, StringComparison.OrdinalIgnoreCase));
                    if (e != null) return e.Id;
                }
                // fallback: 直接搜所有 element
                var all = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Cast<Element>()
                    .FirstOrDefault(x => string.Equals(x.Name, val, StringComparison.OrdinalIgnoreCase));
                return all != null ? all.Id : null;
            }
            catch { return null; }
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

    public class ParamEditAssignment
    {
        public ElementId ElementId { get; set; }
        public string ParamName { get; set; }
        public bool IsInstance { get; set; }
        public string NewValue { get; set; }
    }

    public class EditDialogResult
    {
        public bool Confirmed { get; set; }
        public List<ParamEditAssignment> Assignments { get; set; } = new List<ParamEditAssignment>();
    }

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
            string f = Path.Combine(dir, "BatchEditElementParams_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
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
                string name = "batch_edit_failures_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, text);
                return path;
            }
            catch { return null; }
        }
    }
}
