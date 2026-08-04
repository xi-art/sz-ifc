using Autodesk.Revit.UI;
using System;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// 静态状态：跨类共享 UIApplication、ApiKey
    /// </summary>
    /// <remarks>
    /// 不要缓存 UIDocument/Document：Revit 会在文档关闭/切换时释放它们，
    /// 缓存的引用会变成 InvalidObject。改为缓存 UIApplication，
    /// 每次需要时取 ActiveUIDocument（永远有效）。
    /// </remarks>
    public static class AIAssistantState
    {
        public static UIApplication CurrentUIApp { get; set; }

        /// <summary>
        /// 取当前活动 UIDocument（每次重新获取，避开 InvalidObject 问题）
        /// </summary>
        public static UIDocument CurrentUIDoc
        {
            get
            {
                try
                {
                    return CurrentUIApp?.ActiveUIDocument;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string ApiKey { get; set; }
        public static string Model { get; set; } = "qwen-turbo";
        /// <summary>
        /// OpenAI 兼容端点（如华为云 ModelArts）
        /// </summary>
        public static string Endpoint { get; set; } = null;  // null = 用 QwenApiClient 内置默认

        /// <summary>
        /// 常用模型下拉列表（兼容 OpenAI Chat Completions 接口的模型）
        /// </summary>
        public static readonly string[] CommonModels = new[]
        {
            "qwen-turbo",
            "qwen-plus",
            "qwen-max",
            "qwen-long",
            "deepseek-v3",
            "deepseek-r1",
            "glm-4",
            "gpt-4o-mini",
            "gpt-4o",
        };
    }
}
