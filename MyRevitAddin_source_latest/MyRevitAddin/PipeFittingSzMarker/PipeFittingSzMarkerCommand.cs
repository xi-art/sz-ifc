using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.PipeFittingSzMarker
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipeFittingSzMarkerCommand : IExternalCommand
    {
        // 关键字 → 填充值 映射表
        // 用户通用规则：「变径」属于次等，其他所有关键字都优先于「变径」
        // 所以分组如下（同组长词优先，同类按原序）：
        //   A组（优先：接头类 + 三通/弯头/四通，按长度降序）→ B组（变径类：大小头/变径，放在最后）
        private static readonly KeyValuePair<string, string>[] KeywordMap = new[]
        {
            // —— A 组：非变径类（全部优先于 B 组）——
            new KeyValuePair<string, string>("变径接头", "活接头"),   // 4字：虽含「变径」但主体是「接头」，归为接头类优先
            new KeyValuePair<string, string>("活接头",   "活接头"),   // 3字
            new KeyValuePair<string, string>("三通",     "三通"),     // 2字
            new KeyValuePair<string, string>("弯头",     "弯头"),
            new KeyValuePair<string, string>("四通",     "四通"),
            new KeyValuePair<string, string>("接头",     "活接头"),   // 2字兜底：其他带「接头」的都归活接头
            // —— B 组：变径类（次等，只有在 A 组全没命中时才生效）——
            new KeyValuePair<string, string>("大小头",   "过渡件"),   // 3字
            new KeyValuePair<string, string>("变径",     "过渡件")    // 2字
        };

        // 对外列出可读的映射表（供 UI 提示用）
        internal static readonly string MappingTip =
            "【优先】变径接头/活接头/三通/弯头/四通/接头→活接头 ；  " +
            "【次等】大小头/变径→过渡件  （规则：只要名称里还有其他关键词，就不按变径）";

        private const string TargetParamName = "深圳构件标识";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData?.Application;
            UIDocument uidoc = uiApp?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                message = "请先打开一个 Revit 项目文档。";
                return Result.Failed;
            }

            try
            {
                var selectedIds = uidoc?.Selection?.GetElementIds()?.ToList() ?? new List<ElementId>();
                using (var dlg = new PipeFittingSzMarkerDialog(doc, selectedIds))
                {
                    var result = dlg.ShowDialog(GetRevitOwner(uiApp));
                    if (result != DialogResult.OK) return Result.Cancelled;

                    var assignments = dlg.Assignments;
                    if (assignments == null || assignments.Count == 0)
                    {
                        SafeShow("提示", "没有可应用的修改项（所有管件都没匹配到关键字）。", TaskDialogIcon.TaskDialogIconInformation);
                        return Result.Cancelled;
                    }

                    int ok = 0, noParam = 0, readOnly = 0, fail = 0;
                    string errorLog = "";

                    using (var tx = new Transaction(doc, "管件深圳构件标识填充"))
                    {
                        tx.Start();
                        foreach (var a in assignments)
                        {
                            try
                            {
                                Element e = doc.GetElement(a.ElementId);
                                if (e == null || !e.IsValidObject) { fail++; continue; }

                                Parameter p = FindParameterCaseInsensitive(e, TargetParamName);
                                if (p == null)
                                {
                                    noParam++;
                                    errorLog += string.Format("[缺失参数] 类型名=\"{0}\"  ID={1}\n",
                                        (e.Name ?? "") + "|" + (e is ElementType ? "" : SafeTypeName(e)),
                                        a.ElementId.IntegerValue);
                                    continue;
                                }
                                if (p.IsReadOnly)
                                {
                                    readOnly++;
                                    continue;
                                }
                                SetParamString(p, a.MatchValue);
                                ok++;
                            }
                            catch (Exception ex)
                            {
                                fail++;
                                errorLog += string.Format("[出错] id={0}: {1}\n", a.ElementId.IntegerValue, ex.Message);
                            }
                        }
                        var ts = tx.Commit();
                        if (ts != TransactionStatus.Committed)
                        {
                            SafeShow("事务提交失败", "修改已回滚，请检查是否有参数约束不合法。", TaskDialogIcon.TaskDialogIconWarning);
                            return Result.Failed;
                        }
                    }

                    string summary = string.Format(
                        "管件「深圳构件标识」填充完成。\n\n" +
                        "成功写入：{0} 个\n" +
                        "缺少参数「{1}」：{2} 个\n" +
                        "参数只读：{3} 个\n" +
                        "其他失败：{4} 个",
                        ok, TargetParamName, noParam, readOnly, fail);

                    if (!string.IsNullOrWhiteSpace(errorLog))
                    {
                        try
                        {
                            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyRevitAddin_Logs");
                            try { Directory.CreateDirectory(dir); } catch { }
                            string f = Path.Combine(dir, "szmarker_failures_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
                            File.WriteAllText(f, errorLog);
                            summary += "\n\n明细日志：" + f;
                        }
                        catch { }
                    }

                    SafeShow("管件深圳构件标识", summary, TaskDialogIcon.TaskDialogIconInformation);
                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                SafeShow("管件深圳构件标识 - 出错", ex.GetType().Name + ": " + ex.Message, TaskDialogIcon.TaskDialogIconError);
                return Result.Failed;
            }
        }

        // 从名称匹配关键字（优先级按 KeywordMap 顺序：长词先匹配），返回填充值（不是关键字本身）
        internal static string MatchKeyword(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var kv in KeywordMap)
            {
                if (name.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            }
            return null;
        }

        internal static Parameter FindParameterCaseInsensitive(Element e, string name)
        {
            if (string.IsNullOrEmpty(name) || e == null) return null;
            foreach (Parameter p in e.Parameters)
            {
                if (p != null && p.Definition != null &&
                    string.Equals(p.Definition.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            // 回退：如果是实例，再查类型
            ElementType et = null;
            try { et = e.Document.GetElement(e.GetTypeId()) as ElementType; } catch { }
            if (et != null)
            {
                foreach (Parameter p in et.Parameters)
                {
                    if (p != null && p.Definition != null &&
                        string.Equals(p.Definition.Name, name, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
            return null;
        }

        internal static void SetParamString(Parameter p, string val)
        {
            // 参考成功模式：优先 Set(string)
            try
            {
                p.Set(val ?? "");
                return;
            }
            catch { }
            // String StorageType
            try { p.Set(val ?? ""); return; }
            catch { }
            // Fallback：尝试 SetValueString（按显示单位）
            try { p.SetValueString(val ?? ""); return; } catch { }
        }

        private static string SafeTypeName(Element e)
        {
            try
            {
                ElementId tid = e.GetTypeId();
                Element t = e.Document.GetElement(tid);
                return t != null ? (t.Name ?? "") : "";
            }
            catch { return ""; }
        }

        // ============================================================
        // 通用
        // ============================================================
        private static IWin32Window GetRevitOwner(UIApplication uiApp)
        {
            try
            {
                IntPtr h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h != IntPtr.Zero) return new Win32Window(h);
            }
            catch { }
            return null;
        }
        private class Win32Window : IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public Win32Window(IntPtr h) { Handle = h; }
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
    }

    internal class SzMarkerAssignment
    {
        public ElementId ElementId { get; set; }
        public string CategoryName { get; set; }   // 管道管件/风管管件
        public string ElementName { get; set; }    // 类型名称
        public string MatchValue { get; set; }     // 匹配到的关键字（要写入的值）
    }
}
