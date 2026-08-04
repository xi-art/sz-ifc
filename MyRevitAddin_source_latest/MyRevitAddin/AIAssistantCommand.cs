using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MyRevitAddin.AIAssistant;
using System;

namespace MyRevitAddin
{
    /// <summary>
    /// AI 助手命令：点击按钮显示 Dockable Pane
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AIAssistantCommand : IExternalCommand
    {
        private const string QWEN_API_KEY = "sk-ws-H.RYYYDEE.QA70.MEYCIQCTzinNAGKVWlI4HditIDB-5GZ0iTdXZA4VcTa5itzHXgIhALU4zd3wM9iP1PKdH4vX0EitrRXcpdk_1hyNaxmUj0k8";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null)
                {
                    TaskDialog.Show("AI 助手", "请先打开一个 Revit 文档");
                    return Result.Failed;
                }

                // 更新状态：缓存 UIApplication（安全），不要缓存 UIDocument
                AIAssistantState.CurrentUIApp = uiapp;
                AIAssistantState.ApiKey = QWEN_API_KEY;

                // 如果面板已存在，重新初始化一次（因为启动时 SetupDockablePane 先初始化，
                // 那时 AIAssistantState.ApiKey 和 CurrentUIApp 都是 null，现在补全）
                if (AIAssistantPaneHolder.Pane != null)
                {
                    AIAssistantPaneHolder.Pane.SetUIApplication(uiapp);
                    AIAssistantPaneHolder.Pane.Initialize(uidoc, QWEN_API_KEY);
                }

                // 显示 Dockable Pane
                DockablePane pane = uiapp.GetDockablePane(App.AI_PANE_ID);
                if (pane != null)
                {
                    pane.Show();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("AI 助手错误", ex.Message);
                return Result.Failed;
            }
        }
    }
}
