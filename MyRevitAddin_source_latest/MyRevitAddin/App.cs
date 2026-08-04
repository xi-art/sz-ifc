using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using MyRevitAddin.AIAssistant;

namespace MyRevitAddin
{
    public class App : IExternalApplication
    {
        private const string TabName = "我的工具";

        // AI 助手 Dockable Pane ID（唯一 GUID）
        public static readonly DockablePaneId AI_PANE_ID = new DockablePaneId(
            new Guid("8B3F2A1C-5D4E-4F6A-9B7C-2E1D3A4B5C6D"));

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 保存引用（供 AI 面板初始化时获取 UIDocument）
                RevitCommandDataHolder.UIControlledApplication = application;

                // 初始化 AI 助手状态（从 AIAssistantCommand 提取 API Key，避免重复）
                InitializeAIState();

                // 创建自定义选项卡
                application.CreateRibbonTab(TabName);

                // 面板1: 标记工具
                RibbonPanel panelTags = application.CreateRibbonPanel(TabName, "标记工具");

                AddButton(panelTags,
                    "TagObstacleAvoidance",
                    "关键标记\n避障",
                    "批量选中关键标记，自动检测重叠并平移避让",
                    "MyRevitAddin.TagObstacleAvoidance");

                // 面板2: 图纸工具
                RibbonPanel panelSheets = application.CreateRibbonPanel(TabName, "图纸工具");

                AddButton(panelSheets,
                    "DuplicateSheets",
                    "批量复制\n图纸",
                    "批量复制图纸与视图，支持视图替换与自定义命名",
                    "MyRevitAddin.DuplicateSheetsCommand");

                // 面板2b: 视图工具
                RibbonPanel panelViews = application.CreateRibbonPanel(TabName, "视图工具");

                AddButton(panelViews,
                    "DuplicateViews",
                    "批量复制\n视图样板",
                    "批量复制视图并设置视图样板，命名格式：{原名}（{样板名}）",
                    "MyRevitAddin.DuplicateViewsCommand");

                AddButton(panelViews,
                    "AddFiltersToTemplates",
                    "添加过滤器\n到样板",
                    "批量添加过滤器到视图样板并关闭可见性",
                    "MyRevitAddin.AddFiltersToTemplatesCommand");

                // 面板3: AI 助手
                RibbonPanel panelAI = application.CreateRibbonPanel(TabName, "AI 助手");

                AddButton(panelAI,
                    "AIAssistant",
                    "AI 助手",
                    "自然语言控制 Revit（基于千问 API）",
                    "MyRevitAddin.AIAssistantCommand");

                // 面板4: 标高工具
                RibbonPanel panelLevels = application.CreateRibbonPanel(TabName, "标高工具");

                AddButton(panelLevels,
                    "ReassignLevelByElevation",
                    "重新归属\n标高",
                    "选择两个标高，将范围内所有构件重新归属到下部标高",
                    "MyRevitAddin.ReassignLevelByElevationCommand");

                // 面板5: 族工具
                RibbonPanel panelFamily = application.CreateRibbonPanel(TabName, "族工具");

                AddButton(panelFamily,
                    "InPlaceFamilyConverter",
                    "内建族转\n可载入族",
                    "选择内建族，导出几何/参数为可在其他项目复用的 .rfa 常规族",
                    "MyRevitAddin.InPlaceFamilyConverter.InPlaceConvertCommand");

                AddButton(panelFamily,
                    "BatchRenameFamilies",
                    "批量族\n重命名",
                    "加前后缀、查找替换、插入/截断、序号编号（深圳报建预设），预览确认后一次性改族名/族类型名",
                    "MyRevitAddin.BatchRenameFamilies.BatchRenameFamiliesCommand");

                AddButton(panelFamily,
                    "BatchFillFamilyParameters",
                    "批量改\n参数",
                    "筛选类别→选源参数→选目标参数→预览→一键复制（支持类型/实例参数互拷）",
                    "MyRevitAddin.BatchParamCopy.BatchParamCopyCommand");

