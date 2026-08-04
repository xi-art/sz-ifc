using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MyRevitAddin.AIAssistant
{
    /// <summary>
    /// 从项目 C# 源代码自动扫描提取命令档案（读取 *Command.cs 的 XML summary 注释）
    /// 写入 AssistantMemory.CmdArchive 类型，让 AI 知道本地有哪些现成功能可用
    /// </summary>
    public static class CmdArchiveExtractor
    {
        /// <summary>
        /// 默认扫描路径：程序所在目录向上回退两级（因为 Revit 加载的 DLL 在 bin\Release，项目根有 .csproj）
        /// 同时也扫描 %APPDATA%\MyRevitAddin\ 下可能存在的源码路径配置
        /// </summary>
        public static List<string> DetectProjectRoots()
        {
            var list = new List<string>();
            try
            {
                // 1. 从 DLL 位置：假设 DLL 在 bin\Release\ 或 deploy\2020\ 下，向上回退找 .csproj
                string asmLoc = typeof(CmdArchiveExtractor).Assembly.Location;
                if (!string.IsNullOrEmpty(asmLoc))
                {
                    string dir = Path.GetDirectoryName(asmLoc);
                    for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
                    {
                        if (Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                        {
                            if (!list.Contains(dir)) list.Add(dir);
                            break;
                        }
                        dir = Path.GetDirectoryName(dir);
                    }
                }

                // 2. AppData 下可能有用户配置的源码路径（memory\\project_paths.txt）
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string cfgFile = Path.Combine(appData, "MyRevitAddin", "Memory", "project_paths.txt");
                if (File.Exists(cfgFile))
                {
                    foreach (var line in File.ReadAllLines(cfgFile))
                    {
                        string p = line.Trim();
                        if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p) && !list.Contains(p))
                            list.Add(p);
                    }
                }

                // 3. 工作目录兜底
                string cwd = Environment.CurrentDirectory;
                if (Directory.Exists(cwd) && Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                    if (!list.Contains(cwd)) list.Add(cwd);
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 扫描指定项目根目录下所有 *Command.cs 文件，提取 XML summary
        /// </summary>
        public static List<CmdExtracted> ScanProject(string projectRoot)
        {
            var result = new List<CmdExtracted>();
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot)) return result;

            foreach (var file in Directory.EnumerateFiles(projectRoot, "*Command.cs", SearchOption.AllDirectories))
            {
                try
                {
                    var cmd = ParseCommandFile(file);
                    if (cmd != null) result.Add(cmd);
                }
                catch { /* 单个文件解析失败跳过 */ }
            }
            return result;
        }

        /// <summary>
        /// 解析单个 *Command.cs 文件，提取类名、summary 注释
        /// </summary>
        public static CmdExtracted ParseCommandFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            string code = File.ReadAllText(filePath, Encoding.UTF8);

            // 1. 找类名：public class XxxCommand : IExternalCommand
            Match classMatch = Regex.Match(code,
                @"public\s+class\s+(?<name>\w+Command)\s*[:{]",
                RegexOptions.Multiline);
            if (!classMatch.Success) return null;
            string className = classMatch.Groups["name"].Value;

            // 2. 找类前的 XML summary：/// <summary> ... </summary>（允许跨多行）
            // 定位 class 定义起始位置之前的 summary
            int classIdx = classMatch.Index;
            string head = code.Substring(0, classIdx);

            string summaryFull = "";
            // 找最后一个 </summary> 之前的内容
            var summaryMatches = Regex.Matches(head,
                @"///\s*<summary>\s*(?<body>.*?)///\s*</summary>",
                RegexOptions.Singleline);
            if (summaryMatches.Count > 0)
            {
                var last = summaryMatches[summaryMatches.Count - 1];
                string body = last.Groups["body"].Value;
                // 去除每行的 /// 前缀，统一换行
                var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var cleanLines = new List<string>();
                foreach (var l in lines)
                {
                    string cl = Regex.Replace(l, @"^\s*///\s*", "").Trim();
                    if (!string.IsNullOrWhiteSpace(cl)) cleanLines.Add(cl);
                }
                summaryFull = string.Join(" ", cleanLines);
            }

            // 3. 从 summary 里再拆分：一句话描述 vs 详细功能
            string shortDesc = "";
            string detailed = summaryFull;
            if (!string.IsNullOrEmpty(summaryFull))
            {
                // 找"功能："或"核心逻辑："作为分界
                int idx = summaryFull.IndexOf("功能：", StringComparison.Ordinal);
                if (idx < 0) idx = summaryFull.IndexOf("核心逻辑：", StringComparison.Ordinal);
                if (idx <= 0)
                {
                    // 没有分标题的话，第一句作为短描述
                    int dotIdx = summaryFull.IndexOfAny(new[] { '。', '.', '\n' });
                    if (dotIdx > 8)
                    {
                        shortDesc = summaryFull.Substring(0, dotIdx).Trim();
                        detailed = summaryFull.Substring(dotIdx).Trim(' ', '。', '.');
                    }
                    else
                    {
                        shortDesc = summaryFull;
                        detailed = "";
                    }
                }
                else
                {
                    shortDesc = summaryFull.Substring(0, idx).Trim();
                    detailed = summaryFull.Substring(idx).Trim();
                }
            }

            // 4. 生成关键词（从类名、短描述里提取建筑/Revit 常用词）
            var kws = new HashSet<string>();
            string blob = className + " " + shortDesc + " " + detailed;
            string[] dict = { "过滤器", "样板", "视图", "图纸", "复制", "批量", "标高", "楼层",
                              "族", "参数", "替换", "导出", "导入", "导入导出", "dwg", "dxf",
                              "标记", "注释", "轴网", "梁", "柱", "墙", "门", "窗", "管道",
                              "风管", "钢筋", "剖面", "立面", "平面", "三维", "明细表", "避让",
                              "避障", "拆分", "合模", "协同", "编号", "命名", "设置" };
            foreach (var k in dict)
                if (blob.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) kws.Add(k);

            string relPath = filePath;
            try
            {
                string root = Path.GetPathRoot(filePath) ?? "";
                // 回退到包含 .csproj 的最近目录作为显示根
                string dir = Path.GetDirectoryName(filePath);
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                    {
                        relPath = filePath.Substring(dir.Length).TrimStart('\\', '/');
                        break;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }

            return new CmdExtracted
            {
                ClassName = className,
                ShortDescription = shortDesc,
                DetailedSteps = detailed,
                Keywords = string.Join(", ", kws.OrderBy(s => s)),
                FilePath = relPath,
                FullFilePath = filePath
            };
        }

        /// <summary>
        /// 一键：自动扫描并写入记忆。返回写入的条数
        /// </summary>
        public static int AutoImportToMemory(AssistantMemory memory)
        {
            int count = 0;
            try
            {
                var roots = DetectProjectRoots();
                var seen = new HashSet<string>();
                // 先读取现有 CmdArchive，避免重复
                var existing = memory.LoadAllMemory()
                    .Where(e => e.Type == MemoryType.CmdArchive && !string.IsNullOrEmpty(e.UserInput))
                    .Select(e => e.UserInput)
                    .ToList();

                foreach (var root in roots)
                {
                    var list = ScanProject(root);
                    foreach (var cmd in list)
                    {
                        if (!seen.Add(cmd.ClassName)) continue;
                        string key = "[命令名] " + cmd.ClassName;
                        if (existing.Any(x => x.Contains(key))) continue; // 已经有了就跳过（避免重复导入）

                        memory.SaveCmdArchive(
                            commandClassName: cmd.ClassName,
                            shortDescription: cmd.ShortDescription,
                            detailedSteps: cmd.DetailedSteps,
                            keywords: cmd.Keywords,
                            codeLocation: cmd.FilePath);
                        count++;
                    }
                }
            }
            catch { }
            return count;
        }

        /// <summary>
        /// 返回所有现有 CmdArchive 类名 + 描述的摘要（用于界面展示）
        /// </summary>
        public static List<CmdSummary> ListImported(AssistantMemory memory)
        {
            var list = new List<CmdSummary>();
            try
            {
                foreach (var e in memory.LoadAllMemory().Where(e => e.Type == MemoryType.CmdArchive))
                {
                    list.Add(new CmdSummary
                    {
                        ClassName = ExtractToken(e.UserInput, "[命令名] "),
                        ShortDesc = ExtractToken(e.UserInput, "[一句话描述] "),
                        Detail = ExtractToken(e.AiResponse, "[详细功能与步骤] "),
                        Keywords = ExtractToken(e.ToolArgs, "[关键词] "),
                        Location = ExtractToken(e.ToolArgs, "[代码位置] "),
                        Time = e.Timestamp
                    });
                }
            }
            catch { }
            return list;
        }

        private static string ExtractToken(string src, string prefix)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(prefix)) return "";
            int idx = src.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) return "";
            int start = idx + prefix.Length;
            // 下一个 [ 标记或字符串结尾
            int end = src.IndexOf("  [", start, StringComparison.Ordinal);
            if (end < 0) end = src.Length;
            return src.Substring(start, end - start).Trim();
        }
    }

    /// <summary>
    /// 从 C# 源码解析出来的命令结构
    /// </summary>
    public class CmdExtracted
    {
        public string ClassName { get; set; }
        public string ShortDescription { get; set; }
        public string DetailedSteps { get; set; }
        public string Keywords { get; set; }
        public string FilePath { get; set; }
        public string FullFilePath { get; set; }
    }

    /// <summary>
    /// 已导入命令的摘要（供界面展示）
    /// </summary>
    public class CmdSummary
    {
        public string ClassName { get; set; }
        public string ShortDesc { get; set; }
        public string Detail { get; set; }
        public string Keywords { get; set; }
        public string Location { get; set; }
        public DateTime Time { get; set; }
    }
}
