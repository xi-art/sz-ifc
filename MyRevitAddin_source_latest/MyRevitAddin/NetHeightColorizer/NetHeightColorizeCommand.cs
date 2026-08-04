using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.AIAssistant;

namespace MyRevitAddin.NetHeightColorizer
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class NetHeightColorizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            var ctx = new Dictionary<string, object>
            {
                { "doc_title", doc.Title ?? "" },
                { "doc_path", doc.PathName ?? "" },
                { "active_view", doc.ActiveView?.Name ?? "" },
                { "active_view_id", doc.ActiveView?.Id.IntegerValue.ToString() ?? "" }
            };
            HistoryLogger.Operation("NetHeightColorizer:START", ctx, "开始净高图例颜色批量填充");

            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                ICollection<Element> filledRegions = collector.OfClass(typeof(FilledRegion))
                    .WhereElementIsNotElementType()
                    .ToElements();

                ctx["filled_region_count"] = filledRegions.Count;

                if (filledRegions.Count == 0)
                {
                    HistoryLogger.Operation("NetHeightColorizer:CANCELLED-NO-DATA", ctx, "当前视图中未找到 FilledRegion");
                    TaskDialog.Show("净高着色", "当前视图中未找到任何填充区域 (FilledRegion)。");
                    return Result.Cancelled;
                }

                string[] knownParamNames = new string[]
                {
                    "净高", "净空高度", "净空", "高度",
                    "NetHeight", "ClearHeight", "Height"
                };

                Dictionary<ElementId, double> heightMap = new Dictionary<ElementId, double>();
                List<Element> unsupportedElements = new List<Element>();
                List<string> paramNameHits = new List<string>();

                foreach (Element fr in filledRegions)
                {
                    Parameter param = null;
                    string hitName = null;
                    foreach (string name in knownParamNames)
                    {
                        param = fr.LookupParameter(name);
                        if (param != null) { hitName = name; break; }
                    }
                    if (!string.IsNullOrEmpty(hitName) && !paramNameHits.Contains(hitName)) paramNameHits.Add(hitName);

                    if (param != null && param.HasValue)
                    {
                        if (param.StorageType == StorageType.Double)
                        {
                            double heightInternal = param.AsDouble();
                            double heightMM = UnitUtils.ConvertFromInternalUnits(
                                heightInternal, DisplayUnitType.DUT_MILLIMETERS);
                            heightMap[fr.Id] = heightMM;
                        }
                        else if (param.StorageType == StorageType.Integer)
                        {
                            heightMap[fr.Id] = param.AsInteger();
                        }
                        else
                        {
                            unsupportedElements.Add(fr);
                        }
                    }
                    else
                    {
                        unsupportedElements.Add(fr);
                    }
                }

                ctx["matched_param_names"] = string.Join(", ", paramNameHits);
                ctx["height_map_count"] = heightMap.Count;
                ctx["unsupported_elements"] = unsupportedElements.Count;

                if (heightMap.Count == 0)
                {
                    HistoryLogger.Operation("NetHeightColorizer:CANCELLED-NO-H", ctx,
                        "所有填充区域都未找到净高参数，命中参数名=" + (paramNameHits.Count == 0 ? "(无)" : string.Join(", ", paramNameHits)));
                    TaskDialog.Show("净高着色",
                        "填充区域中未找到净高参数。\n" +
                        "请确保填充区域已添加以下任一参数：\n" +
                        "净高 / 净空高度 / 净空 / 高度 / NetHeight / ClearHeight");
                    return Result.Cancelled;
                }

                double minH = heightMap.Values.Min();
                double maxH = heightMap.Values.Max();
                double range = maxH - minH;
                ctx["min_height_mm"] = Math.Round(minH, 2);
                ctx["max_height_mm"] = Math.Round(maxH, 2);
                ctx["range_mm"] = Math.Round(range, 2);

                const double hue = 220.0;
                const double maxLightness = 0.85;
                const double minLightness = 0.20;
                const double maxSaturation = 0.60;
                const double minSaturation = 0.90;

                Dictionary<ElementId, Autodesk.Revit.DB.Color> colorMap = new Dictionary<ElementId, Autodesk.Revit.DB.Color>();

                foreach (var kvp in heightMap)
                {
                    double h = kvp.Value;
                    double t = range > 0.001 ? (h - minH) / range : 0.5;

                    double lightness = maxLightness - t * (maxLightness - minLightness);
                    double saturation = minSaturation + t * (maxSaturation - minSaturation);

                    Autodesk.Revit.DB.Color color = HslToRgb(hue, saturation, lightness);
                    colorMap[kvp.Key] = color;
                }

                ElementId solidFillPatternId = GetSolidFillPatternId(doc);
                ctx["solid_fill_pattern"] = (solidFillPatternId ?? ElementId.InvalidElementId).IntegerValue.ToString();

                using (Transaction trans = new Transaction(doc, "按净高着色填充区域"))
                {
                    trans.Start();

                    int applied = 0;
                    foreach (var kvp in colorMap)
                    {
                        try
                        {
                            OverrideGraphicSettings overrides = doc.ActiveView.GetElementOverrides(kvp.Key);
#if REVIT2018
                            overrides.SetProjectionFillColor(kvp.Value);
                            overrides.SetProjectionFillPatternVisible(true);
                            if (solidFillPatternId != null && solidFillPatternId != ElementId.InvalidElementId)
                            {
                                overrides.SetProjectionFillPatternId(solidFillPatternId);
                            }
#else
                            overrides.SetSurfaceForegroundPatternColor(kvp.Value);
                            overrides.SetSurfaceForegroundPatternVisible(true);
                            if (solidFillPatternId != null && solidFillPatternId != ElementId.InvalidElementId)
                            {
                                overrides.SetSurfaceForegroundPatternId(solidFillPatternId);
                            }
#endif
                            doc.ActiveView.SetElementOverrides(kvp.Key, overrides);
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            HistoryLogger.Error("NetHeightColorizer:SetOverrides(id=" + kvp.Key.IntegerValue + ")", ex);
                        }
                    }

                    trans.Commit();
                    ctx["applied_count"] = applied;
                }

                string summary = string.Format(
                    "成功处理 {0} 个填充区域。\n\n" +
                    "高度范围：{1:F0} mm ~ {2:F0} mm\n" +
                    "未找到参数：{3} 个\n\n" +
                    "颜色方案：蓝色渐变\n" +
                    "最高 → 最浅色\n" +
                    "最低 → 最深色",
                    heightMap.Count, minH, maxH, unsupportedElements.Count);

                HistoryLogger.Operation("NetHeightColorizer:SUCCESS", ctx, summary);
                TaskDialog.Show("净高着色 - 完成", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "净高着色失败: " + ex.Message;
                HistoryLogger.Error("NetHeightColorizer.Execute", ex,
                    "上下文: " + string.Join(" | ", ctx));
                return Result.Failed;
            }
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfClass(typeof(FillPatternElement));
                foreach (FillPatternElement fpe in collector.Cast<FillPatternElement>())
                {
                    FillPattern fp = fpe.GetFillPattern();
                    if (fp != null && fp.Target == FillPatternTarget.Drafting &&
                        string.Equals(fp.Name, "Solid fill", StringComparison.OrdinalIgnoreCase))
                    {
                        return fpe.Id;
                    }
                }
                foreach (FillPatternElement fpe in collector.Cast<FillPatternElement>())
                {
                    FillPattern fp = fpe.GetFillPattern();
                    if (fp != null)
                    {
                        return fpe.Id;
                    }
                }
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        private static Autodesk.Revit.DB.Color HslToRgb(double hue, double saturation, double lightness)
        {
            hue = hue % 360.0;
            if (hue < 0) hue += 360.0;

            double c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
            double x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
            double m = lightness - c / 2.0;

            double r = 0, g = 0, b = 0;

            if (hue < 60.0) { r = c; g = x; b = 0; }
            else if (hue < 120.0) { r = x; g = c; b = 0; }
            else if (hue < 180.0) { r = 0; g = c; b = x; }
            else if (hue < 240.0) { r = 0; g = x; b = c; }
            else if (hue < 300.0) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            byte rr = (byte)Math.Round((r + m) * 255.0);
            byte gg = (byte)Math.Round((g + m) * 255.0);
            byte bb = (byte)Math.Round((b + m) * 255.0);
            return new Autodesk.Revit.DB.Color(rr, gg, bb);
        }

        public static Autodesk.Revit.DB.Color GetGradientColor(double t, double hue)
        {
            const double maxLightness = 0.85;
            const double minLightness = 0.20;
            const double maxSaturation = 0.60;
            const double minSaturation = 0.90;

            double lightness = maxLightness - t * (maxLightness - minLightness);
            double saturation = minSaturation + t * (maxSaturation - minSaturation);

            return HslToRgb(hue, saturation, lightness);
        }
    }
}
