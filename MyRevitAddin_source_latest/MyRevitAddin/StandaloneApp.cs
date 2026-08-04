using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace MyRevitAddin
{
    /// <summary>
    /// 独立部署版 App —— 注册全部插件按钮
    /// 作者：兮
    /// </summary>
    public class StandaloneApp : IExternalApplication
    {
        private const string TabName = "兮的工具";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);

                // 面板1: 批量工具
                var panelBatch = application.CreateRibbonPanel(TabName, "批量工具");
                AddButton(panelBatch, "BatchParamCopy_SA", "批量改\n参数",
                    "筛选类别→选源参数→选目标参数→预览→一键复制（支持类型/实例参数互拷）\n作者：兮",
                    "MyRevitAddin.BatchParamCopy.BatchParamCopyCommand");
                AddButton(panelBatch, "ExcelImportExport_SA", "表格\n导入回导",
                    "导出构件属性到CSV→Excel填写→回导入Revit更新\n作者：兮",
                    "MyRevitAddin.ExcelImportExport.ExcelImportExportCommand");

                // 面板2: 构件计算
                var panelCalc = application.CreateRibbonPanel(TabName, "构件计算");
                AddButton(panelCalc, "DoorWindowOpeningArea_SA", "门窗\n开启面积",
                    "自动读取门窗长度和高度，计算开启面积并写入指定实例参数\n作者：兮",
                    "MyRevitAddin.DoorWindowOpeningArea.DoorWindowOpeningAreaCommand");

                // 面板3: 机电工具
                var panelMEP = application.CreateRibbonPanel(TabName, "机电工具");
                AddButton(panelMEP, "SelectBySystem_SA", "按系统\n选中构件",
                    "按机电系统类型筛选并选中构件，支持批量附加属性\n作者：兮",
                    "MyRevitAddin.SelectBySystem.SelectBySystemCommand");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("插件加载失败", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private void AddButton(RibbonPanel panel, string name, string text, string tooltip, string className)
        {
            try
            {
                var btnData = new PushButtonData(name, text,
                    Assembly.GetExecutingAssembly().Location, className);
                PushButton btn = panel.AddItem(btnData) as PushButton;
                btn.ToolTip = tooltip;
            }
            catch { }
        }
    }
}
