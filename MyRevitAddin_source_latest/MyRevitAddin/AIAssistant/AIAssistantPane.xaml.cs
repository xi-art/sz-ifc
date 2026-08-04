using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyRevitAddin.AIAssistant
{
    public partial class AIAssistantPane : UserControl
    {
        // 不再缓存 _uidoc——每次从 AIAssistantState.CurrentUIDoc 动态获取
        private QwenApiClient _aiClient;
        private AssistantMemory _memory;
        private string _apiKey;
        private bool _isProcessing;
        private bool _debugMode;
        private readonly List<Dictionary<string, object>> _chatHistory = new List<Dictionary<string, object>>();
        private const int MAX_HISTORY_MESSAGES = 20; // 最近 10 轮 = 20 条消息（user + assistant 交替）

        /// <summary>
        /// 静态持有 UIApplication 引用（由 AIAssistantCommand 设置），作为 AIAssistantState.CurrentUIApp 的备份
        /// </summary>
        public static UIApplication SharedUIApp { get; set; }

        public AIAssistantPane()
        {
            InitializeComponent();
            _memory = new AssistantMemory();
            ShowWelcome();
            RefreshDocStatus();
        }

        public void Initialize(UIDocument uidoc, string apiKey)
        {
            // uidoc 参数仅用于首次显示连接状态，实际执行不依赖它
            _apiKey = apiKey;
            if (!string.IsNullOrEmpty(apiKey))
                AIAssistantState.ApiKey = apiKey;

            // 初始化设置面板控件
            try
            {
                if (CmbModel != null)
                {
                    CmbModel.Items.Clear();
                    foreach (var m in AIAssistantState.CommonModels)
                        CmbModel.Items.Add(m);
                    if (!string.IsNullOrEmpty(AIAssistantState.Model))
                        CmbModel.Text = AIAssistantState.Model;
                }
                if (TxtEndpoint != null)
                    TxtEndpoint.Text = AIAssistantState.Endpoint ?? "";
                if (TxtApiKey != null)
                    TxtApiKey.Text = AIAssistantState.ApiKey ?? _apiKey ?? "";
            }
            catch { }

            RefreshDocStatus();

            string effectiveKey = AIAssistantState.ApiKey ?? _apiKey;
            if (!string.IsNullOrEmpty(effectiveKey))
            {
                RecreateAIClient("Initialize(" + (uidoc?.GetHashCode().ToString() ?? "null") + ")");
                UpdateConnStatusBanner();
            }
            else
            {
                UpdateConnStatusBanner();
            }
        }

        /// <summary>
        /// 切换设置面板显示
        /// </summary>
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (PanelSettings.Visibility == System.Windows.Visibility.Visible)
                PanelSettings.Visibility = System.Windows.Visibility.Collapsed;
            else
                PanelSettings.Visibility = System.Windows.Visibility.Visible;
        }

        private void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbModel.SelectedItem == null) return;
            string newModel = CmbModel.SelectedItem.ToString();
            AIAssistantState.Model = newModel;
            RecreateAIClient("切换模型");
            UpdateConnStatusBanner();
        }

        private void TxtEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            string newEp = TxtEndpoint.Text.Trim();
            AIAssistantState.Endpoint = string.IsNullOrEmpty(newEp) ? null : newEp;
            RecreateAIClient("切换端点");
        }

        private void TxtApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtApiKey == null) return;
            string newKey = TxtApiKey.Text.Trim();
            if (string.IsNullOrEmpty(newKey)) return;
            AIAssistantState.ApiKey = newKey;
            // 同时更新命令的静态 KEY 备份
            try { _apiKey = newKey; } catch { }
            RecreateAIClient("修改 API Key");
            UpdateConnStatusBanner();
        }

        /// <summary>
        /// 统一入口：用 AIAssistantState 最新值重建 API 客户端
        /// </summary>
        private void RecreateAIClient(string reason)
        {
            try
            {
                string effectiveKey = AIAssistantState.ApiKey ?? _apiKey;
                if (string.IsNullOrEmpty(effectiveKey)) return;
                _aiClient = new QwenApiClient(
                    effectiveKey,
                    AIAssistantState.Model ?? "qwen-turbo",
                    AIAssistantState.Endpoint);
                AppendDebug("CFG", "✅ 已重新初始化 QwenApiClient，原因: " + reason
                    + "\n  模型: " + (AIAssistantState.Model ?? "qwen-turbo")
                    + "\n  端点: " + (AIAssistantState.Endpoint ?? "(默认 DashScope)")
                    + "\n  Key  前 8 位: " + (effectiveKey.Length > 8 ? effectiveKey.Substring(0, 8) + "..." : effectiveKey));
            }
            catch (Exception ex)
            {
                HistoryLogger.Error("RecreateAIClient(" + reason + ")", ex);
                AppendDebug("CFG", "❌ 重建 API 客户端失败: " + reason + " -> " + ex.Message);
            }
        }

        private void UpdateConnStatusBanner()
        {
            try
            {
                if (TxtConnStatus == null) return;
                string effKey = AIAssistantState.ApiKey ?? _apiKey;
                string model = AIAssistantState.Model ?? "qwen-turbo";
                string ep = AIAssistantState.Endpoint ?? "";
                string epShort = "(默认)";
                if (!string.IsNullOrEmpty(ep))
                {
                    try
                    {
                        var u = new Uri(ep);
                        epShort = u.Host;
                    }
                    catch { epShort = ep.Length > 28 ? ep.Substring(0, 28) + "..." : ep; }
                }
                if (string.IsNullOrEmpty(effKey))
                {
                    TxtConnStatus.Text = "⚠️ 请先填入 API Key（点⚙设置→API Key输入框，粘贴后回车） · 端点:" + epShort;
                    TxtConnStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    return;
                }
                TxtConnStatus.Text = "🔵 模型:" + model + " · 端点:" + epShort
                    + " · Key:"
                    + (effKey.Length > 10 ? effKey.Substring(0, 10) + "..." : effKey)
                    + "（点测试连接验证）";
                TxtConnStatus.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
            }
            catch { }
        }

        /// <summary>
        /// 供 AIAssistantCommand 调用，传入最新的 UIApplication
        /// </summary>
        public void SetUIApplication(UIApplication uiapp)
        {
            SharedUIApp = uiapp;
            AIAssistantState.CurrentUIApp = uiapp;
        }

        /// <summary>
        /// 尝试从 SharedUIApp 或 AIAssistantState.CurrentUIApp 获取 UIApplication
        /// </summary>
        private void TrySetUIApp()
        {
            if (SharedUIApp != null)
            {
                AIAssistantState.CurrentUIApp = SharedUIApp;
            }
            else if (AIAssistantState.CurrentUIApp != null)
            {
                SharedUIApp = AIAssistantState.CurrentUIApp;
            }
        }

        // ==================== 系统提示词（含记忆上下文）====================

        private string BuildSystemPrompt()
        {
            string memoryContext = _memory.GetMemorySummary();

            return
                "你是 Revit BIM 助手。可以使用以下工具操作 Revit 模型：\n" +
                "===== 工具列表（按优先级选择）=====\n" +
                "-- 信息查询类：\n" +
                "1. get_document_info - 获取当前打开的文档信息（文件名、路径、是否已保存、项目作者、客户、图元总数等）。用户问'当前文件是什么''这个项目叫什么''打开的是什么文件'时必须先调用\n" +
                "2. get_all_levels - 获取项目所有标高列表（按高度排序，含米单位）\n" +
                "3. get_all_views(view_type) - 获取项目所有视图/图纸列表，可选 view_type 过滤：FloorPlan 平面、ThreeD 3D、DrawingSheet 图纸、Section 剖面、Elevation 立面、All 全部\n" +
                "4. get_project_sheets - 获取项目全部图纸（Sheet）列表，含编号、名称、视口数量\n" +
                "5. get_selected_elements - 获取当前在 Revit 中已选中的图元\n" +
                "6. select_elements_by_category(category_name, limit) - 按类别名批量选择图元（在 Revit 界面中高亮选中）。支持中文类别名：门、窗、墙、楼板、管道、风管、设备、家具、柱、梁、楼梯、轴网等。操作修改类需求时，如果用户没有手动选中元素，必须先用这个工具选中后再操作\n\n" +
                "-- 操作修改类：\n" +
                "7. set_instance_parameter - 修改图元实例参数（需要先通过选中工具获取 element_ids）\n" +
                "8. batch_set_parameter - 批量按规则设置参数（如按标高范围设置楼层参数）\n" +
                "9. replace_family - 替换族类型\n\n" +
                "-- 记忆类：\n" +
                "10. save_memory - 保存记忆到本地\n" +
                "11. search_memory - 搜索过往记忆\n\n" +
                "===== 重要规则（必须严格遵守）=====\n" +
                "1. 用户问任何当前项目/当前文档/当前打开的文件相关问题（如'现在打开的文件是什么''项目名称是什么''这是哪个项目'），必须先调用 **get_document_info**，根据返回结果回答，绝对不能回答'无法获取'！\n" +
                "2. 用户问'有哪些标高''标高列表''楼层有多高'，调用 **get_all_levels**\n" +
                "3. 用户问'有哪些视图''有哪些图纸''有多少张图纸'，调用 **get_project_sheets** 或 **get_all_views**\n" +
                "4. 用户说'选择所有门/窗/墙/管道/设备'或'把所有XX改成XXX'，必须先调用 **select_elements_by_category** 选中元素，再执行后续修改\n" +
                "5. 用户说'记得XXX''之前是怎么做的''同样的操作再来一次''类似之前''再次操作一次'时，第一步必须先调用 **search_memory** 查询本地记忆\n" +
                "6. 如果 search_memory 找到了相关记忆，**优先复用记忆中的成功经验**，并告诉用户找到了历史记录\n" +
                "7. 用户说'记住XXX''记一下XX'时，操作完成后调用 **save_memory** 保存\n" +
                "8. 涉及'楼层/标高'批量操作时，**优先用 batch_set_parameter**\n" +
                "9. 涉及'替换/改成/换成'族类型时，**优先用 replace_family**\n" +
                "10. 执行修改操作前，必须先调用 **get_selected_elements** 或 **select_elements_by_category** 确认操作对象\n" +
                "11. 回复要简洁，用中文，语气自然\n" +
                "12. 回复里要明确告诉用户操作结果（成功数量、失败数量等）\n\n" +
                "===== 本地记忆内容（来自之前的对话和操作）：=====\n" +
                memoryContext + "\n" +
                "===== 记忆上下文结束 =====\n";
        }

        // ==================== 前置自动工具查询（100% 避免 AI 忘记调用工具）====================

        private string PreExecuteAutoQuery(string userInput)
        {
            try
            {
                var uidoc = AIAssistantState.CurrentUIDoc;
                if (uidoc == null || uidoc.Document == null)
                {
                    AppendDebug("KEYMATCH", "用户输入：" + userInput + "\n  ⚠️ 跳过自动查询：CurrentUIDoc = null");
                    return null;
                }

                var executor = new RevitOperationExecutor(null);
                string inputLower = userInput.ToLower();

                AppendDebug("KEYMATCH", "用户输入：" + userInput + "\n  inputLower=" + inputLower);

                // 0. 泛化兜底查询（用户问"它是什么""相关信息""详情"等开放式问题，且前面规则未命中）
                if (Regex.IsMatch(userInput, @"(它|这个|当前|现在|此).*(是什么|什么情况|相关信息|详细信息|详情|信息|内容|呢\?)") ||
                    (inputLower.Contains("info") && inputLower.Contains("what") && !inputLower.Contains("file")))
                {
                    AppendDebug("KEYMATCH", "✅ 命中：规则#0 泛化兜底查询");
                    AppendStatus("🔍 自动查询：当前文档基础信息...");
                    // 先查文档信息
                    var docArgs = new Dictionary<string, object>();
                    AppendDebug("TOOL-CALL", "工具: get_document_info\n参数: (空)");
                    var docResult = executor.ExecuteDirect("get_document_info", docArgs);
                    AppendDebug("TOOL-RESP", "泛化查询先查 get_document_info: " + docResult);
                    string context = BuildAutoContext("当前文档基础信息", "get_document_info", docResult);
                    // 如果有标高，补充标高信息
                    if (Regex.IsMatch(userInput, @"(标高|楼层|高度|level)"))
                    {
                        AppendDebug("TOOL-CALL", "泛化查询补充调用: get_all_levels");
                        string levelResult = executor.ExecuteDirect("get_all_levels", new Dictionary<string, object>());
                        AppendDebug("TOOL-RESP", "get_all_levels 返回: " + levelResult);
                        context += "\n\n" + BuildAutoContext("项目标高列表", "get_all_levels", levelResult);
                    }
                    return context;
                }

                // 1. 当前文档 / 当前文件 / 当前项目信息 查询
                if (Regex.IsMatch(userInput, @"(当前|打开的|这个|现在).*(文件|项目|文档|rvt)") ||
                    Regex.IsMatch(userInput, @"(文件|项目|文档).*(叫什么|是什么|名字|路径|标题)") ||
                    inputLower.Contains("project name") || inputLower.Contains("file name") ||
                    inputLower.Contains("what file") || inputLower.Contains("what project"))
                {
                    AppendDebug("KEYMATCH", "✅ 命中：规则#1 当前文档查询");
                    AppendStatus("🔍 自动查询：当前文档信息...");
                    var args = new Dictionary<string, object>();
                    AppendDebug("TOOL-CALL", "工具: get_document_info\n参数: (空)");
                    var result = executor.ExecuteDirect("get_document_info", args);
                    AppendDebug("TOOL-RESP", "get_document_info 原始返回 (" + result.Length + " 字符):\n" + result);
                    return BuildAutoContext("当前打开的 Revit 文档/项目信息", "get_document_info", result);
                }

                // 2. 标高列表 / 楼层 查询
                if (Regex.IsMatch(userInput, @"(所有|全部|有哪些|列一下|列表).*(标高|楼层|层高)") ||
                    Regex.IsMatch(userInput, @"(标高|楼层).*(有多少|有多高|列表|是什么)"))
                {
                    AppendDebug("KEYMATCH", "✅ 命中：规则#2 标高/楼层查询");
                    AppendStatus("🔍 自动查询：项目标高列表...");
                    var args = new Dictionary<string, object>();
                    AppendDebug("TOOL-CALL", "工具: get_all_levels\n参数: (空)");
                    string result = executor.ExecuteDirect("get_all_levels", args);
                    AppendDebug("TOOL-RESP", "get_all_levels 原始返回 (" + result.Length + " 字符):\n" + result);
                    return BuildAutoContext("项目所有标高（楼层）列表", "get_all_levels", result);
                }

                // 3. 图纸查询
                if (Regex.IsMatch(userInput, @"(所有|有哪些|多少|全部|列一下).*(图纸|sheet|图框)") ||
                    Regex.IsMatch(userInput, @"图纸.*(数量|列表|编号|名称)"))
                {
                    AppendDebug("KEYMATCH", "✅ 命中：规则#3 图纸查询");
                    AppendStatus("🔍 自动查询：项目图纸列表...");
                    var args = new Dictionary<string, object>();
                    AppendDebug("TOOL-CALL", "工具: get_project_sheets\n参数: (空)");
                    string result = executor.ExecuteDirect("get_project_sheets", args);
                    AppendDebug("TOOL-RESP", "get_project_sheets 原始返回 (" + result.Length + " 字符):\n" + result);
                    return BuildAutoContext("项目所有图纸列表", "get_project_sheets", result);
                }

                // 4. 视图查询（不含图纸类）
                if (Regex.IsMatch(userInput, @"(所有|有哪些|全部|列一下).*(视图|平面|剖面|立面|3d|三维)") &&
                    !Regex.IsMatch(userInput, @"(图纸|sheet)"))
                {
                    AppendDebug("KEYMATCH", "✅ 命中：规则#4 视图查询");
                    AppendStatus("🔍 自动查询：项目视图列表...");
                    var args = new Dictionary<string, object>();
                    // 如果指定了类型，传对应参数
                    if (inputLower.Contains("平面")) args["view_type"] = "FloorPlan";
                    else if (inputLower.Contains("3d") || userInput.Contains("三维")) args["view_type"] = "ThreeD";
                    else if (userInput.Contains("剖面")) args["view_type"] = "Section";
                    else if (userInput.Contains("立面")) args["view_type"] = "Elevation";
                    else args["view_type"] = "All";
                    AppendDebug("TOOL-CALL", "工具: get_all_views\n参数: view_type=" + (args.ContainsKey("view_type") ? args["view_type"] : "(无)"));
                    string result = executor.ExecuteDirect("get_all_views", args);
                    AppendDebug("TOOL-RESP", "get_all_views 原始返回 (" + result.Length + " 字符):\n" + result);
                    return BuildAutoContext("项目视图列表", "get_all_views", result);
                }

                // 5. 按类别选择元素（选择所有 XX）
                var selectMatch = Regex.Match(userInput, @"(选中|选择|帮我选|批量选).*(所有|全部)?\s*(门|窗|墙|墙体|楼板|柱|梁|管道|风管|设备|家具|楼梯|屋顶|天花板|吊顶|轴网|标高|族实例)");
                if (selectMatch.Success)
                {
                    string cat = selectMatch.Groups[3].Value;
                    AppendDebug("KEYMATCH", "✅ 命中：规则#5 按类别选元素 (类别=" + cat + ")");
                    AppendStatus("🔍 自动执行：按类别选中所有" + cat + "...");
                    var args = new Dictionary<string, object>
                    {
                        { "category_name", cat },
                        { "limit", 500 }
                    };
                    AppendDebug("TOOL-CALL", "工具: select_elements_by_category\n参数: category_name=" + cat + ", limit=500");
                    string result = executor.ExecuteDirect("select_elements_by_category", args);
                    AppendDebug("TOOL-RESP", "select_elements_by_category 原始返回 (" + result.Length + " 字符):\n" + result);
                    _memory.SaveOperation("select_elements_by_category", args, result);
                    return BuildAutoContext("已在 Revit 中按类别选择图元结果", "select_elements_by_category", result);
                }

                // 6. 当前选中的元素（用户问"我选中了什么"）
                if (Regex.IsMatch(userInput, @"(选中|选择).*什么|当前.*选中|现在.*选了") &&
                    !Regex.IsMatch(userInput, @"所有|全部|帮我选|选择所有"))
                {
                    AppendDebug("KEYMATCH", "✅ 命中：规则#6 当前选中查询");
                    AppendStatus("🔍 自动查询：当前选中的图元...");
                    var args = new Dictionary<string, object>();
                    AppendDebug("TOOL-CALL", "工具: get_selected_elements\n参数: (空)");
                    string result = executor.ExecuteDirect("get_selected_elements", args);
                    AppendDebug("TOOL-RESP", "get_selected_elements 原始返回 (" + result.Length + " 字符):\n" + result);
                    return BuildAutoContext("当前在 Revit 中已选中的图元信息", "get_selected_elements", result);
                }

                AppendDebug("KEYMATCH", "❌ 未命中任何自动查询规则，将交给 AI 自主判断是否调用工具");
            }
            catch (Exception ex)
            {
                AppendDebug("KEYMATCH", "⚠️ 自动查询异常：" + ex.GetType().Name + ": " + ex.Message);
                // 前置查询失败不影响主流程
            }

            return null;
        }

        private static string BuildAutoContext(string label, string toolName, string toolResultJson)
        {
            return
                "\n========== 【系统已自动查询获取以下信息，请基于这些内容回答用户问题，绝对不要再说'无法获取'或'不在工具范围'】==========\n" +
                "工具: " + toolName + "\n" +
                label + ":\n" +
                toolResultJson + "\n" +
                "==================== 自动查询上下文结束 ====================\n";
        }

        // ==================== 兜底：检测 AI 回复是否还是"无法获取"，若是则显示真实信息 ====================

        private string ApplyAnswerFallback(string userInput, string aiAnswer)
        {
            if (string.IsNullOrEmpty(aiAnswer)) return aiAnswer;
            string lower = aiAnswer.ToLower();

            bool stillSaysCannotAccess =
                lower.Contains("无法获取") || lower.Contains("不在可用工具") || lower.Contains("不在工具范围") ||
                lower.Contains("无法直接获取") || lower.Contains("请在 revit") || lower.Contains("查看详细信息") ||
                lower.Contains("信息不可用") || lower.Contains("cannot get") || lower.Contains("unable to get") ||
                lower.Contains("no access") || lower.Contains("not available");

            AppendDebug("FALLBACK",
                "检测 AI 回答是否需要兜底...\n" +
                "  命中关键词? " + stillSaysCannotAccess + "\n" +
                "  AI 回答摘要: " + (aiAnswer.Length > 180 ? aiAnswer.Substring(0, 180) + "..." : aiAnswer.Replace("\n", " ")));

            if (!stillSaysCannotAccess) return aiAnswer;

            // 如果匹配到常见查询，本地执行并给用户真实结果
            try
            {
                var executor = new RevitOperationExecutor(null);
                string inputLower = userInput.ToLower();
                string directResult = null;
                string directTool = null;

                if (Regex.IsMatch(userInput, @"(当前|打开的|这个|现在).*(文件|项目|文档|rvt)") ||
                    Regex.IsMatch(userInput, @"(文件|项目|文档).*(叫什么|是什么|名字|路径|标题)"))
                {
                    directResult = executor.ExecuteDirect("get_document_info", new Dictionary<string, object>());
                    directTool = "get_document_info";
                }
                else if (Regex.IsMatch(userInput, @"(所有|有哪些|列一下|全部).*(标高|楼层)"))
                {
                    directResult = executor.ExecuteDirect("get_all_levels", new Dictionary<string, object>());
                    directTool = "get_all_levels";
                }
                else if (Regex.IsMatch(userInput, @"(所有|有哪些|多少|列一下).*(图纸|sheet)"))
                {
                    directResult = executor.ExecuteDirect("get_project_sheets", new Dictionary<string, object>());
                    directTool = "get_project_sheets";
                }
                else if (Regex.IsMatch(userInput, @"(所有|有哪些|列一下).*(视图|平面)"))
                {
                    directResult = executor.ExecuteDirect("get_all_views", new Dictionary<string, object> { { "view_type", "All" } });
                    directTool = "get_all_views";
                }

                if (!string.IsNullOrEmpty(directResult))
                {
                    AppendDebug("FALLBACK",
                        "✅ 兜底已触发：本地执行 " + directTool + "，用真实结果覆盖 AI 回答\n" +
                        "真实结果 (" + directResult.Length + " 字符):\n" +
                        directResult);
                    return
                        "⚠️ AI 回答与真实工具结果不一致，以下为 **本地直接执行 " + directTool + " 工具** 的真实结果：\n\n" +
                        "──────────────────\n" +
                        directResult + "\n" +
                        "──────────────────\n" +
                        "\n（原始 AI 回答已被覆盖，因为 AI 没有正确调用已有的工具）";
                }
                else
                {
                    AppendDebug("FALLBACK", "⚠️ 命中兜底关键词，但未能匹配到本地执行的工具，保持 AI 原回答");
                }
            }
            catch (Exception ex)
            {
                AppendDebug("FALLBACK", "⚠️ 兜底异常：" + ex.GetType().Name + ": " + ex.Message);
            }

            return aiAnswer;
        }

        // ==================== 发送消息 ====================

        private async void SendMessage()
        {
            if (_isProcessing) return;
            Dispatcher uiDispatcher = this.Dispatcher;
            string userInput = TxtInput.Text.Trim();
            if (string.IsNullOrEmpty(userInput)) return;

            string effKey = AIAssistantState.ApiKey ?? _apiKey;
            if (string.IsNullOrEmpty(effKey) || _aiClient == null)
            {
                RecreateAIClient("SendMessage-preflight");
            }
            effKey = AIAssistantState.ApiKey ?? _apiKey;
            if (_aiClient == null || string.IsNullOrEmpty(effKey))
            {
                AppendError("⚠️ 请先在右上角【设置】里填写有效的 API Key，再点【测试连接】验证。\n" +
                    "当前 Key: " + (string.IsNullOrEmpty(effKey) ? "(空)" : effKey.Substring(0, Math.Min(8, effKey.Length)) + "...") + "\n" +
                    "获取地址：https://dashscope.console.aliyun.com/ （DashScope）\n" +
                    "           或阿里云百炼 https://bailian.console.aliyun.com （MAAS 模型）");
                try { UpdateConnStatusBanner(); } catch { }
                return;
            }
            // 发送前再次用最新 AIAssistantState 参数重建客户端（确保最新 Key/模型/端点生效）
            RecreateAIClient("SendMessage-resync");

            AppendMessage("user", userInput);
            TxtInput.Clear();
            SetProcessing(true);

            var userMsg = new Dictionary<string, object>
            {
                { "role", "user" },
                { "content", userInput }
            };
            _chatHistory.Add(userMsg);
            if (_chatHistory.Count > MAX_HISTORY_MESSAGES)
                _chatHistory.RemoveRange(0, _chatHistory.Count - MAX_HISTORY_MESSAGES);

            var historyForApi = _chatHistory.Count > 1
                ? _chatHistory.Take(_chatHistory.Count - 1).ToList()
                : new List<Dictionary<string, object>>();

            try
            {
                // ===== 前置自动工具查询（C# 本地判断并直接执行，不依赖 AI 是否调用）=====
                // 这里在第一个 await 之前执行，本来就在 UI 线程；但显式通过 uiDispatcher 运行以确保安全
                string autoContext = uiDispatcher.Invoke(() => {
                    TrySetUIApp();
                    RefreshDocStatus();
                    return PreExecuteAutoQuery(userInput);
                });

                string aiInput = string.IsNullOrEmpty(autoContext)
                    ? userInput
                    : (userInput + "\n\n" + autoContext);

                AppendDebug("AI-INPUT",
                    "autoContext 命中? " + !string.IsNullOrEmpty(autoContext) + "\n" +
                    "短期记忆消息数: " + historyForApi.Count + " / " + MAX_HISTORY_MESSAGES + "\n" +
                    "最终发给 AI 的 aiInput (" + aiInput.Length + " 字符)，前 400 字符预览：\n" +
                    "──────────\n" +
                    (aiInput.Length > 400 ? aiInput.Substring(0, 400) + "\n...(截断，共 " + aiInput.Length + " 字符)" : aiInput) +
                    "\n──────────");

                bool memoryHint = ContainsMemoryKeyword(userInput);

                var tools = RevitToolDefinitions.GetAllTools();
                string systemPrompt = BuildSystemPrompt();

                AppendStatus("正在调用千问 API (模型=" + (AIAssistantState.Model ?? "qwen-turbo") + ")...");

                var response = await _aiClient.ChatAsync(aiInput, tools, systemPrompt, historyForApi)
                    .ConfigureAwait(false);

                string rawResponse = _aiClient.GetContent(response);
                AppendDebug("AI-RAW-RESP",
                    "API 调用成功，AI 纯文本响应 (" + (rawResponse ?? "").Length + " 字符)：\n" +
                    "──────────\n" +
                    (string.IsNullOrEmpty(rawResponse) ? "(空)" :
                     (rawResponse.Length > 600 ? rawResponse.Substring(0, 600) + "\n...(截断)" : rawResponse)) +
                    "\n──────────");
                AppendStatus("API 返回成功");

                var toolCall = _aiClient.GetFirstToolCall(response);
                AppendDebug("AI-TOOL-DECISION",
                    "AI 是否自主调用工具? " + (toolCall != null ? "是" : "否") +
                    (toolCall != null ? ("\n  工具名: " + GetToolName(toolCall) +
                                        "\n  参数 JSON: " + (GetToolArgs(toolCall)?.Count.ToString() ?? "0") + " 个键") : ""));

                if (toolCall != null)
                {
                    string toolName = GetToolName(toolCall);
                    AppendMessage("assistant", "🔧 调用工具: " + toolName);

                    // ⚠️ 从线程池线程回到 UI 线程才能访问 Revit COM 对象
                    Tuple<string, Dictionary<string, object>, string> toolBundle =
                        uiDispatcher.Invoke<Tuple<string, Dictionary<string, object>, string>>(() =>
                        {
                            TrySetUIApp();
                            RefreshDocStatus();
                            var executor = new RevitOperationExecutor(null);
                            string tResult = executor.Execute(toolCall);
                            var tArgs = GetToolArgs(toolCall);
                            _memory.SaveOperation(toolName, tArgs, tResult);
                            return Tuple.Create(tResult, tArgs, toolName);
                        });
                    string toolResult = toolBundle.Item1;
                    Dictionary<string, object> toolArgs = toolBundle.Item2;
                    string resolvedName = toolBundle.Item3;

                    AppendDebug("AI-TOOL-RESULT",
                        "AI 调用 " + resolvedName + " 结果 (" + toolResult.Length + " 字符)：\n" +
                        (toolResult.Length > 600 ? toolResult.Substring(0, 600) + "\n...(截断)" : toolResult));

                    var toolResults = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            { "tool_call_id", GetToolCallId(toolCall) },
                            { "content", toolResult }
                        }
                    };

                    AppendStatus("工具执行完成，等待 AI 总结...");
                    var finalResp = await _aiClient.ChatWithToolResultAsync(
                        aiInput,
                        new List<Dictionary<string, object>> { toolCall },
                        toolResults,
                        tools,
                        systemPrompt,
                        historyForApi).ConfigureAwait(false);

                    string finalContent = _aiClient.GetContent(finalResp);
                    AppendDebug("AI-FINAL-SUMMARY",
                        "AI 工具结果总结 (" + (finalContent ?? "").Length + " 字符)：\n" +
                        (string.IsNullOrEmpty(finalContent) ? "(空)" :
                         (finalContent.Length > 600 ? finalContent.Substring(0, 600) + "\n...(截断)" : finalContent)));

                    if (!string.IsNullOrEmpty(finalContent))
                    {
                        // ⚠️ ApplyAnswerFallback 内部要访问 Revit API，强制 UI 线程
                        string fallback = uiDispatcher.Invoke<string>(() => ApplyAnswerFallback(userInput, finalContent));
                        AppendMessage("assistant", fallback);
                        _memory.SaveConversation(userInput, fallback, resolvedName);
                        _chatHistory.Add(new Dictionary<string, object>
                        {
                            { "role", "assistant" },
                            { "content", fallback }
                        });
                    }
                    else
                    {
                        string altAns = "✅ 操作已完成。\n\n工具: " + resolvedName + "\n结果: " + toolResult;
                        AppendMessage("assistant", altAns);
                        _chatHistory.Add(new Dictionary<string, object>
                        {
                            { "role", "assistant" },
                            { "content", altAns }
                        });
                    }
                    if (_chatHistory.Count > MAX_HISTORY_MESSAGES)
                        _chatHistory.RemoveRange(0, _chatHistory.Count - MAX_HISTORY_MESSAGES);
                }
                else if (!string.IsNullOrEmpty(rawResponse))
                {
                    // ⚠️ ApplyAnswerFallback 内部要访问 Revit API，强制 UI 线程
                    string withFallback = uiDispatcher.Invoke<string>(() => ApplyAnswerFallback(userInput, rawResponse));
                    AppendMessage("assistant", withFallback);
                    _memory.SaveConversation(userInput, withFallback, "chat");
                    _chatHistory.Add(new Dictionary<string, object>
                    {
                        { "role", "assistant" },
                        { "content", withFallback }
                    });
                    if (_chatHistory.Count > MAX_HISTORY_MESSAGES)
                        _chatHistory.RemoveRange(0, _chatHistory.Count - MAX_HISTORY_MESSAGES);
                }
                else
                {
                    AppendError("API 返回为空。可能是 Key 无效、模型不可用或网络问题。\n请点\"测试连接\"验证。");
                }
            }
            catch (Exception ex)
            {
                HistoryLogger.Error("AIAssistantPane.SendMessage", ex,
                    "用户输入: " + userInput);
                AppendDebug("ERROR",
                    "主流程异常: " + ex.GetType().Name + "\n" +
                    "Message: " + ex.Message + "\n" +
                    "StackTrace: " + ex.StackTrace);
                AppendError("错误: " + ex.Message + "\n\n" + ex.GetType().Name);
            }
            finally
            {
                try { uiDispatcher.Invoke(() => { TrySetUIApp(); RefreshDocStatus(); }); } catch { }
                SetProcessing(false);
                AppendStatus("就绪");
                TxtInput.Focus();
            }
        }

        // ==================== 测试连接 ====================

        private async void BtnTestConn_Click(object sender, RoutedEventArgs e)
        {
            // 先用最新 AIAssistantState 重建一次客户端，避免用户刚改完 Key/模型就点测试
            try { RecreateAIClient("测试连接-点击前重同步"); } catch { }

            if (_aiClient == null)
            {
                AppendError("⚠️ API Key 未配置：请在右上【⚙ 设置】里的【API Key】输入框粘贴你自己的长期有效 Key，回车生效后再点本按钮。\n\n获取长期有效 Key：\n• DashScope（qwen 系列）：https://dashscope.console.aliyun.com/apiKey  \n  格式：sk-xxxx（48+ 位十六进制，无点号）\n• 百炼 MAAS：https://bailian.console.aliyun.com  \n  格式：sk-xxxx 或 sk-ws-xxxx（工作空间级）");
                UpdateConnStatusBanner();
                return;
            }

            BtnTestConn.IsEnabled = false;
            TxtConnStatus.Text = "🔄 测试中（1/3 DNS+TCP 探测端点连通）...";
            TxtConnStatus.Foreground = new SolidColorBrush(Colors.Yellow);

            // 阶段 1：快速网络探测（DNS 解析 + TCP 握手到 443，4 秒超时）—— 判断是网络层问题还是 API 层问题
            bool networkOk = false;
            string networkInfo = "";
            try
            {
                string ep = AIAssistantState.Endpoint ?? "";
                if (!string.IsNullOrWhiteSpace(ep) && Uri.TryCreate(ep, UriKind.Absolute, out var uri))
                {
                    networkInfo += $"端点主机: {uri.Host}:{uri.Port}\n";
                    networkInfo += $"端点路径: {uri.AbsolutePath}\n";
                    using (var tcp = new System.Net.Sockets.TcpClient())
                    {
                        var t = tcp.ConnectAsync(uri.Host, uri.Port);
                        var timeout = Task.Delay(4000);
                        var finished = await Task.WhenAny(t, timeout);
                        networkOk = finished == t && t.Exception == null;
                    }
                    networkInfo += networkOk ? "✅ TCP 443 连通" : "❌ TCP 443 超时 / 拒绝连接（可能网络/防火墙/代理/域名填错）";
                }
            }
            catch (Exception ne)
            {
                networkInfo = "❌ 端点探测异常: " + ne.Message;
            }

            TxtConnStatus.Text = "🔄 测试中（2/3 API 请求）...";

            try
            {
                string result = await _aiClient.TestConnection();
                AppendMessage("assistant",
                    "✅ 连接测试成功！\n\n" + result + "\n\n" +
                    "── 诊断摘要 ──\n" +
                    networkInfo + "\n" +
                    "当前模型: " + (AIAssistantState.Model ?? "qwen-turbo") + "\n" +
                    "当前端点: " + (AIAssistantState.Endpoint ?? "(默认 DashScope)"));
                TxtConnStatus.Text = "✅ 连接正常 · " + (AIAssistantState.Model ?? "qwen-turbo");
                TxtConnStatus.Foreground = new SolidColorBrush(Colors.LightGreen);
            }
            catch (Exception ex)
            {
                // 分类错误
                string rawMsg = ex.Message ?? "";
                string kind = "其他错误";
                if (!networkOk) kind = "❌ 网络层（连不上端点主机 443）";
                else if (rawMsg.Contains("401") || rawMsg.Contains("InvalidApiKey") || rawMsg.Contains("未授权")) kind = "❌ 鉴权层 HTTP 401（Key 过期/错误/端点不匹配）";
                else if (rawMsg.Contains("404") || rawMsg.Contains("NotFound") || rawMsg.Contains("模型不存在")) kind = "❌ 路由/模型层 HTTP 404";
                else if (rawMsg.Contains("429")) kind = "⚠️ 限流 HTTP 429";
                else if (rawMsg.Contains("500") || rawMsg.Contains("502") || rawMsg.Contains("503")) kind = "⚠️ 服务器端 HTTP 5xx";
                else if (rawMsg.Contains("超时") || rawMsg.Contains("Timeout")) kind = "⚠️ 请求超时";

                AppendError(
                    "🚫 " + kind + "\n\n" +
                    "── 诊断摘要 ──\n" +
                    networkInfo + "\n" +
                    "── 原始错误 ──\n" + rawMsg + "\n\n" +
                    "── 建议按顺序排查 ──\n" +
                    "1. 重新生成 Key 再粘贴：【⚙ 设置】→ API Key 输入框贴完记得回车\n" +
                    "   DashScope：https://dashscope.console.aliyun.com/apiKey\n" +
                    "   百炼 MAAS：https://bailian.console.aliyun.com\n" +
                    "2. 确认端点 ↔ Key 匹配：DashScope 端点 → DashScope Key；MAAS (*.maas.aliyuncs.com) → 对应工作空间 Key\n" +
                    "3. 模型可访问性：百炼 MAAS 里必须在对应工作空间下开通了该模型（deepseek/qwen 等）\n" +
                    "4. 网络/代理：公司网络或代理拦截请改用热点；端点域名结尾对吗？（不要漏 /chat/completions）\n" +
                    "5. 账号是否欠费/Key 是否被禁用。");
                TxtConnStatus.Text = "❌ " + kind;
                TxtConnStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
            }
            finally
            {
                BtnTestConn.IsEnabled = true;
            }
        }

        // ==================== 调试工具栏事件 ====================

        private void ChkDebugMode_Changed(object sender, RoutedEventArgs e)
        {
            _debugMode = ChkDebugMode.IsChecked == true;
            if (_debugMode)
            {
                AppendDebug("DEB-SYSTEM", "调试模式已开启，将显示以下链路信息：\n  • 关键词匹配命中\n  • 工具调用名称与参数\n  • 工具执行原始返回 JSON\n  • 发给 AI 的最终 Prompt\n  • AI 原始响应\n  • 兜底触发情况");
            }
            else
            {
                AppendStatus("调试模式已关闭");
            }
        }

        private void BtnManualTool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new ManualToolDialog();
                if (dlg.ShowDialog() == true)
                {
                    AppendMessage("assistant",
                        "🛠 手动工具执行结果\n\n" +
                        "工具: " + dlg.ResultToolName + "\n" +
                        "参数: " + dlg.ResultArgsJson + "\n\n" +
                        "返回 JSON:\n" +
                        "──────────────────\n" +
                        dlg.ResultJson + "\n" +
                        "──────────────────");
                }
            }
            catch (Exception ex)
            {
                AppendError("手动调用工具失败: " + ex.Message);
            }
        }

        // ==================== 记忆链 按钮事件 ====================

        private void BtnRecordWorkflow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new MemoryManagementDialog(_memory, 0);
                dlg.ShowDialog();
                RefreshDocStatus();
                AppendDebug("MEMORY", "用户打开了记忆管理窗口（工作流记录 Tab）");
            }
            catch (Exception ex)
            {
                AppendError("打开记忆管理失败: " + ex.Message);
            }
        }

        private void BtnImportMemory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new MemoryManagementDialog(_memory, 1);
                dlg.ShowDialog();
                RefreshDocStatus();
                AppendDebug("MEMORY", "用户打开了记忆管理窗口（导入规则/资料 Tab）");
            }
            catch (Exception ex)
            {
                AppendError("打开记忆管理失败: " + ex.Message);
            }
        }

        private void BtnMemoryOverview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new MemoryManagementDialog(_memory, 2);
                dlg.ShowDialog();
                RefreshDocStatus();
                AppendDebug("MEMORY", "用户打开了记忆管理窗口（记忆总览 Tab）");
            }
            catch (Exception ex)
            {
                AppendError("打开记忆管理失败: " + ex.Message);
            }
        }

        // ==================== UI 事件 ====================

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            RefreshDocStatus();
            SendMessage();
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                RefreshDocStatus();
                SendMessage();
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtChat.Document.Blocks.Clear();
            ShowWelcome();
        }

        // ==================== UI 辅助方法 ====================

        private void ShowWelcome()
        {
            AppendMessage("assistant",
                "👋 你好，我是 Revit AI 助手。\n\n" +
                "💡 常用指令示例：\n" +
                "  • 把选中的门全部换成 M2 类型\n" +
                "  • 把所有设备的楼层参数设为对应楼层\n" +
                "  • 批量给选中的图元设置\"系统类型\"=暖通\n\n" +
                "🧠 记忆功能：\n" +
                "  • 记住：帮我记住XX的操作方法\n" +
                "  • 回忆：上次批量设置参数是怎么做的？\n" +
                "  • 同样的操作再来一次\n\n" +
                "🔍 调试校准流程（推荐）：\n" +
                "  1. 勾选顶部「调试模式」\n" +
                "  2. 点「🛠 手动调用工具」先直接执行工具，看到真实 JSON\n" +
                "  3. 确认接口返回正确后，再用自然语言问 AI\n" +
                "  4. 对比 AI 回答 vs 工具原始返回，校准系统提示词/正则\n\n" +
                "🔧 提示：先点\"测试连接\"验证 API 是否可用。");
        }

        private void AppendMessage(string sender, string message)
        {
            try { HistoryLogger.Chat(sender, message); } catch { }
            TxtChat.Dispatcher.Invoke(() =>
            {
                var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };

                var run1 = new Run("[" + (sender == "user" ? "你" : "AI") + "] " + DateTime.Now.ToString("HH:mm") + "\n")
                {
                    FontSize = 11,
                    Foreground = sender == "user" ? Brushes.Gray : Brushes.DodgerBlue,
                    FontWeight = FontWeights.Bold
                };
                para.Inlines.Add(run1);

                var run2 = new Run(message)
                {
                    FontSize = 14
                };
                para.Inlines.Add(run2);

                TxtChat.Document.Blocks.Add(para);
                TxtChat.ScrollToEnd();
            });
        }

        private void AppendDebug(string tag, string message)
        {
            try { HistoryLogger.Raw("chat", "DEBUG_" + tag, message); } catch { }
            if (!_debugMode) return;
            TxtChat.Dispatcher.Invoke(() =>
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(4, 3, 4, 3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Khaki,
                    Background = Brushes.LightYellow,
                    Padding = new Thickness(6, 4, 6, 4)
                };

                var run1 = new Run("[调试 " + tag + "] " + DateTime.Now.ToString("HH:mm:ss") + "\n")
                {
                    FontSize = 10,
                    Foreground = Brushes.DarkOliveGreen,
                    FontWeight = FontWeights.Bold
                };
                para.Inlines.Add(run1);

                var run2 = new Run(message)
                {
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = Brushes.Black
                };
                para.Inlines.Add(run2);

                TxtChat.Document.Blocks.Add(para);
                TxtChat.ScrollToEnd();
            });
        }

        private void AppendError(string message)
        {
            try { HistoryLogger.Error("AppendError(" + message?.Length + "chars)", new Exception(message ?? "(no inner)"), message); } catch { }
            TxtChat.Dispatcher.Invoke(() =>
            {
                var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };

                var run1 = new Run("[错误] " + DateTime.Now.ToString("HH:mm") + "\n")
                {
                    FontSize = 11,
                    Foreground = Brushes.OrangeRed,
                    FontWeight = FontWeights.Bold
                };
                para.Inlines.Add(run1);

                var run2 = new Run(message)
                {
                    FontSize = 14,
                    Foreground = Brushes.DarkRed
                };
                para.Inlines.Add(run2);

                TxtChat.Document.Blocks.Add(para);
                TxtChat.ScrollToEnd();
            });
        }

        private void AppendStatus(string message)
        {
            TxtHint.Text = message;
        }

        private void RefreshDocStatus()
        {
            try
            {
                TrySetUIApp();
                var uidoc = AIAssistantState.CurrentUIDoc;
                if (uidoc == null || uidoc.Document == null)
                {
                    TxtDocStatus.Text = "📄 当前文档：未加载 (CurrentUIDoc=null)";
                    return;
                }
                var doc = uidoc.Document;
                string name = doc.Title ?? "(无标题)";
                string path = doc.PathName ?? "(未保存)";

                // 图元总数：多级 fallback，异常时显示 ⚠️统计失败
                int total = -1;
                try
                {
                    total = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().Count;
                }
                catch (Exception ex1)
                {
                    AppendDebug("DOC-STATS", "WhereElementIsNotElementType 统计失败: " + ex1.Message + "，尝试全量统计...");
                    try
                    {
                        total = new FilteredElementCollector(doc).ToElements().Count;
                    }
                    catch (Exception ex2)
                    {
                        AppendDebug("DOC-STATS", "全量统计也失败: " + ex2.Message);
                        total = -1;
                    }
                }

                int selCount = 0;
                try { selCount = uidoc.Selection?.GetElementIds()?.Count ?? 0; }
                catch (Exception exS) { AppendDebug("DOC-STATS", "选中统计失败: " + exS.Message); }

                string totalStr = total < 0 ? "⚠️统计失败" : total.ToString();
                TxtDocStatus.Text =
                    "📄 当前文档：" + name +
                    "  |  选中: " + selCount +
                    "  |  图元总数: " + totalStr +
                    "  |  路径: " + path;
            }
            catch (Exception ex)
            {
                TxtDocStatus.Text = "📄 当前文档：异常 - " + ex.Message;
            }
        }

        private void SetProcessing(bool processing)
        {
            _isProcessing = processing;
            BtnSend.IsEnabled = !processing;
            TxtInput.IsEnabled = !processing;
            RefreshDocStatus();
        }

        // ==================== 辅助 ====================

        private bool ContainsMemoryKeyword(string text)
        {
            string[] keywords = { "记得", "之前", "上次", "同样", "类似", "再次", "记忆" };
            foreach (var k in keywords)
            {
                if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private string GetToolName(Dictionary<string, object> toolCall)
        {
            try
            {
                var function = toolCall["function"] as Dictionary<string, object>;
                return function["name"] as string;
            }
            catch { return "unknown"; }
        }

        private string GetToolCallId(Dictionary<string, object> toolCall)
        {
            try { return toolCall["id"] as string; }
            catch { return "call_001"; }
        }

        private Dictionary<string, object> GetToolArgs(Dictionary<string, object> toolCall)
        {
            try
            {
                var function = toolCall["function"] as Dictionary<string, object>;
                string argsJson = function["arguments"] as string;
                return SimpleJsonParse(argsJson);
            }
            catch { return new Dictionary<string, object>(); }
        }

        private Dictionary<string, object> SimpleJsonParse(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json)) return result;
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            foreach (var pair in json.Split(','))
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
    }
}
