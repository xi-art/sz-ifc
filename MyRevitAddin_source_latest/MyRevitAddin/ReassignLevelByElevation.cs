using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyRevitAddin
{
    /// <summary>
    /// 将指定两个标高范围内的所有构件，重新归属到下部标高。
    /// 横跨多标高的构件以底部标高为准，判断其归属哪个标高区间。
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ReassignLevelByElevationCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ============================================================
            // 步骤 1：收集所有可标高构件
            // ============================================================
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            var allElements = new List<Element>();
            foreach (var e in collector)
            {
                if (e == null) continue;
                var cat = e.Category;
                if (cat == null) continue;

                // 排除标高、图纸、视图、网格等
                int catId = cat.Id.IntegerValue;
                if (catId == (int)BuiltInCategory.OST_Levels) continue;
                if (catId == (int)BuiltInCategory.OST_Sheets) continue;
                if (catId == (int)BuiltInCategory.OST_Views) continue;
                if (catId == (int)BuiltInCategory.OST_Grids) continue;
                if (catId == (int)BuiltInCategory.OST_Dimensions) continue;
                if (catId == (int)BuiltInCategory.OST_MEPSpaces) continue;

                // 排除结构梁、楼梯、栏杆（复杂，不处理）
                if (catId == (int)BuiltInCategory.OST_StructuralFraming) continue;
                if (catId == (int)BuiltInCategory.OST_Stairs) continue;
                if (catId == (int)BuiltInCategory.OST_Railings) continue;

                // 排除无 Level 参数的元素
                if (!HasLevelParameter(e)) continue;

                allElements.Add(e);
            }

            if (allElements.Count == 0)
            {
                TaskDialog.Show("提示", "文档中没有找到可标高构件。");
                return Result.Cancelled;
            }

            // ============================================================
            // 步骤 2：获取项目中的所有标高
            // ============================================================
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count < 2)
            {
                TaskDialog.Show("提示", "项目标高少于两个，请先创建足够的标高。");
                return Result.Cancelled;
            }

            // ============================================================
            // 步骤 3：弹出对话框让用户选择两个标高
            // ============================================================
            var dialog = new ReassignLevelDialog(levels);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return Result.Cancelled;

            Level lowerLevel = dialog.SelectedLowerLevel;
            Level upperLevel = dialog.SelectedUpperLevel;
            double lowerElev = lowerLevel.Elevation;
            double upperElev = upperLevel.Elevation;

            if (lowerElev >= upperElev)
            {
                TaskDialog.Show("错误", "下部标高必须低于上部标高。");
                return Result.Failed;
            }

            // ============================================================
            // 步骤 4：过滤出在范围内的构件
            // ============================================================
            var candidates = new List<Tuple<Element, double>>();

            foreach (var e in allElements)
            {
                double bottomElev = GetBottomElevation(doc, e);
                // 在 [lowerElev, upperElev) 范围内
                if (!double.IsNaN(bottomElev) && bottomElev >= lowerElev && bottomElev < upperElev)
                {
                    candidates.Add(Tuple.Create(e, bottomElev));
                }
            }

            if (candidates.Count == 0)
            {
                TaskDialog.Show("结果",
                    $"在标高「{lowerLevel.Name}」至「{upperLevel.Name}」之间未找到任何构件。");
                return Result.Succeeded;
            }

            // 预览确认
            string preview =
                $"找到 {candidates.Count} 个构件在「{lowerLevel.Name}」至「{upperLevel.Name}」之间。\n\n" +
                $"下部标高「{lowerLevel.Name}」：{FormatFeet(lowerElev)}\n" +
                $"上部标高「{upperLevel.Name}」：{FormatFeet(upperElev)}\n\n" +
                "确认将所有这些构件的归属标高改为「" + lowerLevel.Name + "」？";

            TaskDialogResult confirm = TaskDialog.Show(
                "确认重新归属标高",
                preview,
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                TaskDialogResult.No);

            if (confirm != TaskDialogResult.Yes)
                return Result.Cancelled;

            // ============================================================
            // 步骤 5：执行重归属
            // ============================================================
            using (Transaction tx = new Transaction(doc, "重新归属构件标高"))
            {
                tx.Start();

                int successCount = 0;
                int failCount = 0;
                var fails = new List<string>();

                foreach (var pair in candidates)
                {
                    Element e = pair.Item1;
                    bool ok = ReassignElementLevel(doc, e, lowerLevel);
                    if (ok)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        if (fails.Count < 10)
                            fails.Add($"  · {e.Category?.Name} / {e.Name} (ID:{e.Id})");
                    }
                }

                tx.Commit();

                string resultMsg = $"操作完成：\n" +
                                   $"  成功重归属：{successCount} 个构件\n" +
                                   $"  失败：{failCount} 个构件";
                if (fails.Count > 0)
                    resultMsg += $"\n\n前 {fails.Count} 个失败项：\n" + string.Join("\n", fails);

                TaskDialog.Show("完成", resultMsg);
            }

            return Result.Succeeded;
        }

        // ============================================================
        // 辅助方法
        // ============================================================

        /// <summary>
        /// 构件是否有标高相关参数
        /// </summary>
        private bool HasLevelParameter(Element e)
        {
            if (e is Wall) return true;
            if (e is Floor) return true;
            if (e is Ceiling) return true;
            if (e is RoofBase) return true;
            if (e is FamilyInstance) return true;

            // 通用查询
            Parameter p = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            if (p != null) return true;
            p = e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            if (p != null) return true;
            p = e.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (p != null) return true;
            return false;
        }

        /// <summary>
        /// 获取构件底部标高（单位：英尺）
        /// </summary>
        private double GetBottomElevation(Document doc, Element e)
        {
            try
            {
                // 方法1：BoundingBox（最准确）
                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb != null && bb.Min != null)
                    return bb.Min.Z;

                // 方法2：Location 点（柱、门、窗、家具等）
                if (e.Location is LocationPoint lp)
                    return lp.Point.Z;

                // 方法3：FAMILY_LEVEL_PARAM
                Parameter levelParam = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (levelParam != null && levelParam.HasValue)
                {
                    ElementId levelId = levelParam.AsElementId();
                    if (levelId != ElementId.InvalidElementId)
                    {
                        Level level = doc.GetElement(levelId) as Level;
                        if (level != null) return level.Elevation;
                    }
                }

                // 方法4：SCHEDULE_LEVEL_PARAM
                Parameter schedParam = e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (schedParam != null && schedParam.HasValue)
                {
                    ElementId levelId = schedParam.AsElementId();
                    if (levelId != ElementId.InvalidElementId)
                    {
                        Level level = doc.GetElement(levelId) as Level;
                        if (level != null) return level.Elevation;
                    }
                }
            }
            catch { }

            return double.NaN;
        }

        /// <summary>
        /// 把构件重归属到新标高
        /// </summary>
        private bool ReassignElementLevel(Document doc, Element e, Level newLevel)
        {
            try
            {
                // === 墙 ===
                if (e is Wall)
                {
                    Wall wall = e as Wall;
                    Parameter baseLevParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    if (baseLevParam != null && !baseLevParam.IsReadOnly)
                    {
                        baseLevParam.Set(newLevel.Id);
                        return true;
                    }
                    // 回退：FAMILY_LEVEL_PARAM
                    Parameter famLev = wall.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (famLev != null && !famLev.IsReadOnly)
                    {
                        famLev.Set(newLevel.Id);
                        return true;
                    }
                    return false;
                }

                // === 楼板 / 天花板 / 屋顶 ===
                if (e is Floor || e is Ceiling || e is RoofBase)
                {
                    Parameter levParam = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (levParam != null && !levParam.IsReadOnly)
                    {
                        levParam.Set(newLevel.Id);
                        return true;
                    }
                    return false;
                }

                // === FamilyInstance（门、窗、家具、柱等）===
                if (e is FamilyInstance)
                {
                    FamilyInstance fi = e as FamilyInstance;

                    // 结构柱 → BaseLevel（Revit 2018）
                    // OST_StructuralColumns = -2001120
                    int catId = fi.Category?.Id?.IntegerValue ?? 0;
                    if (catId == (int)BuiltInCategory.OST_StructuralColumns)
                    {
                        // 尝试结构柱的基准标高参数（Revit 2018 结构柱通过实例参数控制）
                        Parameter baseLev = fi.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                        if (baseLev != null && !baseLev.IsReadOnly)
                        {
                            baseLev.Set(newLevel.Id);
                            return true;
                        }
                        return false;
                    }

                    // 普通构件
                    Parameter famLev = fi.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (famLev != null && !famLev.IsReadOnly)
                    {
                        famLev.Set(newLevel.Id);
                        return true;
                    }
                    Parameter schedLev = fi.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                    if (schedLev != null && !schedLev.IsReadOnly)
                    {
                        schedLev.Set(newLevel.Id);
                        return true;
                    }
                    return false;
                }

                // === 通用回退 ===
                Parameter p = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (p != null && !p.IsReadOnly) { p.Set(newLevel.Id); return true; }
                p = e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (p != null && !p.IsReadOnly) { p.Set(newLevel.Id); return true; }
                p = e.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (p != null && !p.IsReadOnly) { p.Set(newLevel.Id); return true; }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 英尺转米显示
        /// </summary>
        private static string FormatFeet(double feet)
        {
            try
            {
                double meters = UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_METERS);
                return $"{meters:F3} m";
            }
            catch
            {
                return $"{feet:F3} ft";
            }
        }
    }
}