                AddButton(panelFamily,
                    "BatchCreateSharedParams",
                    "批量建\n项目参数",
                    "选类别+设参数属性+批量输入名称，一键创建多个实例参数并绑定到类别",
                    "MyRevitAddin.BatchCreateSharedParams.BatchCreateSharedParamsCommand");

                AddButton(panelFamily,
                    "BatchEditElementParams",
                    "批量修改\n构件属性",
                    "衍生自房间名→属性参数：选任意类别、勾参数、填值，一键批量写入（支持实例/类型参数）",
                    "MyRevitAddin.BatchEditElementParams.BatchEditElementParamsCommand");

                // 面板5a: 机电工具
                RibbonPanel panelMEP = application.CreateRibbonPanel(TabName, "机电工具");
                AddButton(panelMEP,
                    "SelectBySystem",
                    "按系统\n选中构件",
                    "按机电系统类型（管道/风管）筛选并选中构件，支持批量附加属性",
                    "MyRevitAddin.SelectBySystem.SelectBySystemCommand");

                AddButton(panelMEP,
                    "ExcelImportExport",
                    "表格\n导入回导",
                    "导出构件属性到CSV表格→Excel填写数据→回导入Revit更新属性",
                    "MyRevitAddin.ExcelImportExport.ExcelImportExportCommand");

                AddButton(panelMEP,
                    "SplitByMepSystem",
                    "按系统\n拆分模型",
                    "复制当前模型3份，分别删除非风管/非桥架/非水管系统的构件，生成3个独立 .rvt 文件（建筑/基准/视图/图纸等共享内容三份均保留）",
                    "MyRevitAddin.SplitByMepSystem.SplitByMepSystemCommand");

                AddButton(panelMEP,
                    "PipeFittingSzMarker",
                    "管件\n深圳标识",
                    "自动识别管件类型名：含「活接头/三通/弯头/四通」→ 关键字写入参数「深圳构件标识」（管道管件/风管管件/桥架/线管配件可勾选）",
                    "MyRevitAddin.PipeFittingSzMarker.PipeFittingSzMarkerCommand");

                // 面板5b: 房间工具
                RibbonPanel panelRoom = application.CreateRibbonPanel(TabName, "房间工具");
                AddButton(panelRoom,
                    "RoomSeparation",
                    "生成房间\n分割线",
                    "选择楼层，按指定间距自动生成房间分割线网格",
                    "MyRevitAddin.RoomSeparation.RoomSeparationCommand");
                AddButton(panelRoom,
                    "RoomGapDetector",
                    "房间缺口\n检测",
                    "检测房间边界是否闭合，用红色十字标记缺口位置",
                    "MyRevitAddin.RoomGapDetector.RoomGapDetectorCommand");
                AddButton(panelRoom,
                    "BatchRenameRooms",
                    "批量修改\n房间名称",
                    "按规则批量修改房间名称：前后缀、查找替换（支持正则）、截取、自动编号，预览后一键应用",
                    "MyRevitAddin.BatchRenameRooms.BatchRenameRoomsCommand");
                AddButton(panelRoom,
                    "RoomCopyNameToId",
                    "房间名→\n属性参数",
                    "把房间名称批量赋值到所有可写属性参数（自动识别实例/类型参数，可多选）",
                    "MyRevitAddin.RoomCopyNameToId.RoomCopyNameToIdCommand");

                AddButton(panelRoom,
                    "RoomOverlapChecker",
                    "重叠房间\n检测",
                    "一键查询项目中所有重叠房间，双击表格行可在视图中选中对应房间",
                    "MyRevitAddin.RoomOverlapChecker.RoomOverlapCheckerCommand");

