using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MyRevitAddin
{
    /// <summary>
    /// 批量添加过滤器到视图样板并关闭可见性
    ///
    /// 功能：
    /// 1. 选择一个或多个视图样板
    /// 2. 从项目已有过滤器列表多选
    /// 3. 将选中的过滤器添加到这些样板中，并设置可见性 = 关闭（取消勾选）
    ///
    /// Revit 2020 兼容
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AddFiltersToTemplatesCommand : IExternalCommand
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
                // Step 1: 收集所有视图样板
                // ============================================================
                var allTemplates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate && v.ViewType != ViewType.Internal)
                    .OrderBy(v => v.Name)
                    .ToList();

                if (allTemplates.Count == 0)
                {
                    message = "项目中没有视图样板。";
                    TaskDialog.Show("提示", "项目中没有视图样板。");
                    return Result.Failed;
                }

                // ============================================================
                // Step 2: 收集所有过滤器（ParameterFilterElement + SelectionFilterElement）
                // ============================================================
                var parameterFilters = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .ToList();

                var selectionFilters = new FilteredElementCollector(doc)
                    .OfClass(typeof(SelectionFilterElement))
                    .Cast<SelectionFilterElement>()
                    .ToList();

                var allFilters = new List<Element>();

                // 合并并按名称排序
                allFilters.AddRange(parameterFilters);
                allFilters.AddRange(selectionFilters);
                allFilters = allFilters.OrderBy(f => f.Name).ToList();

                if (allFilters.Count == 0)
                {
                    message = "项目中没有过滤器。请先创建过滤器。";
                    TaskDialog.Show("提示", "项目中没有过滤器。\n\n请先在 Revit 中创建过滤器后再使用此功能。");
                    return Result.Failed;
                }

                // ============================================================
                // Step 3: 显示对话框
                // ============================================================
                var dialog = new AddFiltersToTemplatesDialog(allTemplates, allFilters);
                dialog.StartPosition = FormStartPosition.CenterScreen;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                var selectedTemplates = dialog.SelectedTemplates;
                var selectedFilters = dialog.SelectedFilters;

                if (selectedTemplates.Count == 0 || selectedFilters.Count == 0)
                {
                    message = "未选择视图样板或过滤器。";
                    return Result.Cancelled;
                }

                // ============================================================
                // Step 4: 批量执行
                // ============================================================
                int totalAdded = 0;
                int totalSkipped = 0;
                int totalFailed = 0;

                using (Transaction tx = new Transaction(doc, "批量添加过滤器到视图样板"))
                {
                    tx.Start();

                    foreach (View template in selectedTemplates)
                    {
                        foreach (Element filter in selectedFilters)
                        {
                            try
                            {
                                // 检查样板是否已包含此过滤器
                                var existingFilters = template.GetFilters();
                                if (existingFilters.Contains(filter.Id))
                                {
                                    totalSkipped++;
                                    continue;
                                }

                                // 添加过滤器到样板
                                template.AddFilter(filter.Id);

                                // 设置过滤器可见性 = 关闭（取消勾选）
                                template.SetFilterVisibility(filter.Id, false);

                                totalAdded++;
                            }
                            catch
                            {
                                totalFailed++;
                                // 记录失败原因（可选）
                            }
                        }
                    }

                    tx.Commit();
                }

                // ============================================================
                // Step 5: 显示结果
                // ============================================================
                string resultMsg = $"成功添加 {totalAdded} 个过滤器到 {selectedTemplates.Count} 个视图样板。";
                if (totalSkipped > 0)
                    resultMsg += $"\n跳过 {totalSkipped} 个（已存在）。";
                if (totalFailed > 0)
                    resultMsg += $"\n失败 {totalFailed} 个。";

                TaskDialog.Show("完成", resultMsg);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
