using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MyRevitAddin
{
    /// <summary>
    /// 批量复制图纸与视图
    /// 参照 PowerSheets / SmartViews - Duplicate Sheets with Views
    /// 
    /// 功能：
    /// 1. 在项目浏览器中选择一张或多张图纸（Ctrl+点击多选）
    /// 2. 设置 Sheet 前缀/视图前缀、复制数量、编号规则
    /// 3. 预览表格：每个视图可原样复制，或替换成其他视图
    /// 4. 批量生成新图纸，视图同步放置到原位置
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DuplicateSheetsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ============================================================
                // Step 1: 让用户在项目浏览器中选择图纸
                // ============================================================
                IList<ElementId> selectedSheetIds = null;

                // PickObjects: 直接选择元素，无版本兼容问题
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new SheetSelectionFilter(),
                    "请在项目浏览器中选择要复制的图纸（Ctrl+点击多选）");
                selectedSheetIds = refs.Select(r => r.ElementId).ToList();

                if (selectedSheetIds == null || selectedSheetIds.Count == 0)
                {
                    message = "未选择任何图纸。";
                    return Result.Cancelled;
                }

                // ============================================================
                // Step 2: 显示配置对话框
                // ============================================================
                var dialog = new DuplicateSheetsDialog(doc, selectedSheetIds.ToList());
                dialog.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                string sheetPrefix = dialog.SheetPrefix;
                string viewPrefix = dialog.ViewPrefix;
                int copyCount = dialog.CopyCount;
                bool usePrefix = dialog.UsePrefixForNumbering;
                var viewReplacements = dialog.ViewReplacements;

                // ============================================================
                // Step 3: 批量执行复制
                // ============================================================
                int totalCreated = 0;

                using (Transaction tx = new Transaction(doc,
                    $"批量复制 {selectedSheetIds.Count} 张图纸"))
                {
                    tx.Start();

                    foreach (ElementId sourceSheetId in selectedSheetIds)
                    {
                        ViewSheet sourceSheet = doc.GetElement(sourceSheetId) as ViewSheet;
                        if (sourceSheet == null) continue;

                        var viewportIds = sourceSheet.GetAllViewports();
                        var viewports = viewportIds
                            .Select(vpId => doc.GetElement(vpId) as Viewport)
                            .Where(vp => vp != null)
                            .ToList();

                        for (int copyIdx = 0; copyIdx < copyCount; copyIdx++)
                        {
                            // --- 创建新图纸 ---
                            // Revit API: ViewSheet.Create(doc, titleBlockId)
                            ViewSheet newSheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                            ElementId newSheetId = newSheet.Id;

                            // 设置图纸编号
                            string newSheetNumber = BuildSheetNumber(
                                sourceSheet.SheetNumber, sheetPrefix,
                                copyIdx + 1, copyCount, usePrefix);
                            try { newSheet.SheetNumber = newSheetNumber; }
                            catch { newSheet.SheetNumber = $"{newSheetNumber}_{copyIdx + 1}"; }

                            // 设置图纸名称
                            string newSheetName = BuildSheetName(sourceSheet.Name, sheetPrefix, usePrefix);
                            try { newSheet.Name = newSheetName; }
                            catch { newSheet.Name = $"{newSheetName}_{copyIdx + 1}"; }

                            // --- 复制视图并放置 ---
                            foreach (Viewport srcViewport in viewports)
                            {
                                ElementId srcViewId = srcViewport.ViewId;
                                View srcView = doc.GetElement(srcViewId) as View;
                                if (srcView == null) continue;

                                // 替换视图逻辑
                                ElementId targetViewId = srcViewId;
                                if (viewReplacements != null &&
                                    viewReplacements.TryGetValue((sourceSheetId, srcViewId), out var replacement) &&
                                    replacement != ElementId.InvalidElementId)
                                {
                                    targetViewId = replacement;
                                }

                                // 复制视图
                                ElementId dupViewId = DuplicateView(
                                    doc, targetViewId, viewPrefix, copyIdx + 1, copyCount);
                                if (dupViewId == ElementId.InvalidElementId) continue;

                                // 获取视口在图纸上的位置
                                UV vpCenter = GetViewportCenterOnSheet(srcViewport);

                                // 在新图纸上创建视口
                                try
                                {
                                    XYZ pos = new XYZ(vpCenter.U, vpCenter.V, 0);
                                    Viewport newVp = Viewport.Create(doc, newSheetId, dupViewId, pos);
                                    TryCopyViewportRotation(srcViewport, newVp);
                                }
                                catch
                                {
                                    // 不支持的视图类型跳过
                                }
                            }

                            totalCreated++;
                        }
                    }

                    if (totalCreated == 0)
                    {
                        tx.RollBack();
                        message = "未能创建任何图纸。";
                        return Result.Failed;
                    }

                    tx.Commit();
                }

                TaskDialog.Show("完成", $"成功创建 {totalCreated} 张图纸。");
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private ElementId DuplicateView(
            Document doc, ElementId sourceViewId,
            string prefix, int copyIdx, int totalCopies)
        {
            View src = doc.GetElement(sourceViewId) as View;
            if (src == null) return ElementId.InvalidElementId;

            try
            {
                ElementId newId = src.Duplicate(ViewDuplicateOption.Duplicate);
                if (newId == ElementId.InvalidElementId) return ElementId.InvalidElementId;

                View dst = doc.GetElement(newId) as View;
                if (dst == null) return ElementId.InvalidElementId;

                string newName = BuildViewName(src.Name, prefix, copyIdx, totalCopies);
                try { dst.Name = newName; }
                catch { dst.Name = $"{newName}_{copyIdx}"; }

                return newId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private string BuildSheetNumber(string original, string prefix, int idx, int total, bool usePrefix)
        {
            if (usePrefix)
            {
                string num = $"{prefix}{original}";
                return total > 1 ? $"{num}_{idx}" : num;
            }
            return total > 1
                ? $"{original} - Copy {idx}"
                : $"{original} - Copy";
        }

        private string BuildSheetName(string original, string prefix, bool usePrefix)
        {
            return usePrefix ? $"{prefix}{original}" : original;
        }

        private string BuildViewName(string original, string prefix, int idx, int total)
        {
            if (!string.IsNullOrWhiteSpace(prefix))
                return $"{prefix}{original}";
            return total > 1
                ? $"{original} - Copy {idx}"
                : $"{original} - Copy";
        }

        /// <summary>
        /// 获取视口在图纸上的中心位置
        /// </summary>
        private UV GetViewportCenterOnSheet(Viewport viewport)
        {
            try
            {
                // Revit 2020 Viewport.get_BoundingBox 返回 BoundingBoxXYZ
                BoundingBoxXYZ bboxXyz = viewport.get_BoundingBox(null);
                if (bboxXyz != null)
                {
                    // 取投影中心（XY），Z 设为 0
                    XYZ center = (bboxXyz.Min + bboxXyz.Max) / 2.0;
                    return new UV(center.X, center.Y);
                }
            }
            catch { }
            return new UV(0, 0);
        }

        /// <summary>
        /// 复制视口旋转参数（遍历参数列表匹配）
        /// </summary>
        private void TryCopyViewportRotation(Viewport src, Viewport tgt)
        {
            try
            {
                foreach (Parameter srcP in src.Parameters)
                {
                    if (srcP.Definition == null) continue;
                    string name = srcP.Definition.Name;
                    // 旋转/角度相关参数
                    if (!name.Contains("旋转") && !name.Contains("Rotation") &&
                        !name.Contains("Angle") && !name.Contains("角"))
                        continue;

                    try
                    {
                        // 通过参数名在目标元素中查找对应参数
                        var tgtParams = tgt.GetParameters(srcP.Definition.Name);
                        if (tgtParams != null && tgtParams.Count > 0)
                        {
                            Parameter tgtP = tgtParams.First();
                            if (!tgtP.IsReadOnly && tgtP.StorageType == srcP.StorageType)
                            {
                                if (srcP.StorageType == StorageType.Double)
                                    tgtP.Set(srcP.AsDouble());
                                else if (srcP.StorageType == StorageType.Integer)
                                    tgtP.Set(srcP.AsInteger());
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 图纸选择过滤器（用于 PickObjects 降级方案）
    /// </summary>
    public class SheetSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is ViewSheet;
        // Revit 2020 ISelectionFilter: AllowReference(Reference, XYZ)
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
