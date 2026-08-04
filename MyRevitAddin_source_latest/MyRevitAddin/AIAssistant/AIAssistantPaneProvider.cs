using Autodesk.Revit.UI;
using System;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// AI 助手 Dockable 面板 Provider
    /// Revit 2020+ API：使用 DockablePaneProviderData
    /// 注意：Provider 在 Revit 启动时调用，没有 UIApplication/UIDocument，
    ///       面板自己按需获取（通过 AIAssistantState.CurrentUIDoc）
    /// </summary>
    public class AIAssistantPaneProvider : IDockablePaneProvider
    {
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            // 创建 WPF 面板实例
            var control = new AIAssistantPane();

            // 用 API Key 初始化（UIDoc 为空时由面板自己按需获取）
            control.Initialize(AIAssistantState.CurrentUIDoc, AIAssistantState.ApiKey);

            // 静态引用，供后续更新
            AIAssistantPaneHolder.Pane = control;

            // 注入到 Revit 面板
            data.FrameworkElement = control;

            // 初始停靠位置：右侧底部
            // 注：Revit 2020 的 DockablePanes 没有 AnalyticalPropertiesPane，
            //     用 PropertiesPalette（属性栏）作为 TabBehind
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }
    }

    /// <summary>
    /// 面板静态持有（用于不通过 Dockable Pane 访问面板实例）
    /// </summary>
    public static class AIAssistantPaneHolder
    {
        public static AIAssistantPane Pane { get; set; }
    }

    /// <summary>
    /// 启动期持有 UIControlledApplication 引用
    /// 命令期需要 UIApplication，由命令类写入 CurrentUIDoc
    /// </summary>
    public static class RevitCommandDataHolder
    {
        public static UIControlledApplication UIControlledApplication { get; set; }
        public static UIApplication SharedUIApp { get; set; }
        public static UIControlledApplication UIControlledApplicationShim
        {
            get => UIControlledApplication;
            set => UIControlledApplication = value;
        }
    }
}
