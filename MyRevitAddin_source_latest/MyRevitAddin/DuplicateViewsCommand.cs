using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MyRevitAddin
{
    /// <summary>
    /// 批量复制视图并设置视图样板
    /// 
    /// 核心逻辑：
    /// 1. 用户选中多个源视图（如 F01, F02...）
    /// 2. 对每个源视图，按选中的视图样板各复制一份
    /// 3. 新视图命名：{原视图名}（{视图样板名}）
    /// 4. 自动设置对应的视图样板
    /// 
    /// Revit 2020 兼容
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DuplicateViewsCommand : IExternalCommand
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
                // Step 1: 收集所有视图并让用户多选源视图
                // ============================================================
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                                && v.ViewType != ViewType.Internal
                                && v.ViewType != ViewType.Undefined)
                    .OrderBy(v => v.Name)
                    .ToList();

                if (allViews.Count == 0)
                {
                    message = "项目中没有可复制的视图。";
                    return Result.Failed;
                }

                // ============================================================
                // Step 2: 显示对话框
                // ============================================================
                var dialog = new DuplicateViewsDialog(doc, allViews);
                dialog.StartPosition = FormStartPosition.CenterScreen;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                var viewTemplates = dialog.SelectedTemplates;
                var namingPattern = dialog.NamingPattern;
                bool copyOncePerTemplate = dialog.CopyOncePerTemplate;

                if (viewTemplates.Count == 0)
                {
                    message = "未选择任何视图样板。";
                    return Result.Cancelled;
                }

                // ============================================================
                // Step 3: 批量执行
                // ============================================================
                int totalCreated = 0;
                int totalFailed = 0;
                var errors = new List<string>();

                using (Transaction tx = new Transaction(doc, "批量复制视图并设置样板"))
                {
                    tx.Start();

                    foreach (View sourceView in dialog.SelectedViews)
                    {

                        foreach (View template in viewTemplates)
                        {
                            // 复制视图
                            ElementId newViewId;
                            try
                            {
                                newViewId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
                                if (newViewId == ElementId.InvalidElementId)
                                {
                                    totalFailed++;
                                    errors.Add($"  {sourceView.Name} 复制失败");
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                totalFailed++;
                                errors.Add($"  {sourceView.Name} 复制异常: {ex.Message}");
                                continue;
                            }

                            View newView = doc.GetElement(newViewId) as View;
                            if (newView == null)
                            {
                                totalFailed++;
                                continue;
                            }

                            // 设置新视图名称
                            string newName = BuildViewName(sourceView.Name, template.Name, namingPattern);
                            try { newView.Name = newName; }
                            catch
                            {
                                // 名称冲突，加序号后缀
                                for (int suffix = 1; suffix <= 99; suffix++)
                                {
                                    try
                                    {
                                        newView.Name = $"{newName}_{suffix}";
                                        break;
                                    }
                                    catch { }
                                }
                            }

                            // 设置视图样板
                            bool templateSet = TrySetViewTemplate(doc, newView, template);

                            totalCreated++;
                        }
                    }

                    if (totalCreated == 0)
                    {
                        tx.RollBack();
                        message = "未能创建任何视图。";
                        return Result.Failed;
                    }

                    tx.Commit();
                }

                // ============================================================
                // Step 4: 显示结果
                // ============================================================
                string resultMsg = $"成功复制 {totalCreated} 个视图。";
                if (totalFailed > 0)
                    resultMsg += $"\n失败 {totalFailed} 个。";
                if (errors.Count > 0 && errors.Count <= 20)
                    resultMsg += "\n\n失败详情:\n" + string.Join("\n", errors.Take(20));

                TaskDialog.Show("完成", resultMsg);
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

        /// <summary>
        /// 根据命名规则生成新视图名称
        /// </summary>
        private string BuildViewName(string sourceName, string templateName, string pattern)
        {
            // pattern 支持: {view}（{template}）、{view}-{template}、{view}_{template}
            return pattern
                .Replace("{view}", sourceName)
                .Replace("{template}", templateName);
        }

        /// <summary>
        /// 通过内置参数设置视图样板（Revit 2020 标准 API）
        /// </summary>
        private bool TrySetViewTemplate(Document doc, View view, View template)
        {
            try
            {
                // 遍历所有参数，查找视图样板参数
                foreach (Parameter p in view.Parameters)
                {
                    if (p.Definition == null) continue;
                    string name = p.Definition.Name;
                    // 视图样板参数名（中英文兼容）
                    if (name.Contains("视图样板") || name.Contains("View Template") ||
                        name.Contains("样板") || name.Contains("Template"))
                    {
                        if (!p.IsReadOnly && p.StorageType == StorageType.ElementId)
                        {
                            p.Set(template.Id);
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