                // 面板5c: 门窗工具
                RibbonPanel panelDoorWindow = application.CreateRibbonPanel(TabName, "门窗工具");
                AddButton(panelDoorWindow,
                    "DoorWindowOpeningArea",
                    "门窗\n开启面积",
                    "自动读取门窗的长度和高度参数，计算开启面积(长度×高度÷1000000)并写入指定的实例参数",
                    "MyRevitAddin.DoorWindowOpeningArea.DoorWindowOpeningAreaCommand");

                // 面板5d: 楼板工具
                RibbonPanel panelFloor = application.CreateRibbonPanel(TabName, "楼板工具");
                AddButton(panelFloor,
                    "FloorDimensionCalculator",
                    "楼板\n长宽计算",
                    "通过楼板面积和周长推算矩形长宽尺寸，写入「长度」「宽度」实例属性",
                    "MyRevitAddin.FloorDimensionCalculator.FloorDimensionCalculatorCommand");

                // 面板5e: 柱工具
                RibbonPanel panelColumn = application.CreateRibbonPanel(TabName, "柱工具");
                AddButton(panelColumn,
                    "ColumnDimensionCalculator",
                    "异形柱\n长宽计算",
                    "通过体积和高度推算正方形尺寸，写入「长度」「宽度」实例属性",
                    "MyRevitAddin.ColumnDimensionCalculator.ColumnDimensionCalculatorCommand");

                // 面板6: 分析可视化
                RibbonPanel panelAnalysis = application.CreateRibbonPanel(TabName, "分析可视化");

                AddButton(panelAnalysis,
                    "NetHeightColorizer",
                    "净高\n图例着色",
                    "按净高参数批量对当前视图的填充区域着色——越高越浅、越低越深",
                    "MyRevitAddin.NetHeightColorizer.NetHeightColorizeCommand");

                // 面板7: 测试
                RibbonPanel panelTest = application.CreateRibbonPanel(TabName, "测试");

                AddButton(panelTest,
                    "HelloRevit",
                    "Hello\nRevit",
                    "测试插件是否正常工作",
                    "MyRevitAddin.MyRevitAddin");

                // ====== 注册 AI 助手 Dockable Pane（常驻侧边） ======
                try
                {
                    var paneProvider = new AIAssistantPaneProvider();
                    application.RegisterDockablePane(
                        AI_PANE_ID,
                        "AI 助手",
                        paneProvider);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("AI Pane 注册失败", ex.Message);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ribbon 初始化失败", ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// 初始化 AI 助手状态（Revit 启动时调用，早于任何命令）
        /// </summary>
        private void InitializeAIState()
        {
            // ⚠️ 严禁在代码里硬编码真实/临时 API Key：
            // 1. 临时 STS 签名 Key 一般只有 1 小时有效期（带点号分隔），过期后用户看到"已配置"但实际 401
            // 2. 真实 Key 写进 DLL 容易泄漏、难更换
            // ➡️ 正确做法：默认留空，让用户在面板右上【设置】→ API Key 里粘贴自己的长期有效 Key
            AIAssistantState.ApiKey = null;

            // 默认模型优先选通义千问（DashScope 原生 Key sk- 无点号长期有效）；用户可在面板切
            AIAssistantState.Model = "qwen-turbo";

            // 默认端点：DashScope 原生（最稳定，因为 MAAS 子域名/临时签名容易变）
            // 用户可在面板右上设置里改成自家 MAAS 兼容端点（记得带 /chat/completions）
            AIAssistantState.Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
        }

        private void AddButton(RibbonPanel panel, string name, string text, string tooltip, string className)
        {
            try
            {
                var btnData = new PushButtonData(name, text,
                    Assembly.GetExecutingAssembly().Location, className);

                PushButton btn = panel.AddItem(btnData) as PushButton;
                btn.ToolTip = tooltip;
                btn.LargeImage = GetEmbeddedImage("MyRevitAddin.Resources.icon32.png");
                btn.Image = GetEmbeddedImage("MyRevitAddin.Resources.icon16.png");
            }
            catch
            {
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private BitmapImage GetEmbeddedImage(string resourceName)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    return bitmap;
                }
            }
            catch { return null; }
        }
    }
}
