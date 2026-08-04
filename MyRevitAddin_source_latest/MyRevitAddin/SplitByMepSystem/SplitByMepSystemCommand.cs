using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.SplitByMepSystem
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SplitByMepSystemCommand : IExternalCommand
    {
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
            if (doc.IsFamilyDocument)
            {
                message = "此工具只适用于项目文档，不适用于族文档。";
                return Result.Failed;
            }

            try
            {
                // 推断默认输出目录和文件名
                string srcPath = null;
                try { srcPath = doc.PathName; } catch { }
                string defaultDir;
                string defaultBase;
                if (!string.IsNullOrEmpty(srcPath) && File.Exists(srcPath))
                {
                    defaultDir = Path.GetDirectoryName(srcPath);
                    defaultBase = Path.GetFileNameWithoutExtension(srcPath);
                }
                else
                {
                    defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    defaultBase = doc.Title ?? "Model";
                }

                using (var dlg = new SplitByMepSystemDialog(defaultDir, defaultBase))
                {
                    var result = dlg.ShowDialog(GetRevitOwner(uiApp));
                    if (result != DialogResult.OK) return Result.Cancelled;

                    string outDir = dlg.OutputDirectory;
                    string ductFile = Path.Combine(outDir, dlg.DuctFileName);
                    string trayFile = Path.Combine(outDir, dlg.TrayFileName);
                    string pipeFile = Path.Combine(outDir, dlg.PipeFileName);

                    if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
                    {
                        SafeShow("错误", "当前文档从未保存过（没有可复制的源 .rvt 文件）。\n请先保存一次本项目，再运行此工具。", TaskDialogIcon.TaskDialogIconWarning);
                        return Result.Failed;
                    }

                    // 检查输出文件是否已打开（被锁定）
                    foreach (var p in new[] { ductFile, trayFile, pipeFile })
                    {
                        if (File.Exists(p))
                        {
                            try { using (var fs = File.Open(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                            catch
                            {
                                SafeShow("文件被占用",
                                    "目标文件可能已在 Revit 中打开：\n" + p + "\n\n请先关闭所有 Revit 文档后重试。",
                                    TaskDialogIcon.TaskDialogIconWarning);
                                return Result.Failed;
                            }
                        }
                    }

                    // 步骤1：File.Copy 源 RVT → 3 份
                    SafeCopy(srcPath, ductFile);
                    SafeCopy(srcPath, trayFile);
                    SafeCopy(srcPath, pipeFile);

                    // 步骤2：对每份 OpenDocumentFile → 删除不属于本系统的元素 → 保存 → 关闭
                    int dDel, tDel, pDel;
                    Action<string> noop = _ => { };
                    dDel = CleanFile(uiApp, ductFile, SystemKind.Duct, noop);
                    tDel = CleanFile(uiApp, trayFile, SystemKind.Tray, noop);
                    pDel = CleanFile(uiApp, pipeFile, SystemKind.Pipe, noop);

                    SafeShow("按系统分离完成",
                        string.Format("已生成 3 个独立文件：\n\n" +
                                      "风管文件（删除非风管 {0} 项）：\n  {1}\n\n" +
                                      "桥架文件（删除非桥架 {2} 项）：\n  {3}\n\n" +
                                      "水管文件（删除非水管 {4} 项）：\n  {5}\n\n" +
                                      "标高、轴网、视图、图纸、标注、建筑结构构件等均保留在三份中。",
                                      dDel, ductFile, tDel, trayFile, pDel, pipeFile),
                        TaskDialogIcon.TaskDialogIconInformation);

                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                SafeShow("按系统分离模型 - 出错", ex.GetType().Name + ": " + ex.Message, TaskDialogIcon.TaskDialogIconError);
                return Result.Failed;
            }
        }

        // ============================================================
        // 辅助：复制
        // ============================================================
        private static void SafeCopy(string src, string dst)
        {
            if (File.Exists(dst))
            {
                try { File.SetAttributes(dst, FileAttributes.Normal); } catch { }
                try { File.Delete(dst); } catch { }
            }
            var dir = Path.GetDirectoryName(dst);
            if (!Directory.Exists(dir) && !string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Copy(src, dst, true);
        }

        // ============================================================
        // 三种系统分类
        // ============================================================
        internal enum SystemKind
        {
            Duct,   // 风管系统
            Tray,   // 桥架/电气系统
            Pipe    // 水管系统
        }

        // 专属类别（严格属于某一个系统的，直接删）
        private static readonly HashSet<BuiltInCategory> DuctOnly = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctInsulations,
            BuiltInCategory.OST_DuctLinings,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_FlexDuctCurves
        };

        private static readonly HashSet<BuiltInCategory> TrayOnly = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_CommunicationDevices
        };

        private static readonly HashSet<BuiltInCategory> PipeOnly = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_PipeInsulations,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_FlexPipeCurves
        };

        // 设备跨系统类：按参数归属，判断不出就保留
        private static readonly HashSet<BuiltInCategory> EquipmentShared = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_SpecialityEquipment
        };

        // ============================================================
        // 核心：打开一个 RVT 副本，删除不属于该系统的构件，关闭保存
        // ============================================================
        internal static int CleanFile(UIApplication uiApp, string filePath, SystemKind kind, Action<string> progress)
        {
            Document doc = null;
            int totalDeleted = 0;
            try
            {
                progress?.Invoke("正在打开 " + Path.GetFileName(filePath));
                ModelPath mPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                var openOpts = new OpenOptions { DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets };
                try
                {
                    doc = uiApp.Application.OpenDocumentFile(mPath, openOpts);
                }
                catch
                {
                    doc = uiApp.Application.OpenDocumentFile(filePath);
                }

                HashSet<BuiltInCategory> keepOnly;   // 本系统专属（另外两个专属类别要删）
                HashSet<BuiltInCategory> removeA;    // 另外两个专属类别
                HashSet<BuiltInCategory> removeB;
                if (kind == SystemKind.Duct)
                {
                    keepOnly = DuctOnly; removeA = TrayOnly; removeB = PipeOnly;
                }
                else if (kind == SystemKind.Tray)
                {
                    keepOnly = TrayOnly; removeA = DuctOnly; removeB = PipeOnly;
                }
                else
                {
                    keepOnly = PipeOnly; removeA = DuctOnly; removeB = TrayOnly;
                }

                // 第一轮：直接删除另外两个系统的专属类别
                var toDelete = new List<ElementId>(1024);
                foreach (var bic in removeA.Union(removeB))
                {
                    try
                    {
                        var col = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
                        foreach (Element e in col)
                        {
                            // 跳过无法删除的
                            if (e != null && e.Id != null && e.IsValidObject)
                                toDelete.Add(e.Id);
                        }
                    }
                    catch { }
                }
                progress?.Invoke(string.Format("专属类别收集到 {0} 个待删除项", toDelete.Count));

                // 第二轮：处理跨系统设备类（按系统参数归属）
                foreach (var bic in EquipmentShared)
                {
                    try
                    {
                        var col = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
                        foreach (Element e in col)
                        {
                            if (e == null || !e.IsValidObject) continue;
                            SystemKind? belong = ClassifyEquipment(e);
                            if (belong == null)
                            {
                                // 无法判断 → 三份都保留（宁可保留不删除）
                                continue;
                            }
                            if (belong.Value != kind)
                                toDelete.Add(e.Id);
                        }
                    }
                    catch { }
                }

                // 去重 + 批量删除
                var uniq = new List<ElementId>(toDelete.Count);
                var seen = new HashSet<long>();
                foreach (var id in toDelete)
                {
                    if (id == null || id == ElementId.InvalidElementId) continue;
                    long k = ((long)id.IntegerValue << 32);  // 简单整型键
                    if (seen.Contains(k)) continue;
                    seen.Add(k);
                    uniq.Add(id);
                }
                progress?.Invoke(string.Format("开始删除 {0} 个元素", uniq.Count));

                if (uniq.Count > 0)
                {
                    // 每批 500 个事务，避免 OOM
                    int batch = 500;
                    for (int i = 0; i < uniq.Count; i += batch)
                    {
                        int take = Math.Min(batch, uniq.Count - i);
                        var part = uniq.GetRange(i, take);
                        using (var tx = new Transaction(doc, "分离模型-删除批次 " + i))
                        {
                            tx.Start();
                            try
                            {
                                ICollection<ElementId> deleted = doc.Delete(part);
                                totalDeleted += deleted != null ? deleted.Count : 0;
                            }
                            catch
                            {
                                // 批次失败 → 逐个删
                                foreach (var id in part)
                                {
                                    try { doc.Delete(id); totalDeleted++; } catch { }
                                }
                            }
                            tx.Commit();
                        }
                        progress?.Invoke(string.Format("已删除 {0}/{1}", Math.Min(i + batch, uniq.Count), uniq.Count));
                    }
                }

                // 保存 + 关闭
                progress?.Invoke("保存文件");
                try
                {
                    var so = new SaveAsOptions { OverwriteExistingFile = true };
                    doc.SaveAs(filePath, so);
                }
                catch
                {
                    doc.Save();
                }
            }
            finally
            {
                try { doc?.Close(false); } catch { }
            }
            return totalDeleted;
        }

        // 根据元素是否有各系统参数，判断归属（返回 null 表示无法判断，保留）
        private static SystemKind? ClassifyEquipment(Element e)
        {
            bool hasDuct = false;
            bool hasPipe = false;
            bool hasTray = false;
            try
            {
                // 风管：优先用系统类型参数
                try { var p = e.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM); if (p != null && p.StorageType != StorageType.None) hasDuct = true; } catch { }
                // 水管：优先用系统类型参数
                try { var p = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM); if (p != null && p.StorageType != StorageType.None) hasPipe = true; } catch { }
                // 桥架：找电气相关参数（按名称判断更通用，避免枚举值不兼容）
                try
                {
                    var names = new[] { "电气系统", "系统", "电路", "额定电压", "功率", "回路", "配电盘" };
                    foreach (Parameter p in e.Parameters)
                    {
                        if (p == null || p.Definition == null) continue;
                        string n = p.Definition.Name ?? "";
                        foreach (var kw in names)
                        {
                            if (n.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 && p.StorageType != StorageType.None)
                            {
                                hasTray = true;
                                break;
                            }
                        }
                        if (hasTray) break;
                    }
                }
                catch { }
            }
            catch { }

            int cnt = (hasDuct ? 1 : 0) + (hasPipe ? 1 : 0) + (hasTray ? 1 : 0);
            if (cnt == 0) return null;
            if (cnt >= 2)
            {
                // 多系统共用（如空调机组同时有风管+水管）→ 保留在所有三份，避免删错
                return null;
            }
            if (hasDuct) return SystemKind.Duct;
            if (hasTray) return SystemKind.Tray;
            return SystemKind.Pipe;
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
}
