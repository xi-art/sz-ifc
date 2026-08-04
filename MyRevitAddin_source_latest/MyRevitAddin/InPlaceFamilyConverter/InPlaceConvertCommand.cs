using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using MyRevitAddin.AIAssistant;

namespace MyRevitAddin.InPlaceFamilyConverter
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InPlaceConvertCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            var opArgs = new Dictionary<string, object>
            {
                { "doc_title", doc.Title ?? "" },
                { "doc_path", doc.PathName ?? "" },
                { "doc_id", doc.GetHashCode() }
            };
            HistoryLogger.Operation("InPlaceConvertCommand:START", opArgs, "开始执行内建族转换命令");

            try
            {
                Reference pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new InPlaceFamilySelectionFilter(),
                    "请选择一个内建族实例");

                Element elem = doc.GetElement(pickedRef);
                if (elem == null)
                {
                    message = "未能获取选中的元素。";
                    HistoryLogger.Operation("InPlaceConvertCommand:FAIL-NO-ELEM", opArgs, message);
                    return Result.Failed;
                }

                FamilyInstance inPlaceInstance = elem as FamilyInstance;
                if (inPlaceInstance == null || !IsInPlaceFamily(inPlaceInstance))
                {
                    TaskDialog.Show("错误", "选中的元素不是内建族实例。");
                    HistoryLogger.Operation("InPlaceConvertCommand:CANCELLED-NOT-INPLACE", opArgs,
                        "选中元素 id=" + elem.Id.IntegerValue + " / name=" + elem.Name + " / category=" + elem.Category?.Name);
                    return Result.Cancelled;
                }

                opArgs["inplace_id"] = inPlaceInstance.Id.IntegerValue;
                opArgs["inplace_name"] = inPlaceInstance.Name ?? "";
                opArgs["inplace_symbol"] = inPlaceInstance.Symbol?.Name ?? "";
                opArgs["inplace_family"] = inPlaceInstance.Symbol?.Family?.Name ?? "";
                opArgs["inplace_category"] = inPlaceInstance.Category?.Name ?? "";

                using (InPlaceSettingsForm settingsForm = new InPlaceSettingsForm(doc, inPlaceInstance))
                {
                    if (settingsForm.ShowDialog() != DialogResult.OK)
                    {
                        HistoryLogger.Operation("InPlaceConvertCommand:CANCELLED-FORM", opArgs, "用户取消了设置窗体");
                        return Result.Cancelled;
                    }

                    string targetPath = settingsForm.TargetFilePath;
                    bool deleteOriginal = settingsForm.DeleteOriginal;
                    bool replaceInProject = settingsForm.ReplaceInProject;
                    string customTpl = settingsForm.CustomTemplatePath ?? "";

                    opArgs["target_path"] = targetPath;
                    opArgs["delete_original"] = deleteOriginal;
                    opArgs["replace_in_project"] = replaceInProject;
                    opArgs["custom_template"] = customTpl;

                    HistoryLogger.Operation("InPlaceConvertCommand:BEFORE-CONVERT", opArgs,
                        "准备调用 Converter.ConvertToLoadableFamily，目标=" + targetPath + "，自定义模板=" + (string.IsNullOrEmpty(customTpl) ? "(未设，使用自动查找)" : customTpl));

                    Converter converter = new Converter(doc, inPlaceInstance);
                    if (!string.IsNullOrEmpty(customTpl))
                    {
                        converter.CustomTemplatePath = customTpl;
                    }
                    Family newFamily = converter.ConvertToLoadableFamily(targetPath);

                    if (newFamily == null)
                    {
                        message = "转换失败。";
                        HistoryLogger.Operation("InPlaceConvertCommand:FAIL-CONVERT", opArgs,
                            "Converter.ConvertToLoadableFamily 返回 null。转换器最后错误: " + converter.LastError);
                        return Result.Failed;
                    }

                    opArgs["new_family_id"] = newFamily.Id.IntegerValue;
                    opArgs["new_family_name"] = newFamily.Name ?? "";

                    if (replaceInProject)
                    {
                        HistoryLogger.Operation("InPlaceConvertCommand:PLACE-INSTANCE", opArgs, "替换模式：在项目中放置新族实例");
                        PlaceFamilyInstance(doc, inPlaceInstance, newFamily, targetPath, deleteOriginal);
                    }

                    HistoryLogger.Operation("InPlaceConvertCommand:SUCCESS", opArgs,
                        "转换完成！新族文件: " + targetPath + " / 族名: " + newFamily.Name + " / 替换: " + replaceInProject + " / 删除原内建: " + deleteOriginal);
                    TaskDialog.Show("完成", $"内建族已成功转换为可载入族。\n保存路径: {targetPath}");
                }

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                HistoryLogger.Operation("InPlaceConvertCommand:CANCELLED-OPC", opArgs, "用户取消了点选操作");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"转换过程中发生错误: {ex.Message}";
                HistoryLogger.Error("InPlaceConvertCommand.Execute", ex,
                    "参数: " + string.Join(" | ", opArgs));
                return Result.Failed;
            }
        }

        private bool IsInPlaceFamily(FamilyInstance instance)
        {
            if (instance == null) return false;
            Family family = instance.Symbol?.Family;
            if (family == null) return false;
            return family.IsInPlace;
        }

        private void PlaceFamilyInstance(
            Document doc,
            FamilyInstance originalInstance,
            Family newFamily,
            string familyPath,
            bool deleteOriginal)
        {
            using (Transaction trans = new Transaction(doc, "放置转换后的族实例"))
            {
                trans.Start();

                try
                {
                    FamilySymbol symbol = GetFirstFamilySymbol(doc, newFamily);

                    if (symbol == null)
                    {
                        doc.LoadFamily(familyPath, out Family loadedFamily);
                        symbol = GetFirstFamilySymbol(doc, loadedFamily);
                    }

                    if (symbol != null)
                    {
                        if (!symbol.IsActive)
                        {
                            symbol.Activate();
                        }

                        LocationPoint locPoint = originalInstance.Location as LocationPoint;
                        LocationCurve locCurve = originalInstance.Location as LocationCurve;
                        Level instLevel = doc.GetElement(originalInstance.LevelId) as Level;

                        FamilyInstance newInstance = null;

                        if (locPoint != null)
                        {
                            XYZ point = locPoint.Point;
                            newInstance = doc.Create.NewFamilyInstance(
                                point,
                                symbol,
                                originalInstance.Host,
                                instLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (newInstance != null && originalInstance.CanFlipFacing)
                            {
                                if (originalInstance.FacingFlipped)
                                    newInstance.flipFacing();
                            }
                            if (newInstance != null && originalInstance.CanFlipHand)
                            {
                                if (originalInstance.HandFlipped)
                                    newInstance.flipHand();
                            }
                        }
                        else if (locCurve != null)
                        {
                            Curve curve = locCurve.Curve;
                            newInstance = doc.Create.NewFamilyInstance(
                                curve,
                                symbol,
                                instLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        }
                        else
                        {
                            try
                            {
                                newInstance = doc.Create.NewFamilyInstance(
                                    originalInstance.HostFace,
                                    XYZ.Zero,
                                    XYZ.BasisX,
                                    symbol);
                            }
                            catch { }
                        }

                        if (newInstance != null)
                        {
                            CopyParameters(originalInstance, newInstance);
                        }

                        if (deleteOriginal && newInstance != null)
                        {
                            doc.Delete(originalInstance.Id);
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

        private FamilySymbol GetFirstFamilySymbol(Document doc, Family family)
        {
            if (family == null) return null;

            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(FamilySymbol));

            foreach (FamilySymbol sym in collector.Cast<FamilySymbol>())
            {
                if (sym.Family != null && sym.Family.Id == family.Id)
                {
                    return sym;
                }
            }

            return null;
        }

        private void CopyParameters(FamilyInstance source, FamilyInstance target)
        {
            foreach (Parameter param in source.Parameters)
            {
                if (param.IsReadOnly) continue;
                if (param.Definition == null) continue;

                Parameter targetParam = target.get_Parameter(param.Definition);
                if (targetParam == null || targetParam.IsReadOnly) continue;

                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.Double:
                            targetParam.Set(param.AsDouble());
                            break;
                        case StorageType.Integer:
                            targetParam.Set(param.AsInteger());
                            break;
                        case StorageType.String:
                            targetParam.Set(param.AsString());
                            break;
                        case StorageType.ElementId:
                            targetParam.Set(param.AsElementId());
                            break;
                    }
                }
                catch { }
            }
        }
    }

    public class InPlaceFamilySelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is FamilyInstance instance)
            {
                Family family = instance.Symbol?.Family;
                if (family != null && family.IsInPlace)
                {
                    return true;
                }
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
