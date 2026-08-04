using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// Revit 操作执行器：解析 AI 返回的工具调用，执行对应操作
    /// </summary>
    /// <remarks>
    /// 不要缓存 UIDocument/Document——Revit 会在文档关闭后使缓存引用失效。
    /// 每次执行时从 AIAssistantState.CurrentUIDoc 动态获取（内部通过 UIApplication.ActiveUIDocument 获取，永远有效）。
    /// </remarks>
    public class RevitOperationExecutor
    {
        private readonly AssistantMemory _memory;

        public RevitOperationExecutor(UIDocument uidoc)
        {
            // uidoc 参数仅用于向后兼容，实际执行时会被忽略（从 AIAssistantState 重新获取）
            _memory = new AssistantMemory();
        }

        /// <summary>
        /// 获取当前有效的 UIDocument（每次重新获取，不缓存）
        /// 诊断版：含详细错误信息，并从 SharedUIApp 备份恢复
        /// </summary>
        private UIDocument GetUIDocument()
        {
            try
            {
                // 优先使用 AIAssistantState.CurrentUIApp
                if (AIAssistantState.CurrentUIApp != null)
                {
                    var uidoc = AIAssistantState.CurrentUIApp.ActiveUIDocument;
                    if (uidoc != null) return uidoc;
                }

                // 备份：从 AIAssistantPane.SharedUIApp 恢复
                if (AIAssistantPane.SharedUIApp != null)
                {
                    AIAssistantState.CurrentUIApp = AIAssistantPane.SharedUIApp;
                    var uidoc = AIAssistantPane.SharedUIApp.ActiveUIDocument;
                    if (uidoc != null) return uidoc;
                }

                throw new Exception("AIAssistantState.CurrentUIApp 和 AIAssistantPane.SharedUIApp 均为 null，请重新点击一次【AI 助手】按钮以刷新状态");
            }
            catch (Exception ex)
            {
                throw new Exception("获取 UIDocument 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 获取当前有效的 Document
        /// </summary>
        private Document GetDocument()
        {
            var uidoc = GetUIDocument();
            return uidoc?.Document;
        }

        /// <summary>
        /// 执行前检查：确保有有效的 UIDocument 和 Document
        /// 诊断版：抛出异常而非返回 false
        /// </summary>
        private void CheckDocument(out UIDocument uidoc, out Document doc)
        {
            uidoc = GetUIDocument();  // 如果失败会抛异常
            doc = uidoc.Document;
            if (doc == null || !doc.IsValidObject)
            {
                throw new Exception("Document 无效: doc=" + (doc == null ? "null" : "IsValidObject=" + doc.IsValidObject));
            }
        }

        /// <summary>
        /// 执行 AI 工具调用，返回执行结果（JSON 字符串）
        /// </summary>
        public string Execute(Dictionary<string, object> toolCall)
        {
            string funcName = "(unknown)";
            string argsJson = "";
            try
            {
                var function = toolCall["function"] as Dictionary<string, object>;
                funcName = function["name"] as string;
                argsJson = function["arguments"] as string;

                var args = ParseJsonObject(argsJson);

                string result = funcName switch
                {
                    "get_selected_elements" => GetSelectedElements(),
                    "get_document_info" => GetDocumentInfo(),
                    "get_all_levels" => GetAllLevels(),
                    "get_all_views" => GetAllViews(args),
                    "select_elements_by_category" => SelectElementsByCategory(args),
                    "get_project_sheets" => GetProjectSheets(),
                    "set_instance_parameter" => SetInstanceParameter(args),
                    "batch_set_parameter" => BatchSetParameter(args),
                    "replace_family" => ReplaceFamily(args),
                    "save_memory" => SaveMemory(args),
                    "search_memory" => SearchMemory(args),
                    _ => "{\"error\": \"未知函数: " + funcName + "\"}"
                };

                HistoryLogger.Tool(funcName, argsJson, result, "AI-decided");
                _memory?.SaveOperation(funcName, args, result);
                return result;
            }
            catch (Exception ex)
            {
                HistoryLogger.Error("RevitOperationExecutor.Execute(" + funcName + ")", ex, "参数: " + argsJson);
                string err = "{\"error\": \"" + ex.GetType().Name + ": " + ex.Message + "\"}";
                HistoryLogger.Tool(funcName, argsJson, err, "AI-decided(FAIL)");
                return err;
            }
        }

        /// <summary>
        /// 直接执行：传入工具名和参数字典（前置自动查询、兜底调用时使用，无需构造 AI toolCall 格式）
        /// </summary>
        public string ExecuteDirect(string funcName, Dictionary<string, object> args)
        {
            string argsJson = "";
            try
            {
                if (args == null) args = new Dictionary<string, object>();
                try { argsJson = DictionaryToJson(new Dictionary<string, object> { { "args", args } }); } catch { }
                string result;
                switch (funcName)
                {
                    case "get_selected_elements": result = GetSelectedElements(); break;
                    case "get_document_info": result = GetDocumentInfo(); break;
                    case "get_all_levels": result = GetAllLevels(); break;
                    case "get_all_views": result = GetAllViews(args); break;
                    case "select_elements_by_category": result = SelectElementsByCategory(args); break;
                    case "get_project_sheets": result = GetProjectSheets(); break;
                    case "set_instance_parameter": result = SetInstanceParameter(args); break;
                    case "batch_set_parameter": result = BatchSetParameter(args); break;
                    case "replace_family": result = ReplaceFamily(args); break;
                    case "save_memory": result = SaveMemory(args); break;
                    case "search_memory": result = SearchMemory(args); break;
                    default: result = "{\"error\": \"未知函数: " + funcName + "\"}"; break;
                }
                HistoryLogger.Tool(funcName, argsJson, result, "pre-autorun");
                _memory?.SaveOperation(funcName, args, result);
                return result;
            }
            catch (Exception ex)
            {
                HistoryLogger.Error("RevitOperationExecutor.ExecuteDirect(" + funcName + ")", ex, "参数: " + argsJson);
                string err = "{\"error\": \"" + ex.GetType().Name + ": " + ex.Message + "\"}";
                HistoryLogger.Tool(funcName, argsJson, err, "pre-autorun(FAIL)");
                return err;
            }
        }

        // ==================== 具体操作方法 ====================

        private string GetSelectedElements()
        {
            CheckDocument(out UIDocument uidoc, out Document doc);  // 如果失败会抛异常（由 Execute 的 try-catch 处理）

            var selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
                return "{\"count\": 0, \"message\": \"没有选中任何图元\"}";

            var elements = new List<Dictionary<string, object>>();
            foreach (var id in selectedIds)
            {
                var e = doc.GetElement(id);
                elements.Add(new Dictionary<string, object>
                {
                    { "id", e.Id.IntegerValue },
                    { "name", e.Name ?? "(未命名)" },
                    { "category", e.Category?.Name ?? "(无类别)" }
                });
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "count", elements.Count },
                { "elements", elements }
            });
        }

        private string GetDocumentInfo()
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            string rawTitle = doc.Title ?? "";
            string rawPath = doc.PathName ?? "";
            bool isSaved = !string.IsNullOrEmpty(rawPath);
            string displayTitle = isSaved ? rawTitle : "(项目尚未保存，临时标题：" + (string.IsNullOrEmpty(rawTitle) ? "Project" : rawTitle) + ")";
            string displayPath = isSaved ? rawPath : "(文件未保存——请先在 Revit 里按 Ctrl+S 另存为 .rvt 项目文件，之后即可显示完整路径)";

            var info = new Dictionary<string, object>
            {
                { "title", displayTitle },
                { "title_raw", rawTitle },
                { "path_name", displayPath },
                { "path_name_raw", rawPath },
                { "file_name", isSaved ? (Path.GetFileName(rawPath) ?? "(无法解析)") : "(未保存.rvt)" },
                { "folder", isSaved ? (Path.GetDirectoryName(rawPath) ?? "(无法解析)") : "(未保存，无文件夹)" },
                { "is_saved", isSaved },
                { "save_status", isSaved ? "已保存" : "未保存 - 当前文件名和路径为空，保存后才能显示" },
                { "is_readonly", doc.IsReadOnly },
                { "is_workshared", doc.IsWorkshared },
                { "document_type", doc.IsFamilyDocument ? "族文档 (.rfa)" : "项目文档 (.rvt)" }
            };

            try
            {
                ModelPath mp = doc.GetWorksharingCentralModelPath();
                if (mp != null)
                {
                    string central = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                    info["central_model_path"] = central ?? "";
                    info["is_linked_central"] = !string.IsNullOrEmpty(central);
                }
            }
            catch { info["central_model_path"] = "(非工作共享文档或不支持)"; }

            try
            {
                if (doc.Application != null)
                {
                    info["revit_version"] = doc.Application.VersionName + " (build " + doc.Application.VersionBuild + ")";
                    info["revit_language"] = doc.Application.Language.ToString();
                }
            }
            catch { }

            try
            {
                var projectInfo = new FilteredElementCollector(doc)
                    .OfClass(typeof(ProjectInfo))
                    .FirstElement() as ProjectInfo;
                if (projectInfo != null)
                {
                    Parameter p;
                    p = projectInfo.LookupParameter("Organization Name");
                    info["organization_name"] = (p != null && p.HasValue) ? (p.AsString() ?? p.AsValueString() ?? "") : "";
                    p = projectInfo.LookupParameter("Client Name");
                    info["client_name"] = (p != null && p.HasValue) ? (p.AsString() ?? p.AsValueString() ?? "") : "";
                    p = projectInfo.LookupParameter("Project Number");
                    info["project_number"] = (p != null && p.HasValue) ? (p.AsString() ?? p.AsValueString() ?? "") : "";
                    p = projectInfo.LookupParameter("Project Status");
                    info["project_status"] = (p != null && p.HasValue) ? (p.AsString() ?? p.AsValueString() ?? "") : "";
                }
            }
            catch { }

            try
            {
                int totalElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                info["total_elements_count"] = totalElements;
            }
            catch { }

            string humanSummary =
                "【Revit 文档快速摘要 - AI 可直接使用以下自然语言回答】" +
                "\n  • 文档名称: " + displayTitle +
                "\n  • 完整路径: " + displayPath +
                "\n  • 保存状态: " + (isSaved ? "已保存 ✓" : "尚未保存 ✗（请先 Ctrl+S 保存，文件名/路径就会出现）") +
                "\n  • 文档类型: " + (doc.IsFamilyDocument ? "族 (.rfa)" : "项目 (.rvt)") +
                (doc.IsWorkshared ? "\n  • 工作共享: 是" : "\n  • 工作共享: 否") +
                (doc.IsReadOnly ? "（只读）" : "") +
                (info.ContainsKey("central_model_path") ? "\n  • 中心文件: " + info["central_model_path"] : "") +
                (info.ContainsKey("total_elements_count") ? "\n  • 总图元数: " + info["total_elements_count"] : "");

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "success", true },
                { "document", info },
                { "ai_ready_summary", humanSummary }
            });
        }

        private string GetAllLevels()
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0)
                return "{\"count\": 0, \"message\": \"项目中没有标高\"}";

            var levelList = new List<Dictionary<string, object>>();
            foreach (var lv in levels)
            {
                double elevMeters = UnitUtils.ConvertFromInternalUnits(lv.Elevation, DisplayUnitType.DUT_METERS);
                levelList.Add(new Dictionary<string, object>
                {
                    { "id", lv.Id.IntegerValue },
                    { "name", lv.Name ?? "(未命名)" },
                    { "elevation_ft", Math.Round(lv.Elevation, 4) },
                    { "elevation_m", Math.Round(elevMeters, 3) },
                    { "elevation_display", elevMeters.ToString("F3") + " m" }
                });
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "count", levelList.Count },
                { "levels", levelList }
            });
        }

        private string GetAllViews(Dictionary<string, object> args)
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            string viewTypeFilter = args.ContainsKey("view_type") ? (args["view_type"] as string ?? "") : "All";

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Internal && v.ViewType != ViewType.Undefined)
                .ToList();

            // 按视图类型过滤
            if (!string.IsNullOrEmpty(viewTypeFilter) && !viewTypeFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                ViewType? vt = TryParseViewType(viewTypeFilter);
                if (vt.HasValue)
                    views = views.Where(v => v.ViewType == vt.Value).ToList();
            }

            views = views.OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name).ToList();

            var viewList = new List<Dictionary<string, object>>();
            foreach (var v in views)
            {
                viewList.Add(new Dictionary<string, object>
                {
                    { "id", v.Id.IntegerValue },
                    { "name", v.Name ?? "(未命名)" },
                    { "view_type", v.ViewType.ToString() },
                    { "view_type_label", GetViewTypeLabel(v.ViewType) },
                    { "is_sheet", v.ViewType == ViewType.DrawingSheet }
                });
            }

            // 统计各类型数量
            var byType = viewList.GroupBy(x => x["view_type_label"].ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "count", viewList.Count },
                { "filter", string.IsNullOrEmpty(viewTypeFilter) ? "All" : viewTypeFilter },
                { "count_by_type", byType },
                { "views", viewList.Take(200).ToList() },
                { "note", viewList.Count > 200 ? $"仅返回前 200 条（共 {viewList.Count} 条）" : null }
            });
        }

        private string SelectElementsByCategory(Dictionary<string, object> args)
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            string catName = args.ContainsKey("category_name") ? (args["category_name"] as string ?? "").Trim() : "";
            int limit = 500;
            if (args.ContainsKey("limit") && args["limit"] != null)
                int.TryParse(args["limit"].ToString(), out limit);

            if (string.IsNullOrEmpty(catName))
                return "{\"error\": \"必须指定类别名称\"}";

            // 中文名 -> BuiltInCategory / 类别名匹配
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            List<Element> matched = new List<Element>();

            string catNameLower = catName.ToLower();

            foreach (var e in collector)
            {
                var cat = e.Category;
                if (cat == null) continue;
                if (string.IsNullOrEmpty(cat.Name)) continue;

                // 精确匹配类别名或包含匹配（中英文都试试）
                if (cat.Name.Equals(catName, StringComparison.OrdinalIgnoreCase) ||
                    cat.Name.ToLower().Contains(catNameLower) ||
                    CategoryKeywordsMatch(cat.Name, catNameLower))
                {
                    matched.Add(e);
                    if (matched.Count >= limit) break;
                }
            }

            // 如果没找到，尝试按 BuiltInCategory 枚举名匹配
            if (matched.Count == 0)
            {
                BuiltInCategory? bic = TryParseBuiltInCategory(catName);
                if (bic.HasValue)
                {
                    matched = new FilteredElementCollector(doc)
                        .OfCategory(bic.Value)
                        .WhereElementIsNotElementType()
                        .Take(limit)
                        .ToList();
                }
            }

            // 在 Revit 界面中选中匹配到的元素
            if (matched.Count > 0)
            {
                try
                {
                    var ids = matched.Select(e => e.Id).ToList();
                    uidoc.Selection.SetElementIds(ids);
                }
                catch { /* 选中失败不影响返回 */ }
            }

            // 只返回摘要，避免 JSON 太大
            var summary = new List<Dictionary<string, object>>();
            int showCount = Math.Min(matched.Count, 50);
            for (int i = 0; i < showCount; i++)
            {
                var e = matched[i];
                summary.Add(new Dictionary<string, object>
                {
                    { "id", e.Id.IntegerValue },
                    { "name", e.Name ?? "(未命名)" },
                    { "category", e.Category?.Name ?? "" }
                });
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "success", true },
                { "count", matched.Count },
                { "category_searched", catName },
                { "selected_in_revit", matched.Count > 0 },
                { "sample_elements", summary },
                { "note", matched.Count > showCount ? $"仅展示前 {showCount} 个示例（共 {matched.Count} 个）" : null }
            });
        }

        private string GetProjectSheets()
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
                return "{\"count\": 0, \"message\": \"项目中没有图纸\"}";

            var sheetList = new List<Dictionary<string, object>>();
            foreach (var s in sheets)
            {
                var vpIds = s.GetAllViewports();
                sheetList.Add(new Dictionary<string, object>
                {
                    { "id", s.Id.IntegerValue },
                    { "sheet_number", s.SheetNumber ?? "" },
                    { "sheet_name", s.Name ?? "(未命名)" },
                    { "viewport_count", vpIds != null ? vpIds.Count : 0 },
                    { "full_title", (s.SheetNumber ?? "") + " - " + (s.Name ?? "") }
                });
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "count", sheetList.Count },
                { "sheets", sheetList.Take(100).ToList() },
                { "note", sheetList.Count > 100 ? $"仅返回前 100 张（共 {sheetList.Count} 张）" : null }
            });
        }

        // ==================== 辅助方法（视图类型、类别关键词匹配等） ====================

        private static ViewType? TryParseViewType(string s)
        {
            try
            {
                // 先按英文枚举名
                if (Enum.TryParse<ViewType>(s, true, out var vt))
                    return vt;
                // 再按中文常见映射
                switch (s.Trim())
                {
                    case "平面": case "楼层平面": case "FloorPlan": return ViewType.FloorPlan;
                    case "天花": case "顶棚": case "CeilingPlan": return ViewType.CeilingPlan;
                    case "3D": case "三维": case "ThreeD": return ViewType.ThreeD;
                    case "剖面": case "Section": return ViewType.Section;
                    case "立面": case "Elevation": return ViewType.Elevation;
                    case "图纸": case "Sheet": case "DrawingSheet": return ViewType.DrawingSheet;
                    case "详图": case "Detail": return ViewType.Detail;
                    case "明细表": case "Schedule": return ViewType.Schedule;
                    case "图例": case "Legend": return ViewType.Legend;
                    case "漫游": case "Walkthrough": return ViewType.Walkthrough;
                }
            }
            catch { }
            return null;
        }

        private static string GetViewTypeLabel(ViewType vt)
        {
            switch (vt)
            {
                case ViewType.FloorPlan: return "[平面]";
                case ViewType.CeilingPlan: return "[天花]";
                case ViewType.Section: return "[剖面]";
                case ViewType.Elevation: return "[立面]";
                case ViewType.ThreeD: return "[3D]";
                case ViewType.Detail: return "[详图]";
                case ViewType.Legend: return "[图例]";
                case ViewType.DrawingSheet: return "[图纸]";
                case ViewType.Schedule: return "[明细表]";
                case ViewType.Report: return "[报告]";
                case ViewType.Walkthrough: return "[漫游]";
                default: return "[?]";
            }
        }

        private static bool CategoryKeywordsMatch(string catName, string keywordLower)
        {
            // 常见类别同义词映射（中文建筑/机电常用词）
            var map = new Dictionary<string, string[]>
            {
                { "门", new[] { "门", "doors", "門" } },
                { "窗", new[] { "窗", "windows", "窗戶" } },
                { "墙", new[] { "墙", "walls", "牆", "墙体", "基本墙" } },
                { "楼板", new[] { "楼板", "floors", "樓板", "地板", "floor" } },
                { "柱", new[] { "柱", "columns", "柱结构", "structure columns" } },
                { "梁", new[] { "梁", "beams", "結構樑", "framing" } },
                { "管道", new[] { "管道", "pipes", "pipe", "管路" } },
                { "风管", new[] { "风管", "ducts", "duct", "風管" } },
                { "设备", new[] { "设备", "equipment", "mechanical equipment", "機電設備", "装置" } },
                { "家具", new[] { "家具", "furniture", "傢具" } },
                { "楼梯", new[] { "楼梯", "stairs", "樓梯" } },
                { "屋顶", new[] { "屋顶", "roofs", "屋頂", "roof" } },
                { "天花板", new[] { "天花板", "ceiling", "ceiling", "吊顶" } }
            };

            foreach (var kv in map)
            {
                if (keywordLower.Contains(kv.Key))
                {
                    foreach (var alias in kv.Value)
                    {
                        if (catName.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            return false;
        }

        private static BuiltInCategory? TryParseBuiltInCategory(string s)
        {
            try
            {
                if (Enum.TryParse<BuiltInCategory>("OST_" + s, true, out var bic))
                    return bic;
                // 常见手动映射
                switch (s.Trim())
                {
                    case "门": return BuiltInCategory.OST_Doors;
                    case "窗": return BuiltInCategory.OST_Windows;
                    case "墙": return BuiltInCategory.OST_Walls;
                    case "楼板": return BuiltInCategory.OST_Floors;
                    case "柱": case "结构柱": return BuiltInCategory.OST_StructuralColumns;
                    case "梁": return BuiltInCategory.OST_StructuralFraming;
                    case "管道": return BuiltInCategory.OST_PipeCurves;
                    case "风管": return BuiltInCategory.OST_DuctCurves;
                    case "设备": return BuiltInCategory.OST_MechanicalEquipment;
                    case "家具": return BuiltInCategory.OST_Furniture;
                    case "标高": return BuiltInCategory.OST_Levels;
                    case "网格": case "轴网": return BuiltInCategory.OST_Grids;
                    case "图纸": return BuiltInCategory.OST_Sheets;
                }
            }
            catch { }
            return null;
        }

        private string SetInstanceParameter(Dictionary<string, object> args)
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            var elementIds = args["element_ids"] as object[];
            string paramName = args["parameter_name"] as string;
            string value = args["value"] as string;

            int success = 0, failed = 0;

            using (Transaction tx = new Transaction(doc, "设置参数: " + paramName))
            {
                tx.Start();

                foreach (var idObj in elementIds)
                {
                    int idInt = Convert.ToInt32(idObj);
                    var elem = doc.GetElement(new ElementId(idInt));
                    if (elem == null) { failed++; continue; }

                    Parameter p = elem.LookupParameter(paramName);
                    if (p == null || p.IsReadOnly) { failed++; continue; }

                    bool ok = SetParameterValue(p, value);
                    if (ok) success++; else failed++;
                }

                tx.Commit();
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "success", success },
                { "failed", failed },
                { "message", "成功设置 " + success + " 个，失败 " + failed + " 个" }
            });
        }

        private string BatchSetParameter(Dictionary<string, object> args)
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            string ruleType = args["rule_type"] as string;
            string paramName = args["parameter_name"] as string;

            int success = 0;

            using (Transaction tx = new Transaction(doc, "批量设置参数: " + paramName))
            {
                tx.Start();

                if (ruleType == "by_floor")
                {
                    var devices = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null)
                        .ToList();

                    foreach (var elem in devices)
                    {
                        Parameter levelParam = elem.LookupParameter("参照标高") ?? elem.LookupParameter("Level");
                        if (levelParam == null) continue;

                        ElementId levelId = levelParam.AsElementId();
                        var level = doc.GetElement(levelId) as Level;
                        if (level == null) continue;

                        Parameter targetParam = elem.LookupParameter(paramName);
                        if (targetParam == null || targetParam.IsReadOnly) continue;

                        string floorValue = ExtractFloor(level.Name);
                        SetParameterValue(targetParam, floorValue);
                        success++;
                    }
                }

                tx.Commit();
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "success", success },
                { "message", "按规则 [" + ruleType + "] 成功设置 " + success + " 个图元" }
            });
        }

        private string SaveMemory(Dictionary<string, object> args)
        {
            string key = args.ContainsKey("key") ? args["key"] as string : "";
            string content = args.ContainsKey("content") ? args["content"] as string : "";

            _memory.SaveConversation("[记忆保存] " + key, content, "save_memory");

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "success", true },
                { "message", "已保存记忆: " + key }
            });
        }

        private string SearchMemory(Dictionary<string, object> args)
        {
            string keyword = args.ContainsKey("keyword") ? args["keyword"] as string : "";

            string result = _memory.SearchMemoryAsString(keyword);

            if (string.IsNullOrEmpty(result))
            {
                return DictionaryToJson(new Dictionary<string, object>
                {
                    { "found", false },
                    { "message", "没有找到相关记忆。" }
                });
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "found", true },
                { "message", "找到以下记忆:\n" + result }
            });
        }

        private string ReplaceFamily(Dictionary<string, object> args)
        {
            CheckDocument(out UIDocument uidoc, out Document doc);

            string categoryName = args["target_category"] as string;
            string newTypeName = args["new_type_name"] as string;

            var newType = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .FirstOrDefault(t => t.Name == newTypeName) as ElementType;

            if (newType == null)
                return "{\"error\": \"找不到族类型: " + newTypeName + "\"}";

            int success = 0;

            using (Transaction tx = new Transaction(doc, "替换族类型"))
            {
                tx.Start();

                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category?.Name == categoryName)
                    .ToList();

                foreach (var inst in instances)
                {
                    if (inst is FamilyInstance fi)
                    {
                        try
                        {
                            fi.ChangeTypeId(newType.Id);
                            success++;
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            return DictionaryToJson(new Dictionary<string, object>
            {
                { "success", success },
                { "message", "成功替换 " + success + " 个族实例" }
            });
        }

        // ==================== 辅助方法 ====================

        private bool SetParameterValue(Parameter p, string value)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(value);
                        return true;
                    case StorageType.Double:
                        if (double.TryParse(value, out double d))
                        { p.Set(d); return true; }
                        return false;
                    case StorageType.Integer:
                        if (int.TryParse(value, out int i))
                        { p.Set(i); return true; }
                        return false;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        private string ExtractFloor(string levelName)
        {
            var digits = new string(levelName.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits))
                return digits + "F";
            return levelName;
        }

        private Dictionary<string, object> ParseJsonObject(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            var pairs = json.Split(',');
            foreach (var pair in pairs)
            {
                var kv = pair.Split(new[] { ':' }, 2);
                if (kv.Length == 2)
                {
                    string key = kv[0].Trim().Trim('"');
                    string val = kv[1].Trim().Trim('"');
                    result[key] = val;
                }
            }

            return result;
        }

        private string DictionaryToJson(Dictionary<string, object> dict)
        {
            var parts = new List<string>();
            foreach (var kv in dict)
            {
                string val;
                if (kv.Value is string)
                    val = "\"" + kv.Value + "\"";
                else
                    val = kv.Value?.ToString() ?? "null";
                parts.Add("\"" + kv.Key + "\": " + val);
            }
            return "{" + string.Join(", ", parts) + "}";
        }
    }
}
