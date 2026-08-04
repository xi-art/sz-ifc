using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.AIAssistant;

namespace MyRevitAddin.InPlaceFamilyConverter
{
    public class Converter
    {
        private readonly Document _document;
        private readonly FamilyInstance _inPlaceInstance;
        private readonly Family _inPlaceFamily;
        private readonly List<string> _templateSearchLog = new List<string>();

        public string CustomTemplatePath { get; set; }

        public string LastError { get; private set; }

        public IReadOnlyList<string> TemplateSearchLog => _templateSearchLog;

        public Converter(Document document, FamilyInstance inPlaceInstance)
        {
            _document = document;
            _inPlaceInstance = inPlaceInstance;
            _inPlaceFamily = inPlaceInstance.Symbol?.Family;
        }

        private void LogTemplate(string msg)
        {
            _templateSearchLog.Add("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg);
        }

        public Family ConvertToLoadableFamily(string targetPath)
        {
            LastError = null;
            var ctx = new Dictionary<string, object>
            {
                { "inplace_family", _inPlaceFamily?.Name ?? "" },
                { "inplace_symbol", _inPlaceInstance.Symbol?.Name ?? "" },
                { "inplace_id", _inPlaceInstance.Id.IntegerValue },
                { "target_path", targetPath ?? "" },
                { "custom_template", CustomTemplatePath ?? "" }
            };
            HistoryLogger.Operation("Converter:Convert-START", ctx, "开始转换为可载入族");

            try
            {
                if (_inPlaceFamily == null)
                    throw new InvalidOperationException("内建族对象为空");

                BuiltInCategory familyCategory = GetFamilyCategory();
                ctx["category"] = familyCategory.ToString();
                LogTemplate("解析类别: " + familyCategory);

                string templatePath = null;

                if (!string.IsNullOrEmpty(CustomTemplatePath) && File.Exists(CustomTemplatePath))
                {
                    templatePath = CustomTemplatePath;
                    LogTemplate("✅ 优先使用用户设置的模板: " + templatePath);
                }
                else
                {
                    if (!string.IsNullOrEmpty(CustomTemplatePath))
                        LogTemplate("⚠️ 用户设置的 CustomTemplatePath 不存在，回退自动查找: " + CustomTemplatePath);

                    templatePath = GetFamilyTemplatePath(familyCategory);
                    if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                    {
                        LogTemplate("⚠️ 类别专用模板未命中，回退到 Generic Model.rft");
                        templatePath = GetGenericModelTemplatePath();
                    }
                }

                ctx["resolved_template"] = templatePath ?? "(空)";
                ctx["template_search_log"] = string.Join("\n", _templateSearchLog);

                if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                {
                    LastError = "模板找不到。搜索路径记录:\n  - " + string.Join("\n  - ", _templateSearchLog);
                    HistoryLogger.Operation("Converter:TEMPLATE-MISSING", ctx, LastError);
                    throw new FileNotFoundException("找不到族模板文件。请在设置窗口中点击\"浏览模板...\"手动选择任意 .rft 族模板文件（推荐 Generic Model.rft）。\n\n已尝试的搜索路径:\n" + string.Join("\n", _templateSearchLog));
                }

                LogTemplate("✅ 最终使用模板: " + templatePath);
                HistoryLogger.Operation("Converter:TEMPLATE-RESOLVED", ctx, "模板解析成功 -> " + templatePath);

                Document familyDoc = _document.Application.NewFamilyDocument(templatePath);
                if (familyDoc == null)
                {
                    LastError = "无法用模板创建族文档: " + templatePath;
                    throw new InvalidOperationException("无法创建族文档");
                }

                HistoryLogger.Operation("Converter:FAM-DOC-CREATED", ctx, "族文档创建完成 hash=" + familyDoc.GetHashCode());

                try
                {
                    ExtractAndRecreateInFamilyDocument(familyDoc);
                    HistoryLogger.Operation("Converter:GEOM-EXTRACTED", ctx, "几何/参数提取已完成");

                    SaveAsOptions saveOptions = new SaveAsOptions();
                    saveOptions.OverwriteExistingFile = true;
                    familyDoc.SaveAs(targetPath, saveOptions);
                    HistoryLogger.Operation("Converter:SAVED-RFA", ctx, "族文件保存完成，文件大小=" + new FileInfo(targetPath).Length + " 字节");

                    Family loadedFamily = null;
                    using (Transaction trans = new Transaction(_document, "加载转换后的族"))
                    {
                        trans.Start();
                        bool loadSuccess = _document.LoadFamily(targetPath, out loadedFamily);
                        trans.Commit();
                        HistoryLogger.Operation("Converter:LOAD-BACK", ctx,
                            "族重新加载到项目: " + (loadSuccess ? "成功" : "失败") + "  id=" + (loadedFamily?.Id.IntegerValue.ToString() ?? "null"));
                    }

                    if (loadedFamily == null)
                    {
                        LastError = "族文件保存成功，但加载回项目失败（LoadFamily 返回 false）";
                        HistoryLogger.Operation("Converter:LOAD-NULL", ctx, LastError);
                    }
                    else
                    {
                        HistoryLogger.Operation("Converter:CONVERT-SUCCESS", ctx,
                            "✅ 全部完成！新族ID=" + loadedFamily.Id.IntegerValue + " / Name=" + loadedFamily.Name);
                    }

                    return loadedFamily;
                }
                finally
                {
                    familyDoc.Close(false);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                HistoryLogger.Error("Converter.ConvertToLoadableFamily", ex,
                    "上下文: " + string.Join(" | ", ctx) + "\n模板搜索路径:\n  " + string.Join("\n  ", _templateSearchLog));
                throw;
            }
        }

        private BuiltInCategory GetFamilyCategory()
        {
            if (_inPlaceInstance.Symbol != null)
            {
                Category category = _inPlaceInstance.Symbol.Category;
                if (category != null)
                {
                    int id = category.Id.IntegerValue;
                    if (Enum.IsDefined(typeof(BuiltInCategory), id))
                    {
                        return (BuiltInCategory)id;
                    }
                }
            }

            if (_inPlaceFamily != null)
            {
                Category category = _inPlaceFamily.FamilyCategory;
                if (category != null)
                {
                    int id = category.Id.IntegerValue;
                    if (Enum.IsDefined(typeof(BuiltInCategory), id))
                    {
                        return (BuiltInCategory)id;
                    }
                }
            }

            return BuiltInCategory.OST_GenericModel;
        }

        private string GetFamilyTemplatePath(BuiltInCategory category)
        {
            string templatesDir = GetTemplatesDirectory();
            if (string.IsNullOrEmpty(templatesDir))
            {
                LogTemplate("  ⚠️ GetFamilyTemplatePath: 父目录 GetTemplatesDirectory 返回 null，跳过类别=" + category);
                return null;
            }

            string templateName = GetTemplateNameForCategory(category);
            LogTemplate("  • 按类别 " + category + " 对应模板名: " + templateName + " (在目录 " + templatesDir + " 中查找)");
            if (!string.IsNullOrEmpty(templateName))
            {
                string templatePath = Path.Combine(templatesDir, templateName);
                if (File.Exists(templatePath))
                {
                    LogTemplate("  ✅ 类别匹配命中: " + templatePath);
                    return templatePath;
                }
                LogTemplate("     × 类别模板文件不存在: " + templatePath);
            }

            return null;
        }

        private string GetGenericModelTemplatePath()
        {
            string templatesDir = GetTemplatesDirectory();
            if (string.IsNullOrEmpty(templatesDir))
            {
                LogTemplate("  ⚠️ GetGenericModelTemplatePath: 父目录 GetTemplatesDirectory 返回 null");
                return null;
            }

            string[] genericTemplates = new[]
            {
                "Generic Model.rft",
                "Metric Generic Model.rft",
                "English Generic Model.rft",
                "M_Generic Model.rft"
            };

            LogTemplate("  • 依次在 " + templatesDir + " 下查找通⽤模型模板: " + string.Join(", ", genericTemplates));
            foreach (string template in genericTemplates)
            {
                string path = Path.Combine(templatesDir, template);
                if (File.Exists(path))
                {
                    LogTemplate("  ✅ 通用模型命中: " + path);
                    return path;
                }
                LogTemplate("     × 不命中: " + path);
            }

            if (Directory.Exists(templatesDir))
            {
                try
                {
                    string[] files = Directory.GetFiles(templatesDir, "*Generic*.rft");
                    if (files.Length > 0)
                    {
                        LogTemplate("  ✅ 通配符搜索命中 *Generic*.rft: " + files[0] + " (共 " + files.Length + " 个)");
                        return files[0];
                    }
                }
                catch (Exception ex) { LogTemplate("  × 异常搜索 *Generic*.rft: " + ex.Message); }
            }

            LogTemplate("  × 兜底也没找到通用模型模板");
            return null;
        }

        private string GetTemplatesDirectory()
        {
            try
            {
                LogTemplate("▶ 开始 GetTemplatesDirectory 搜索...");
                string revitPath = _document.Application.SharedParametersFilename;
                if (!string.IsNullOrEmpty(revitPath))
                {
                    LogTemplate("  • SharedParametersFilename = " + revitPath + " -> 向上推导 Revit 根目录");
                    string revitRoot = Path.GetDirectoryName(revitPath);
                    while (!string.IsNullOrEmpty(revitRoot))
                    {
                        string templatesPath = Path.Combine(revitRoot, "Family Templates");
                        if (Directory.Exists(templatesPath))
                        {
                            LogTemplate("  ✅ 命中 (SharedParametersFilename 推导): " + templatesPath);
                            return templatesPath;
                        }
                        LogTemplate("     × 跳过 (不存在): " + templatesPath);

                        templatesPath = Path.Combine(revitRoot, "Templates", "Family Templates");
                        if (Directory.Exists(templatesPath))
                        {
                            LogTemplate("  ✅ 命中 (SharedParametersFilename 推导2): " + templatesPath);
                            return templatesPath;
                        }
                        LogTemplate("     × 跳过 (不存在): " + templatesPath);

                        try { revitRoot = Directory.GetParent(revitRoot)?.FullName; }
                        catch { break; }
                    }
                }
                else
                {
                    LogTemplate("  • SharedParametersFilename 为空，跳过这条推导");
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                LogTemplate("  • 系统目录: ProgramFiles=" + programFiles + " / X86=" + programFilesX86 + " / ProgramData=" + programData + " / UserProfile=" + userProfile);

                List<string> possiblePaths = new List<string>();

                foreach (string drive in new[] { "F", "E", "D", "C" })
                {
                    foreach (string revitVer in new[] { "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019", "2018", "2017", "2016" })
                    {
                        possiblePaths.Add($@"{drive}:\reivt\{revitVer}\Revit {revitVer}\Family Templates\Chinese");
                        possiblePaths.Add($@"{drive}:\reivt\{revitVer}\Revit {revitVer}\Family Templates");
                        possiblePaths.Add($@"{drive}:\reivt\{revitVer}\Family Templates\Chinese");
                        possiblePaths.Add($@"{drive}:\reivt\{revitVer}\Family Templates");
                    }
                }

                string[] standardRoots = new[] { programFiles, programFilesX86, programData, userProfile };
                foreach (string root in standardRoots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    possiblePaths.Add(Path.Combine(root, "Autodesk", "Revit 2020", "Family Templates", "Chinese"));
                    possiblePaths.Add(Path.Combine(root, "Autodesk", "Revit 2020", "Family Templates"));
                    possiblePaths.Add(Path.Combine(root, "Autodesk", "Revit 2018", "Family Templates", "Chinese"));
                    possiblePaths.Add(Path.Combine(root, "Autodesk", "Revit 2018", "Family Templates"));
                    possiblePaths.Add(Path.Combine(root, "Autodesk", "FormIt Converter For Revit 2018", "FormItConversionAddon", "Resources", "2016", "Templates", "Metric"));
                    possiblePaths.Add(Path.Combine(root, "Autodesk", "FormIt Converter For Revit 2020", "FormItConversionAddon", "Resources", "Templates", "Metric"));
                }

                LogTemplate("  • 枚举候选目录共 " + possiblePaths.Count + " 条，逐条验证存在性:");
                foreach (string path in possiblePaths)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        {
                            LogTemplate("  ✅ 命中: " + path);
                            return path;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTemplate("     × 异常访问: " + path + " -> " + ex.Message);
                    }
                }

                LogTemplate("  • 候选枚举失败，开始全盘搜索 Generic Model.rft（可能耗时）");
                try
                {
                    string[] allDrives = Directory.GetLogicalDrives();
                    LogTemplate("     可用盘符: " + string.Join(", ", allDrives ?? new string[0]));
                    foreach (string drv in allDrives ?? new string[0])
                    {
                        try
                        {
                            string[] found = Directory.GetFiles(drv, "Generic Model.rft", SearchOption.AllDirectories);
                            if (found != null && found.Length > 0)
                            {
                                LogTemplate("  ✅ 全盘命中第 1 个: " + found[0] + "  (共 " + found.Length + " 个)");
                                return Path.GetDirectoryName(found[0]);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTemplate("     × 盘 " + drv + " 搜索失败: " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTemplate("     × 全盘搜索顶层异常: " + ex.Message);
                }
            }
            catch (Exception outerEx)
            {
                LogTemplate("  !!! GetTemplatesDirectory 顶层异常: " + outerEx.GetType().Name + ": " + outerEx.Message);
            }

            LogTemplate("  ❌ GetTemplatesDirectory 最终返回 null（未找到模板目录）");
            return null;
        }

        private string GetTemplateNameForCategory(BuiltInCategory category)
        {
            switch (category)
            {
                case BuiltInCategory.OST_Doors:
                    return "Door.rft";
                case BuiltInCategory.OST_Windows:
                    return "Window.rft";
                case BuiltInCategory.OST_Walls:
                    return "Wall.rft";
                case BuiltInCategory.OST_Floors:
                    return "Floor.rft";
                case BuiltInCategory.OST_Roofs:
                    return "Roof.rft";
                case BuiltInCategory.OST_Ceilings:
                    return "Ceiling.rft";
                case BuiltInCategory.OST_Stairs:
                    return "Stair.rft";
                case BuiltInCategory.OST_Railings:
                    return "Railing.rft";
                case BuiltInCategory.OST_Columns:
                    return "Column.rft";
                case BuiltInCategory.OST_StructuralColumns:
                    return "Structural Column.rft";
                case BuiltInCategory.OST_StructuralFraming:
                    return "Structural Framing.rft";
                case BuiltInCategory.OST_Furniture:
                    return "Furniture.rft";
                case BuiltInCategory.OST_FurnitureSystems:
                    return "Furniture System.rft";
                case BuiltInCategory.OST_PlumbingFixtures:
                    return "Plumbing Fixture.rft";
                case BuiltInCategory.OST_LightingFixtures:
                    return "Lighting Fixture.rft";
                case BuiltInCategory.OST_ElectricalFixtures:
                    return "Electrical Fixture.rft";
                case BuiltInCategory.OST_MechanicalEquipment:
                    return "Mechanical Equipment.rft";
                case BuiltInCategory.OST_ElectricalEquipment:
                    return "Electrical Equipment.rft";
                case BuiltInCategory.OST_Casework:
                    return "Casework.rft";
                case BuiltInCategory.OST_CurtainWallPanels:
                    return "Curtain Wall Panel.rft";
                case BuiltInCategory.OST_CurtainWallMullions:
                    return "Mullion.rft";
                case BuiltInCategory.OST_GenericModel:
                default:
                    return "Generic Model.rft";
            }
        }

        private void ExtractAndRecreateInFamilyDocument(Document familyDoc)
        {
            string tempSatPath = Path.Combine(Path.GetTempPath(), $"InPlaceExport_{Guid.NewGuid()}.sat");

            try
            {
                if (ExportGeometryToSat(tempSatPath))
                {
                    ImportSatToFamilyDocument(familyDoc, tempSatPath);
                }
                else
                {
                    CopyElementsDirectly(familyDoc);
                }

                CopyFamilyParameters(familyDoc);
            }
            finally
            {
                if (File.Exists(tempSatPath))
                {
                    try { File.Delete(tempSatPath); } catch { }
                }
            }
        }

        private bool ExportGeometryToSat(string satPath)
        {
            try
            {
                Options geomOptions = new Options();
                geomOptions.DetailLevel = ViewDetailLevel.Fine;
                GeometryElement geomElement = _inPlaceInstance.get_Geometry(geomOptions);

                if (geomElement == null)
                    return false;

                View3D exportView = null;
                using (Transaction trans = new Transaction(_document, "创建导出视图"))
                {
                    trans.Start();

                    FilteredElementCollector viewCollector = new FilteredElementCollector(_document);
                    viewCollector.OfClass(typeof(View3D));
                    foreach (View3D view in viewCollector.Cast<View3D>())
                    {
                        if (!view.IsTemplate && view.Name == "{3D}")
                        {
                            exportView = view;
                            break;
                        }
                    }

                    if (exportView == null)
                    {
                        ViewFamilyType viewFamilyType = null;
                        FilteredElementCollector vftCollector = new FilteredElementCollector(_document);
                        vftCollector.OfClass(typeof(ViewFamilyType));
                        foreach (ViewFamilyType v in vftCollector.Cast<ViewFamilyType>())
                        {
                            if (v.ViewFamily == ViewFamily.ThreeDimensional)
                            {
                                viewFamilyType = v;
                                break;
                            }
                        }

                        if (viewFamilyType != null)
                        {
                            exportView = View3D.CreateIsometric(_document, viewFamilyType.Id);
                            exportView.Name = "TempExportView";
                        }
                    }

                    trans.Commit();
                }

                if (exportView == null)
                    return false;

                SATExportOptions satOptions = new SATExportOptions();

                ICollection<ElementId> elementIds = new List<ElementId> { _inPlaceInstance.Id };

                bool exported = _document.Export(
                    Path.GetDirectoryName(satPath),
                    Path.GetFileNameWithoutExtension(satPath),
                    elementIds,
                    satOptions);

                return exported && File.Exists(satPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SAT导出失败: " + ex.Message);
                return false;
            }
        }

        private void ImportSatToFamilyDocument(Document familyDoc, string satPath)
        {
            using (Transaction trans = new Transaction(familyDoc, "导入SAT几何"))
            {
                trans.Start();

                try
                {
                    View defaultView = null;
                    FilteredElementCollector viewCollector = new FilteredElementCollector(familyDoc);
                    viewCollector.OfClass(typeof(View));
                    foreach (View v in viewCollector.Cast<View>())
                    {
                        if (v is View3D || v is ViewPlan)
                        {
                            defaultView = v;
                            break;
                        }
                    }

                    if (defaultView == null)
                    {
                        defaultView = familyDoc.ActiveView;
                    }

                    SATImportOptions importOptions = new SATImportOptions();
                    importOptions.Placement = ImportPlacement.Origin;
                    importOptions.Unit = ImportUnit.Default;

                    ElementId importedId = familyDoc.Import(
                        satPath,
                        importOptions,
                        defaultView);

                    if (importedId == ElementId.InvalidElementId)
                    {
                        throw new InvalidOperationException("SAT文件导入失败");
                    }

                    trans.Commit();
                }
                catch
                {
                    trans.RollBack();
                    throw;
                }
            }
        }

        private void CopyElementsDirectly(Document familyDoc)
        {
            using (Transaction trans = new Transaction(familyDoc, "复制元素"))
            {
                trans.Start();
                try
                {
                    Options options = new Options();
                    options.DetailLevel = ViewDetailLevel.Fine;
                    GeometryElement geomElement = _inPlaceInstance.get_Geometry(options);
                    if (geomElement != null)
                    {
                        foreach (GeometryObject geomObj in geomElement)
                        {
                            ProcessGeometryObject(familyDoc, geomObj);
                        }
                    }
                    trans.Commit();
                }
                catch
                {
                    trans.RollBack();
                    throw;
                }
            }
        }

        private void ProcessGeometryObject(Document familyDoc, GeometryObject geomObj)
        {
            if (geomObj is GeometryInstance geomInstance)
            {
                foreach (GeometryObject nestedObj in geomInstance.SymbolGeometry)
                {
                    ProcessGeometryObject(familyDoc, nestedObj);
                }
            }
        }

        private void CopyFamilyParameters(Document familyDoc)
        {
            if (_inPlaceFamily == null) return;

            FamilyManager sourceManager = _inPlaceFamily.Document.FamilyManager;
            FamilyManager targetManager = familyDoc.FamilyManager;

            if (sourceManager == null || targetManager == null) return;
            if (_inPlaceInstance.Symbol == null) return;

            using (Transaction trans = new Transaction(familyDoc, "复制族参数"))
            {
                trans.Start();

                try
                {
                    foreach (FamilyParameter param in sourceManager.Parameters)
                    {
                        try
                        {
                            if (param == null || param.Definition == null) continue;
                            string pName = param.Definition.Name;
                            FamilyParameter existingParam = targetManager.get_Parameter(pName);
                            if (existingParam != null) continue;

                            FamilyParameter newParam = targetManager.AddParameter(
                                pName,
                                param.Definition.ParameterGroup,
                                param.Definition.ParameterType,
                                param.IsInstance);

                            if (!param.IsDeterminedByFormula && newParam != null)
                            {
                                Parameter srcParam = _inPlaceInstance.Symbol.get_Parameter(param.Definition);
                                if (srcParam == null) srcParam = _inPlaceInstance.get_Parameter(param.Definition);
                                if (srcParam != null && srcParam.HasValue)
                                {
                                    try
                                    {
                                        switch (srcParam.StorageType)
                                        {
                                            case StorageType.Double:
                                                targetManager.Set(newParam, srcParam.AsDouble());
                                                break;
                                            case StorageType.Integer:
                                                targetManager.Set(newParam, srcParam.AsInteger());
                                                break;
                                            case StorageType.String:
                                                targetManager.Set(newParam, srcParam.AsString());
                                                break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }

                    trans.Commit();
                }
                catch
                {
                    trans.RollBack();
                }
            }
        }
    }
}
