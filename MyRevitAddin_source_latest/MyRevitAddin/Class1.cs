using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace MyRevitAddin
{
    [Transaction(TransactionMode.Manual)]
    public class MyRevitAddin : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                var selection = uidoc.Selection.GetElementIds();

                TaskDialog.Show("MyRevitAddin",
                    $"Hello Revit!\n当前文档: {doc.Title}\n选中元素数: {selection.Count}");

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
