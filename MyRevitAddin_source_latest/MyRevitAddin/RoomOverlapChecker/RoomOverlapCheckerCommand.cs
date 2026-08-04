using System;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.RoomOverlapChecker
{
    [Transaction(TransactionMode.Manual)]
    public class RoomOverlapCheckerCommand : IExternalCommand
    {
        private static RoomOverlapCheckerDialog _currentDialog;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var uidoc = uiApp.ActiveUIDocument;
                var doc = uidoc.Document;

                if (doc == null)
                {
                    TaskDialog.Show("提示", "请先打开一个项目文档。");
                    return Result.Cancelled;
                }

                if (_currentDialog != null && !_currentDialog.IsDisposed)
                {
                    _currentDialog.BringToFront();
                    return Result.Succeeded;
                }

                _currentDialog = new RoomOverlapCheckerDialog(doc, uidoc);
                _currentDialog.FormClosing += (s, e) => { _currentDialog = null; };
                _currentDialog.Show();

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
