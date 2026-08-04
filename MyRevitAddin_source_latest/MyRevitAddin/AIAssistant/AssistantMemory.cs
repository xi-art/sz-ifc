using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// 记忆类型
    /// </summary>
    public enum MemoryType
    {
        Conversation,   // 普通对话
        Operation,      // AI 调用工具的成功操作
        UserPreference, // 用户偏好（说话风格、常用参数等）
        Workflow,       // 工作流：用户一次工作的完整过程+步骤+经验教训
        KnowledgeRule,  // 业务规则：公司规范、项目要求、命名规则、外部资料导入
        CmdArchive      // 命令档案：从本地 C# 命令自动提取的"有哪些现成功能可用"清单
    }

    /// <summary>
    /// 单条记忆
    /// </summary>
    public class MemoryEntry
    {
        public DateTime Timestamp { get; set; }
        public MemoryType Type { get; set; }
        public string UserInput { get; set; }
        public string ToolName { get; set; }
        public string ToolArgs { get; set; }
        public string Result { get; set; }
        public string AiResponse { get; set; }

        public string ToJson()
        {
            return string.Format(
                "{{\"timestamp\":\"{0}\",\"type\":\"{1}\",\"user\":\"{2}\",\"tool\":\"{3}\",\"args\":\"{4}\",\"result\":\"{5}\",\"ai\":\"{6}\"}}",
                Timestamp.ToString("o"), Type, Escape(UserInput),
                ToolName, Escape(ToolArgs), Escape(Result), Escape(AiResponse));
        }

        public static MemoryEntry FromJson(string json)
        {
            // 简化解析（避免依赖 Newtonsoft.Json）
            try
            {
                var entry = new MemoryEntry();
                // 时间戳解析
                string tsRaw = ExtractJsonString(json, "timestamp");
                DateTime ts;
                if (DateTime.TryParse(tsRaw, out ts)) entry.Timestamp = ts;
                else entry.Timestamp = DateTime.Now;
                // 类型解析（兼容旧数据：缺失时默认 Conversation）
                string typeStr = ExtractJsonString(json, "type");
                try
                {
                    if (!string.IsNullOrEmpty(typeStr))
                        entry.Type = (MemoryType)Enum.Parse(typeof(MemoryType), typeStr);
                    else
                        entry.Type = MemoryType.Conversation;
                }
                catch { entry.Type = MemoryType.Conversation; }

                entry.UserInput = ExtractJsonString(json, "user");
                entry.ToolName = ExtractJsonString(json, "tool");
                entry.ToolArgs = ExtractJsonString(json, "args");
                entry.Result = ExtractJsonString(json, "result");
                entry.AiResponse = ExtractJsonString(json, "ai");
                return entry;
            }
            catch { return null; }
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static string ExtractJsonString(string json, string key)
        {
            int idx = json.IndexOf("\"" + key + "\":\"");
            if (idx < 0) return "";
            int start = idx + key.Length + 4;
            int end = start;
            while (end < json.Length)
            {
                if (json[end] == '\\' && end + 1 < json.Length) { end += 2; continue; }
                if (json[end] == '"') break;
                end++;
            }
            string raw = json.Substring(start, end - start);
            return raw.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    /// <summary>
    /// 本地记忆存储：按日期/类型保存到 %APPDATA%\MyRevitAddin\Memory\
    /// 支持关键词相似度匹配（历史指令优先级）
    /// </summary>
    public class AssistantMemory
    {
        private readonly string _memoryDir;
        private const int MAX_RECENT_OPERATIONS = 5;
        private const int MAX_RECENT_CONVERSATIONS = 10;
        private const int SIMILARITY_THRESHOLD = 30; // 相似度阈值（%）

        public AssistantMemory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _memoryDir = Path.Combine(appData, "MyRevitAddin", "Memory");
            if (!Directory.Exists(_memoryDir))
                Directory.CreateDirectory(_memoryDir);
        }

        public string MemoryDir { get { return _memoryDir; } }

        // ==================== 保存 ====================

        public void SaveOperation(string toolName, Dictionary<string, object> args, string result)
        {
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = MemoryType.Operation,
                ToolName = toolName,
                ToolArgs = SerializeArgs(args),
                Result = result
            };
            AppendToFile(GetTodayFile(), entry);
        }

        public void SaveConversation(string userInput, string aiResponse, string toolName = "")
        {
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = MemoryType.Conversation,
                UserInput = userInput,
                AiResponse = aiResponse,
                ToolName = toolName
            };
            AppendToFile(GetTodayFile(), entry);
        }

        public void SaveUserPreference(string preference)
        {
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = MemoryType.UserPreference,
                UserInput = preference
            };
            AppendToFile(GetTodayFile(), entry);
        }

        // ===== 新增：工作流记忆 =====
        public void SaveWorkflow(string title, string steps, string lessonsLearned = "", string projectContext = "")
        {
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = MemoryType.Workflow,
                ToolName = "workflow",
                UserInput = "[标题] " + title + (!string.IsNullOrEmpty(projectContext) ? "  [项目/上下文] " + projectContext : ""),
                AiResponse = "[步骤] " + steps,
                Result = !string.IsNullOrEmpty(lessonsLearned) ? "[经验教训/注意事项] " + lessonsLearned : ""
            };
            AppendToFile(GetTodayFile(), entry);
            AppendToFile(GetBootstrapFile("workflows"), entry);
        }

        // ===== 新增：业务规则/外部资料记忆 =====
        public void SaveKnowledgeRule(string category, string title, string content, string source = "人工录入")
        {
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = MemoryType.KnowledgeRule,
                ToolName = "rule",
                ToolArgs = "[分类] " + category + "  [来源] " + source,
                UserInput = "[规则标题] " + title,
                AiResponse = "[规则内容] " + content
            };
            AppendToFile(GetTodayFile(), entry);
            AppendToFile(GetBootstrapFile("rules"), entry);
        }

        // ===== 新增：命令档案（从现有 C# 命令自动提取） =====
        public void SaveCmdArchive(string commandClassName, string shortDescription, string detailedSteps,
                                   string keywords = "", string codeLocation = "")
        {
            // 命令档案是长期知识，直接写 bootstrap 档案，也写当天
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = MemoryType.CmdArchive,
                ToolName = "cmd",
                ToolArgs = "[关键词] " + keywords + "  [代码位置] " + codeLocation,
                UserInput = "[命令名] " + commandClassName + "  [一句话描述] " + shortDescription,
                AiResponse = "[详细功能与步骤] " + detailedSteps
            };
            AppendToFile(GetTodayFile(), entry);
            AppendToFile(GetBootstrapFile("cmd_archive"), entry);
        }

        // ===== 新增：删除指定记忆 =====
        public bool DeleteMemory(Predicate<MemoryEntry> predicate)
        {
            bool anyDeleted = false;
            if (!Directory.Exists(_memoryDir)) return false;
            foreach (var file in Directory.GetFiles(_memoryDir, "*.mem"))
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    var kept = new List<string>();
                    bool changed = false;
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) { kept.Add(line); continue; }
                        var entry = MemoryEntry.FromJson(line);
                        if (entry == null || predicate(entry))
                        {
                            changed = true;
                            anyDeleted = true;
                            continue;
                        }
                        kept.Add(line);
                    }
                    if (changed) File.WriteAllLines(file, kept);
                }
                catch { }
            }
            return anyDeleted;
        }

        // ===== 新增：导出所有记忆为 JSON 字符串（备份） =====
        public string ExportAllMemoryAsJsonLines()
        {
            var sb = new StringBuilder();
            var all = LoadAllMemory();
            foreach (var entry in all)
                sb.AppendLine(entry.ToJson());
            return sb.ToString();
        }

        // ===== 新增：从 JSON 行字符串导入记忆（恢复） =====
        public int ImportMemoryFromJsonLines(string jsonLines)
        {
            int count = 0;
            if (string.IsNullOrWhiteSpace(jsonLines)) return 0;
            foreach (var rawLine in jsonLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = MemoryEntry.FromJson(line);
                if (entry != null)
                {
                    AppendToFile(GetTodayFile(), entry);
                    count++;
                }
            }
            return count;
        }

        private void AppendToFile(string filePath, MemoryEntry entry)
        {
            try
            {
                File.AppendAllText(filePath, entry.ToJson() + Environment.NewLine);
            }
            catch { /* 写入失败不影响主流程 */ }
        }

        private string GetTodayFile()
        {
            return Path.Combine(_memoryDir, DateTime.Now.ToString("yyyy-MM-dd") + ".mem");
        }

        private string GetBootstrapFile(string kind)
        {
            // 长期记忆：不按日期切分，一直保留
            return Path.Combine(_memoryDir, "_bootstrap_" + kind + ".mem");
        }

        private string SerializeArgs(Dictionary<string, object> args)
        {
            if (args == null || args.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kvp in args)
            {
                parts.Add(kvp.Key + "=" + kvp.Value);
            }
            return string.Join("; ", parts);
        }

        // ==================== 检索 ====================

        /// <summary>
        /// 搜索记忆：找与用户输入相似的历史操作
        /// 优先返回工具操作（因为这才是可复用的）
        /// </summary>
        public List<MemoryEntry> SearchMemory(string userInput, int maxResults = 5)
        {
            var all = LoadAllMemory();
            if (all.Count == 0 || string.IsNullOrEmpty(userInput)) return new List<MemoryEntry>();

            var scored = new List<Tuple<int, MemoryEntry>>();

            foreach (var entry in all)
            {
                int score = CalculateSimilarity(userInput, entry);
                if (score >= SIMILARITY_THRESHOLD)
                    scored.Add(Tuple.Create(score, entry));
            }

            // 评分降序、操作类型优先
            return scored
                .OrderByDescending(t => t.Item2.Type == MemoryType.Operation ? t.Item1 * 2 : t.Item1)
                .ThenByDescending(t => t.Item2.Timestamp)
                .Take(maxResults)
                .Select(t => t.Item2)
                .ToList();
        }

        /// <summary>
        /// 加载最近 7 天的所有记忆
        /// </summary>
        public List<MemoryEntry> LoadRecentMemory(int days = 7)
        {
            return LoadAllMemory().Where(e => e.Timestamp > DateTime.Now.AddDays(-days)).ToList();
        }

        /// <summary>
        /// 加载所有记忆文件
        /// </summary>
        public List<MemoryEntry> LoadAllMemory()
        {
            var result = new List<MemoryEntry>();
            if (!Directory.Exists(_memoryDir)) return result;

            foreach (var file in Directory.GetFiles(_memoryDir, "*.mem"))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var entry = MemoryEntry.FromJson(line);
                        if (entry != null) result.Add(entry);
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// 生成记忆摘要（注入到系统提示词）
        /// </summary>
        public string GetMemorySummary()
        {
            var all = LoadAllMemory();
            if (all.Count == 0)
                return "（暂无历史记忆）";

            var sb = new StringBuilder();

            // 最近操作（最关键）
            var operations = all.Where(e => e.Type == MemoryType.Operation)
                .OrderByDescending(e => e.Timestamp)
                .Take(MAX_RECENT_OPERATIONS)
                .ToList();

            if (operations.Count > 0)
            {
                sb.AppendLine("[最近成功的操作]");
                foreach (var op in operations)
                {
                    sb.AppendLine(string.Format(
                        "- {0:MM-dd HH:mm} 工具:{1} 参数:{2} 结果:{3}",
                        op.Timestamp, op.ToolName, Truncate(op.ToolArgs, 50), Truncate(op.Result, 50)));
                }
                sb.AppendLine();
            }

            // 用户偏好
            var prefs = all.Where(e => e.Type == MemoryType.UserPreference)
                .OrderByDescending(e => e.Timestamp)
                .Take(3)
                .ToList();

            if (prefs.Count > 0)
            {
                sb.AppendLine("[用户偏好]");
                foreach (var p in prefs)
                    sb.AppendLine("- " + Truncate(p.UserInput, 80));
                sb.AppendLine();
            }

            // ==== 新增注入：命令档案（全局功能知识，AI 最需要知道"有什么现成命令可用"）====
            var cmdArchives = all.Where(e => e.Type == MemoryType.CmdArchive)
                .OrderBy(e => e.UserInput)
                .Take(20)
                .ToList();
            if (cmdArchives.Count > 0)
            {
                sb.AppendLine("[本地可用的 Revit 命令档案] (已通过代码自动提取，推荐优先使用)");
                foreach (var cmd in cmdArchives)
                {
                    string line = "• " + Truncate((cmd.UserInput ?? "").Replace("\n", " "), 100);
                    if (!string.IsNullOrEmpty(cmd.AiResponse))
                        line += "  功能: " + Truncate(cmd.AiResponse.Replace("\n", " → "), 120);
                    if (!string.IsNullOrEmpty(cmd.ToolArgs))
                        line += "  关键词: " + Truncate(cmd.ToolArgs.Replace("\n", " "), 50);
                    sb.AppendLine(line);
                }
                sb.AppendLine();
            }

            // ==== 新增注入：业务规则/公司规范（项目级约束，AI 回答必须遵守）====
            var rules = all.Where(e => e.Type == MemoryType.KnowledgeRule)
                .OrderByDescending(e => e.Timestamp)
                .Take(10)
                .ToList();
            if (rules.Count > 0)
            {
                sb.AppendLine("[业务规则 / 项目规范 / 命名要求]（AI 回答必须严格遵守）");
                foreach (var r in rules)
                {
                    string line = "• " + Truncate((r.UserInput ?? "").Replace("\n", " "), 100);
                    if (!string.IsNullOrEmpty(r.ToolArgs))
                        line += "  (" + Truncate(r.ToolArgs.Replace("\n", " "), 40) + ")";
                    if (!string.IsNullOrEmpty(r.AiResponse))
                        line += "  内容: " + Truncate(r.AiResponse.Replace("\n", " → "), 160);
                    sb.AppendLine(line);
                }
                sb.AppendLine();
            }

            // ==== 新增注入：最近工作流（用户之前做过的完整流程）====
            var workflows = all.Where(e => e.Type == MemoryType.Workflow)
                .OrderByDescending(e => e.Timestamp)
                .Take(5)
                .ToList();
            if (workflows.Count > 0)
            {
                sb.AppendLine("[最近完成的工作流 / 经验沉淀]（优先参考用户之前的操作路径）");
                foreach (var wf in workflows)
                {
                    string line = "- " + wf.Timestamp.ToString("MM-dd HH:mm") + "  " +
                                  Truncate((wf.UserInput ?? "").Replace("\n", " "), 100);
                    if (!string.IsNullOrEmpty(wf.AiResponse))
                        line += "  步骤: " + Truncate(wf.AiResponse.Replace("\n", " → "), 180);
                    if (!string.IsNullOrEmpty(wf.Result))
                        line += "  经验: " + Truncate(wf.Result.Replace("\n", " → "), 100);
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 找相似操作（用于"之前是怎么做的"这类指令）
        /// </summary>
        public MemoryEntry FindSimilarOperation(string userInput)
        {
            var results = SearchMemory(userInput, 1);
            return results.FirstOrDefault(e => e.Type == MemoryType.Operation);
        }

        /// <summary>
        /// 搜索记忆并格式化为可读字符串（供 AI 工具调用返回）
        /// </summary>
        public string SearchMemoryAsString(string keyword, int maxResults = 5)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return "";

            var results = SearchMemory(keyword, maxResults);
            if (results == null || results.Count == 0) return "";

            var sb = new StringBuilder();
            int idx = 1;
            foreach (var entry in results)
            {
                sb.AppendFormat("[{0}] {1:yyyy-MM-dd HH:mm} 类型:{2}\n",
                    idx++, entry.Timestamp, entry.Type);

                if (!string.IsNullOrEmpty(entry.UserInput))
                    sb.AppendFormat("    用户输入: {0}\n", Truncate(entry.UserInput, 200));

                if (!string.IsNullOrEmpty(entry.ToolName))
                    sb.AppendFormat("    工具: {0}\n", entry.ToolName);

                if (!string.IsNullOrEmpty(entry.ToolArgs))
                    sb.AppendFormat("    参数: {0}\n", Truncate(entry.ToolArgs, 200));

                if (!string.IsNullOrEmpty(entry.Result))
                    sb.AppendFormat("    结果: {0}\n", Truncate(entry.Result, 200));

                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ==================== 相似度算法 ====================

        private int CalculateSimilarity(string userInput, MemoryEntry entry)
        {
            // 提取要比较的文本
            string entryText = (entry.UserInput ?? "") + " " + (entry.ToolName ?? "") + " " + (entry.ToolArgs ?? "");
            if (string.IsNullOrWhiteSpace(entryText)) return 0;

            string userLower = userInput.ToLower();
            string entryLower = entryText.ToLower();

            int score = 0;

            // 1. 关键词匹配（中文按 2-gram 拆分，英文按空格）
            var userTokens = Tokenize(userInput);
            var entryTokens = Tokenize(entryText);

            int matched = userTokens.Intersect(entryTokens).Count();
            if (userTokens.Count > 0)
                score += (int)(60.0 * matched / userTokens.Count);

            // 2. 包含匹配
            if (entryLower.Contains(userLower) || userLower.Contains(entryLower))
                score += 20;

            // 3. 关键词加权（建筑/Revit 常用动词）
            string[] weighted = { "批量", "替换", "设置", "参数", "楼层", "族", "类型",
                                  "视图", "样板", "图纸", "复制", "添加", "过滤器", "标记", "避障" };
            foreach (var w in weighted)
            {
                if (userLower.Contains(w) && entryLower.Contains(w))
                    score += 5;
            }

            // 4. 时间衰减（7 天内 +10，更早 -5）
            var ageDays = (DateTime.Now - entry.Timestamp).TotalDays;
            if (ageDays < 1) score += 15;
            else if (ageDays < 7) score += 10;
            else if (ageDays > 30) score -= 10;

            return Math.Min(score, 100);
        }

        private HashSet<string> Tokenize(string text)
        {
            var tokens = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return tokens;

            text = text.ToLower();
            // 2-gram 拆分（中文友好）
            for (int i = 0; i < text.Length - 1; i++)
            {
                string two = text.Substring(i, 2);
                if (!char.IsPunctuation(two[0]) && !char.IsPunctuation(two[1]))
                    tokens.Add(two);
            }
            // 整词
            foreach (var word in text.Split(new[] { ' ', ',', '.', ';', '，', '。', '；' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length >= 2) tokens.Add(word);
            }
            return tokens;
        }

        private string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }
    }
}
