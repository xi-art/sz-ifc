using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// 千问（Qwen）/ OpenAI 兼容 API 客户端
    /// 支持 Function Calling / Tool Use
    /// </summary>
    public class QwenApiClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _endpoint;
        private readonly HttpClient _httpClient;
        private readonly JavaScriptSerializer _json;

        // 千问 OpenAI 兼容端点
        public const string DEFAULT_DASHSCOPE_ENDPOINT =
            "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";

        public const string DEFAULT_OPENAI_ENDPOINT =
            "https://api.openai.com/v1/chat/completions";

        public QwenApiClient(string apiKey, string model = "qwen-turbo",
            string endpoint = null)
        {
            _apiKey = apiKey ?? "";
            _model = model ?? "qwen-turbo";
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DEFAULT_DASHSCOPE_ENDPOINT : endpoint.Trim();
            _httpClient = new HttpClient();
            string authPrefix = GetAuthPrefixForEndpoint(_endpoint);
            _httpClient.DefaultRequestHeaders.Add("Authorization", authPrefix + " " + _apiKey);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MyRevitAddin-AIAssistant/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        private static string GetAuthPrefixForEndpoint(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint)) return "Bearer";
            // DashScope 原生端点同时支持 Bearer 和 APIKey，MAAS 兼容端点一般用 Bearer
            // 默认 Bearer，兼容度最高
            return "Bearer";
        }

        private static bool IsDashScopeDefaultEndpoint(string endpoint)
        {
            return !string.IsNullOrEmpty(endpoint) &&
                endpoint.IndexOf("dashscope.aliyuncs.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsModelStudioEndpoint(string endpoint)
        {
            return !string.IsNullOrEmpty(endpoint) &&
                (endpoint.IndexOf("maas.aliyuncs.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 endpoint.IndexOf("bailian.aliyun.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 endpoint.IndexOf("dsw-", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ValidateKeyFormat(string apiKey, string endpoint)
        {
            List<string> hints = new List<string>();
            if (string.IsNullOrEmpty(apiKey))
            {
                return "❌ API Key 为空，请在右上设置里填入 Key。";
            }
            // 带点号的签名 key → 临时签名 token（sk-ws-H.RYYYDEE.QA70.MExxx...）
            bool hasDots = (apiKey ?? "").Contains(".");
            bool hasDashWs = (apiKey ?? "").StartsWith("sk-ws-", StringComparison.OrdinalIgnoreCase);
            bool hasDashOnly = (apiKey ?? "").StartsWith("sk-") && !hasDashWs;

            if (hasDots)
            {
                hints.Add("⚠️ 该 Key 包含多个点号分隔的段（格式像临时 STS 签名 Token），\n" +
                          "   这类 Key 一般只有 1 小时有效期，过期后必须重新生成。\n" +
                          "   建议到控制台重新生成【长期有效的普通 API Key】（不含点号）。");
            }
            if (IsDashScopeDefaultEndpoint(endpoint))
            {
                if (hasDashWs && !hasDashOnly)
                {
                    hints.Add("⚠️ 端点是 DashScope (dashscope.aliyuncs.com)，但 Key 是 sk-ws- 开头（MAAS/百炼格式）。\n" +
                              "   请确认：① DashScope 控制台 Key 应为 sk- 开头无 ws；② 或改用 MAAS 兼容端点 (*.maas.aliyuncs.com)。");
                }
                if (!apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
                {
                    hints.Add("❌ DashScope Key 必须以 sk- 开头。");
                }
            }
            if (IsModelStudioEndpoint(endpoint))
            {
                if (!apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
                {
                    hints.Add("❌ 阿里云百炼/MAAS Key 必须以 sk-（或 sk-ws- 工作空间）开头。");
                }
                hints.Add("💡 MAAS/百炼 控制台建议：\n" +
                          "   - 确认该工作空间下已开通所调用模型（deepseek/qwen 等）；\n" +
                          "   - 访问 https://bailian.console.aliyun.com 重新生成 API Key；\n" +
                          "   - 子域名（llm-xxxx.cn-beijing.maas.aliyuncs.com）属于具体工作空间，\n" +
                          "     Key 和模型必须属于同一工作空间。");
            }
            if (hints.Count == 0)
            {
                if (apiKey.Length < 24)
                    hints.Add("⚠️ Key 长度过短（仅 " + apiKey.Length + " 字符），格式存疑。");
            }
            return string.Join("\n", hints);
        }

        /// <summary>
        /// 测试连接：发送最小请求验证 API Key 是否有效
        /// </summary>
        public async Task<string> TestConnection()
        {
            var messages = new List<Dictionary<string, object>>
            {
                MakeMessage("user", "ping")
            };

            var request = new Dictionary<string, object>
            {
                { "model", _model },
                { "messages", messages },
                { "max_tokens", 10 }
            };

            string responseJson = await SendRequestAsync(request);

            if (string.IsNullOrWhiteSpace(responseJson))
                throw new Exception("服务器返回空响应（网络超时或被墙）");

            Dictionary<string, object> resp;
            try
            {
                resp = _json.Deserialize<Dictionary<string, object>>(responseJson);
            }
            catch
            {
                throw new Exception("JSON 解析失败，响应内容：\n" + responseJson.Substring(0, Math.Min(responseJson.Length, 500)));
            }

            if (resp.ContainsKey("error"))
            {
                var err = resp["error"] as Dictionary<string, object>;
                throw new Exception("API 错误: " + (err.ContainsKey("message") ? err["message"] : responseJson));
            }

            string content = GetContent(resp);
            if (string.IsNullOrEmpty(content))
            {
                // 返回了 choices 但 content 为空，通常是模型不存在或权限不足
                throw new Exception("响应内容为空（Key 可能无权访问模型 " + _model + "）\n" +
                    "可能原因：\n" +
                    "1. Key 格式不对（通义千问 DashScope 应为32位十六进制，如 abc123...def456）\n" +
                    "2. 该 Key 未开通 " + _model + " 模型\n" +
                    "3. 请到 https://dashscope.console.aliyun.com 获取有效 API Key");
            }

            return "模型: " + _model + " · 响应: " + content;
        }

        /// <summary>
        /// 发送对话请求（带工具定义 + 历史上下文）
        /// </summary>
        /// <param name="userMessage">本轮用户输入</param>
        /// <param name="tools">工具定义</param>
        /// <param name="systemPrompt">系统提示词</param>
        /// <param name="previousMessages">
        /// 历史对话消息数组（不含本轮 user，不含 system），
        /// 每行为 { "role": "user|assistant", "content": "..." }，按时间顺序排列。
        /// 用来让 AI 知道前几轮说了什么，解决短期失忆。
        /// </param>
        public async Task<Dictionary<string, object>> ChatAsync(
            string userMessage,
            List<Dictionary<string, object>> tools,
            string systemPrompt = "",
            List<Dictionary<string, object>> previousMessages = null)
        {
            var messages = new List<Dictionary<string, object>>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(MakeMessage("system", systemPrompt));

            // 1. 先插入历史对话（最近几轮 user/assistant 交替）
            if (previousMessages != null)
            {
                foreach (var m in previousMessages)
                {
                    try
                    {
                        var msg = new Dictionary<string, object>
                        {
                            { "role", m.ContainsKey("role") ? m["role"].ToString() : "user" },
                            { "content", m.ContainsKey("content") ? (m["content"] ?? "").ToString() : "" }
                        };
                        // tool_calls 透传（如果历史里有 AI 调用工具）
                        if (m.ContainsKey("tool_calls") && m["tool_calls"] != null)
                            msg["tool_calls"] = m["tool_calls"];
                        messages.Add(msg);
                    }
                    catch { /* 单条历史格式错，跳过不影响主流程 */ }
                }
            }

            // 2. 最后加本轮用户输入
            messages.Add(MakeMessage("user", userMessage));

            var request = new Dictionary<string, object>
            {
                { "model", _model },
                { "messages", messages },
                { "tools", tools },
                { "tool_choice", "auto" },
                { "temperature", 0.1 }
            };

            string responseJson = await SendRequestAsync(request);
            return _json.Deserialize<Dictionary<string, object>>(responseJson);
        }

        /// <summary>
        /// 发送工具执行结果，继续对话（带历史上下文）
        /// </summary>
        public async Task<Dictionary<string, object>> ChatWithToolResultAsync(
            string userMessage,
            List<Dictionary<string, object>> toolCalls,
            List<Dictionary<string, object>> toolResults,
            List<Dictionary<string, object>> tools,
            string systemPrompt = "",
            List<Dictionary<string, object>> previousMessages = null)
        {
            var messages = new List<Dictionary<string, object>>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(MakeMessage("system", systemPrompt));

            // 1. 历史对话
            if (previousMessages != null)
            {
                foreach (var m in previousMessages)
                {
                    try
                    {
                        var msg = new Dictionary<string, object>
                        {
                            { "role", m.ContainsKey("role") ? m["role"].ToString() : "user" },
                            { "content", m.ContainsKey("content") ? (m["content"] ?? "").ToString() : "" }
                        };
                        if (m.ContainsKey("tool_calls") && m["tool_calls"] != null)
                            msg["tool_calls"] = m["tool_calls"];
                        messages.Add(msg);
                    }
                    catch { }
                }
            }

            // 2. 本轮原始 user（包含前置 autoContext）
            messages.Add(MakeMessage("user", userMessage));

            // 3. AI 调用工具的消息块
            messages.Add(new Dictionary<string, object>
            {
                { "role", "assistant" },
                { "content", "" },
                { "tool_calls", toolCalls }
            });

            // 4. 工具执行结果
            foreach (var result in toolResults)
            {
                messages.Add(new Dictionary<string, object>
                {
                    { "role", "tool" },
                    { "tool_call_id", result["tool_call_id"] },
                    { "content", result["content"] }
                });
            }

            var request = new Dictionary<string, object>
            {
                { "model", _model },
                { "messages", messages },
                { "tools", tools },
                { "tool_choice", "auto" },
                { "temperature", 0.1 }
            };

            string responseJson = await SendRequestAsync(request);
            return _json.Deserialize<Dictionary<string, object>>(responseJson);
        }

        private async Task<string> SendRequestAsync(Dictionary<string, object> request)
        {
            string jsonBody = _json.Serialize(request);

            string reqStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string keyShort = (_apiKey ?? "").Length > 12 ? _apiKey.Substring(0, 12) + "..." : (_apiKey ?? "(空)");

            // === 1) 数据目录历史日志 (data/tools) ===
            try
            {
                string summary =
                    "Endpoint=" + _endpoint +
                    " | Model=" + _model +
                    " | Key=" + keyShort +
                    " | body: " + (jsonBody.Length > 1500 ? jsonBody.Substring(0, 1500) + "..." : jsonBody);
                HistoryLogger.Tool("API-REQUEST", argsJson: summary, resultJson: "(等待响应)", source: "AIClient");
            }
            catch { }
            try
            {
                HistoryLogger.Raw(
                    category: "tools",
                    fileNameSuffix: "req_" + reqStamp + ".json",
                    content:
                        "Endpoint: " + _endpoint + "\r\n" +
                        "Model: " + _model + "\r\n" +
                        "Key prefix: " + keyShort + "\r\n" +
                        "==== REQUEST BODY ====\r\n" + jsonBody + "\r\n");
            }
            catch { }

            // === 2) AppData 桌面日志（保留原做法，兼容习惯） ===
            try
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MyRevitAddin", "Logs");
                System.IO.Directory.CreateDirectory(logDir);
                string logFile = System.IO.Path.Combine(logDir, "ai_request_" + reqStamp + ".log");
                System.IO.File.WriteAllText(logFile,
                    "Endpoint: " + _endpoint + "\n" +
                    "Model: " + _model + "\n" +
                    "Key prefix: " + keyShort + "\n" +
                    "==== REQUEST BODY ====\n" + jsonBody + "\n");
            }
            catch { }

            // === 3) 依次尝试 Authorization 方案（DashScope 既支持 Bearer 也支持 APIKey）
            string[] authSchemes = IsDashScopeDefaultEndpoint(_endpoint)
                ? new[] { "Bearer", "APIKey" }
                : new[] { "Bearer" };

            string responseJson = null;
            HttpResponseMessage lastResponse = null;
            List<string> triedSchemes = new List<string>();
            Exception lastEx = null;

            foreach (string scheme in authSchemes)
            {
                triedSchemes.Add(scheme);
                try
                {
                    // 重写 Authorization 头（先清再设）
                    try
                    {
                        if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
                            _httpClient.DefaultRequestHeaders.Remove("Authorization");
                        _httpClient.DefaultRequestHeaders.Add("Authorization", scheme + " " + _apiKey);
                    }
                    catch { }

                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(_endpoint, content);
                    lastResponse = response;
                    responseJson = await response.Content.ReadAsStringAsync();

                    if ((int)response.StatusCode == 401)
                    {
                        // 401 再试下一个 scheme
                        continue;
                    }

                    // 非 401：如果成功或者别的错误直接返回/抛出
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            // === 4) 响应写 data/tools 日志 ===
            try
            {
                int code = lastResponse != null ? (int)lastResponse.StatusCode : 0;
                string bodyShort = (responseJson ?? "").Length > 1500
                    ? (responseJson ?? "").Substring(0, 1500) + "..."
                    : (responseJson ?? "");
                string argsLine =
                    "Endpoint=" + _endpoint +
                    " | HTTP=" + code +
                    " | AuthTried=" + string.Join(",", triedSchemes);
                HistoryLogger.Tool("API-RESPONSE",
                    argsJson: argsLine,
                    resultJson: bodyShort,
                    source: "AIClient");
            }
            catch { }
            try
            {
                int code = lastResponse != null ? (int)lastResponse.StatusCode : 0;
                HistoryLogger.Raw(
                    category: "tools",
                    fileNameSuffix: "resp_" + reqStamp + ".json",
                    content:
                        "HTTP " + code + " " + (lastResponse?.ReasonPhrase ?? "") + "\r\n" +
                        "Auth schemes tried: " + string.Join(", ", triedSchemes) + "\r\n" +
                        "==== RESPONSE BODY ====\r\n" + responseJson + "\r\n");
            }
            catch { }
            try
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MyRevitAddin", "Logs");
                System.IO.Directory.CreateDirectory(logDir);
                string latestLog = System.IO.Path.Combine(logDir, "last_request.log");
                int statusCode = lastResponse != null ? (int)lastResponse.StatusCode : 0;
                System.IO.File.AppendAllText(latestLog,
                    "==== RESPONSE " + statusCode + " ====\n" +
                    (responseJson ?? "") + "\n\n");
            }
            catch { }

            if (lastResponse == null)
            {
                if (lastEx != null)
                    throw lastEx;
                throw new Exception("请求失败：没有任何 HTTP 响应。\n请检查网络/代理，或在设置里修改端点。");
            }

            if (!lastResponse.IsSuccessStatusCode || string.IsNullOrEmpty(responseJson))
            {
                int statusNum = (int)lastResponse.StatusCode;
                string rawMsg = responseJson ?? "";
                string formatHint = ValidateKeyFormat(_apiKey, _endpoint);

                if (statusNum == 401)
                {
                    // 从响应 JSON 解析具体错误码/消息
                    string errCode = ""; string errMsg = "";
                    try
                    {
                        var err = _json.Deserialize<Dictionary<string, object>>(rawMsg);
                        if (err != null && err.ContainsKey("error") && err["error"] is Dictionary<string, object> em)
                        {
                            if (em.ContainsKey("code")) errCode = em["code"]?.ToString() ?? "";
                            if (em.ContainsKey("message")) errMsg = em["message"]?.ToString() ?? "";
                        }
                    }
                    catch { }

                    string diag =
                        "🚫 HTTP 401 InvalidApiKey / 未授权\n\n" +
                        "服务器说：\n" +
                        "  状态码      : " + statusNum + " " + lastResponse.ReasonPhrase + "\n" +
                        "  错误码      : " + (string.IsNullOrEmpty(errCode) ? "(空)" : errCode) + "\n" +
                        "  错误消息    : " + (string.IsNullOrEmpty(errMsg) ? "(空)" : errMsg) + "\n" +
                        "  端点        : " + _endpoint + "\n" +
                        "  模型        : " + _model + "\n" +
                        "  Key 前缀    : " + keyShort + "\n" +
                        "  鉴权方案尝试: " + string.Join(" → ", triedSchemes) + "\n\n";

                    if (!string.IsNullOrWhiteSpace(formatHint))
                    {
                        diag += "── Key 格式诊断 ──\n" + formatHint + "\n\n";
                    }
                    diag +=
                        "── 推荐处理步骤 ──\n" +
                        "① 【最常见】当前 Key 过期或未生效：到控制台重新生成新 Key，复制后在本面板右上角【设置】→ API Key 输入框里粘贴并回车。\n" +
                        "  DashScope：https://dashscope.console.aliyun.com/apiKey\n" +
                        "  百炼 MAAS：https://bailian.console.aliyun.com\n" +
                        "② 【DashScope 专属】如果 Key 中出现 “. ” 分段：这是临时 STS 签名 Token，一般 1 小时过期→必须重新生成长期 Key（sk-xx，不含点号）。\n" +
                        "③ 【百炼/MAAS】检查当前工作空间：Key 和模型必须属于同一工作空间（llm-xxxx.maas 子域名），并在工作空间下已开通该模型。\n" +
                        "④ 【通用】账号是否欠费、开通所需模型、是否被禁用等。\n" +
                        "⑤ 端点选对了吗？DashScope 端点 → DashScope 的 Key；百炼 MAAS 端点 → MAAS 的 Key。\n\n" +
                        "完整原始响应：\n" + (rawMsg.Length > 2000 ? rawMsg.Substring(0, 2000) + "...[截断]" : rawMsg);

                    try { HistoryLogger.Error("Api401", new Exception(diag)); } catch { }
                    throw new Exception(diag);
                }

                // 非 401 错误
                string keyDiag = string.IsNullOrWhiteSpace(formatHint) ? "" : ("── Key 格式诊断 ──\n" + formatHint + "\n\n");
                throw new Exception(
                    "HTTP " + statusNum + " " + lastResponse.ReasonPhrase + "\n\n" +
                    "端点: " + _endpoint + "\n" +
                    "模型: " + _model + "\n" +
                    "Key 前缀: " + keyShort + "\n" +
                    "尝试鉴权: " + string.Join(", ", triedSchemes) + "\n\n" +
                    keyDiag +
                    "服务器响应: " + rawMsg);
            }

            return responseJson;
        }

        private Dictionary<string, object> MakeMessage(string role, string content)
        {
            return new Dictionary<string, object>
            {
                { "role", role },
                { "content", content }
            };
        }

        public Dictionary<string, object> GetFirstToolCall(Dictionary<string, object> response)
        {
            try
            {
                if (response == null) return null;
                if (!response.ContainsKey("choices")) return null;

                var firstChoice = GetFirstChoice(response);
                if (firstChoice == null) return null;

                if (!firstChoice.ContainsKey("message")) return null;

                var message = firstChoice["message"] as Dictionary<string, object>;
                if (message == null) return null;

                if (message.ContainsKey("tool_calls"))
                {
                    var toolCalls = AsEnumerable(message["tool_calls"]);
                    if (toolCalls != null)
                    {
                        foreach (var tc in toolCalls)
                        {
                            return tc as Dictionary<string, object>;
                        }
                    }
                }
                return null;
            }
            catch { return null; }
        }

        public string GetContent(Dictionary<string, object> response)
        {
            try
            {
                if (response == null) return null;
                if (!response.ContainsKey("choices")) return null;

                var firstChoice = GetFirstChoice(response);
                if (firstChoice == null) return null;

                if (!firstChoice.ContainsKey("message")) return null;

                var message = firstChoice["message"] as Dictionary<string, object>;
                if (message == null) return null;

                return message["content"] as string;
            }
            catch { return null; }
        }

        // JavaScriptSerializer 反序列化 JSON 数组时可能产生 object[]、ArrayList 或 List<object>，
        // 统一通过 IEnumerable 适配，避免 as object[] 失败返回 null
        private static System.Collections.IEnumerable AsEnumerable(object value)
        {
            if (value == null) return null;
            return value as System.Collections.IEnumerable;
        }

        private static Dictionary<string, object> GetFirstChoice(Dictionary<string, object> response)
        {
            var choices = AsEnumerable(response["choices"]);
            if (choices == null) return null;
            foreach (var c in choices)
            {
                return c as Dictionary<string, object>;
            }
            return null;
        }
    }
}
