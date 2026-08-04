using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;

namespace MyRevitAddin.AIAssistant
{
    public partial class MemoryManagementDialog : Window
    {
        private readonly AssistantMemory _memory;
        private List<CmdExtracted> _scannedCmds;
        private List<MemoryRow> _filteredRows;

        public MemoryManagementDialog(AssistantMemory memory, int initialTab = 0)
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("MemoryManagementDialog InitializeComponent 失败: " + ex.Message);
            }

            try
            {
                _memory = memory ?? new AssistantMemory();
                _scannedCmds = new List<CmdExtracted>();
                _filteredRows = new List<MemoryRow>();

                if (TxtMemLocation != null)
                    TxtMemLocation.Text = "💾 记忆文件保存位置：" + _memory.MemoryDir;
            }
            catch { }

            Loaded += (s, e) =>
            {
                try
                {
                    if (TxtMemLocation != null && _memory != null)
                        TxtMemLocation.Text = "💾 记忆文件保存位置：" + _memory.MemoryDir;

                    if (Tabs != null && initialTab >= 0 && initialTab < Tabs.Items.Count)
                        Tabs.SelectedIndex = initialTab;

                    RefreshList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("MemoryManagementDialog Loaded 失败: " + ex.Message);
                }
            };
        }

        // =====================================================
        // Tab 1：💾 记录工作流
        // =====================================================

        private void TxtWorkflowTitle_GotFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb != null && tb.Text.StartsWith("例："))
            {
                tb.Text = "";
                tb.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void BtnSaveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            string title = (TxtWorkflowTitle.Text ?? "").Trim();
            if (string.IsNullOrEmpty(title) || title.StartsWith("例："))
            {
                MessageBox.Show(this, "请先填写工作流标题", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string steps = (TxtWorkflowSteps.Text ?? "").Trim();
            string lessons = (TxtWorkflowLessons.Text ?? "").Trim();
            string ctx = (TxtWorkflowContext.Text ?? "").Trim();

            if (string.IsNullOrEmpty(steps))
            {
                if (MessageBox.Show(this, "步骤内容为空，确定要保存吗？", "确认",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                    return;
            }

            _memory.SaveWorkflow(title, steps, lessons, ctx);
            MessageBox.Show(this,
                "✅ 工作流已保存！\n\n标题: " + title +
                (!string.IsNullOrEmpty(ctx) ? "\n上下文: " + ctx : "") +
                "\n\n后续每次与 AI 对话时，这条工作流会自动注入到系统提示词中，AI 会优先参考。",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);

            // 清空
            TxtWorkflowTitle.Text = "";
            TxtWorkflowSteps.Text = "";
            TxtWorkflowLessons.Text = "";
            RefreshList();
        }

        // =====================================================
        // Tab 2：📚 导入规则 / 自动扫描命令档案
        // =====================================================

        private void BtnScanPreview_Click(object sender, RoutedEventArgs e)
        {
            TxtAutoScanStatus.Text = "🔎 正在扫描项目源码路径...";
            BtnScanImport.IsEnabled = false;

            var roots = CmdArchiveExtractor.DetectProjectRoots();
            if (roots.Count == 0)
            {
                // 兜底：工作目录作为提示
                roots.Add(Environment.CurrentDirectory);
            }

            _scannedCmds = new List<CmdExtracted>();
            var sb = new StringBuilder();
            int totalFiles = 0;
            foreach (var root in roots)
            {
                sb.AppendLine("扫描路径: " + root);
                var list = CmdArchiveExtractor.ScanProject(root);
                totalFiles += list.Count;
                foreach (var cmd in list)
                {
                    if (!_scannedCmds.Any(c => c.ClassName == cmd.ClassName))
                        _scannedCmds.Add(cmd);
                }
            }

            if (_scannedCmds.Count == 0)
            {
                TxtAutoScanStatus.Text = "❌ 未找到任何 *Command.cs 文件。你可以手动把源码路径写进 " +
                    Path.Combine(_memory.MemoryDir, "project_paths.txt") + " 里，一行一个路径。";
                BtnScanImport.IsEnabled = false;
                MessageBox.Show(this, "当前没有找到命令文件。\n记忆目录: " + _memory.MemoryDir,
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            sb.AppendFormat("共找到 {0} 个命令（去重后），来自 {1} 个源文件：\n", _scannedCmds.Count, totalFiles);
            int i = 1;
            int skipped = 0;
            var existing = CmdArchiveExtractor.ListImported(_memory).Select(c => c.ClassName).ToHashSet();
            foreach (var cmd in _scannedCmds)
            {
                string dupMark = existing.Contains(cmd.ClassName) ? "  [已导入，跳过]" : "";
                if (!string.IsNullOrEmpty(dupMark)) skipped++;
                sb.AppendFormat("  {0}. {1}{2}\n", i++, cmd.ClassName, dupMark);
                if (!string.IsNullOrEmpty(cmd.ShortDescription))
                    sb.AppendFormat("       摘要: {0}\n", cmd.ShortDescription);
                if (!string.IsNullOrEmpty(cmd.Keywords))
                    sb.AppendFormat("       关键词: {0}\n", cmd.Keywords);
            }
            sb.AppendFormat("\n提示：未导入的共 {0} 条，点击「导入到记忆」即可写入。",
                _scannedCmds.Count - skipped);

            TxtRuleContent.Text = sb.ToString();
            TxtAutoScanStatus.Text =
                string.Format("✅ 扫描完成：{0} 个命令，其中 {1} 个是新的（未导入）。",
                    _scannedCmds.Count, _scannedCmds.Count - skipped);

            BtnScanImport.IsEnabled = _scannedCmds.Count - skipped > 0;
        }

        private void BtnScanImport_Click(object sender, RoutedEventArgs e)
        {
            int imported = CmdArchiveExtractor.AutoImportToMemory(_memory);
            if (imported <= 0)
            {
                MessageBox.Show(this, "没有新增的命令（可能都已导入过）。", "信息",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            MessageBox.Show(this,
                string.Format("✅ 成功导入 {0} 条命令档案到记忆！\n\n现在每次和 AI 对话，这些命令都会自动出现在系统提示词里的「本地可用的 Revit 命令档案」段落，AI 会优先推荐你已有的本地功能。", imported),
                "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            BtnScanImport.IsEnabled = false;
            RefreshList();
        }

        private void BtnSaveRule_Click(object sender, RoutedEventArgs e)
        {
            string title = (TxtRuleTitle.Text ?? "").Trim();
            string content = (TxtRuleContent.Text ?? "").Trim();
            string category = (CmbRuleCategory.Text ?? "").Trim();
            string source = (TxtRuleSource.Text ?? "").Trim();

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show(this, "请填写规则标题", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show(this, "规则内容不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(category)) category = "其他";
            if (string.IsNullOrEmpty(source)) source = "人工录入";

            _memory.SaveKnowledgeRule(category, title, content, source);

            MessageBox.Show(this,
                "✅ 业务规则已保存！\n\n分类: " + category + "\n标题: " + title +
                "\n来源: " + source + "\n\n以后每次对话都会注入到「业务规则/项目规范」段落，AI 会严格按照这个回答。",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);

            TxtRuleTitle.Text = "";
            TxtRuleContent.Text = "";
            RefreshList();
        }

        // =====================================================
        // Tab 3：📖 记忆总览 / 搜索 / 删除 / 导入导出
        // =====================================================

        class MemoryRow
        {
            public MemoryEntry Entry { get; set; }
            public string Time { get; set; }
            public string Type { get; set; }
            public string Summary { get; set; }
        }

        private void RefreshList()
        {
            var search = (TxtSearch?.Text ?? "").Trim();
            string typeTag = "ALL";
            if (CmbTypeFilter?.SelectedItem is ComboBoxItem ci && ci.Tag is string t)
                typeTag = t;

            var all = _memory.LoadAllMemory();
            var filtered = all.AsEnumerable();

            if (!string.Equals(typeTag, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(e => e.Type.ToString() == typeTag);
            }

            if (!string.IsNullOrEmpty(search))
            {
                string s = search.ToLower();
                filtered = filtered.Where(e =>
                    ((e.UserInput ?? "").ToLower().Contains(s)) ||
                    ((e.AiResponse ?? "").ToLower().Contains(s)) ||
                    ((e.ToolArgs ?? "").ToLower().Contains(s)) ||
                    ((e.Result ?? "").ToLower().Contains(s)) ||
                    ((e.ToolName ?? "").ToLower().Contains(s)));
            }

            var rows = filtered
                .OrderByDescending(e => e.Timestamp)
                .Select(e => new MemoryRow
                {
                    Entry = e,
                    Time = e.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                    Type = TypeDisplayName(e.Type),
                    Summary = SummarizeEntry(e, 80)
                })
                .ToList();

            _filteredRows = rows;
            LstEntries.ItemsSource = rows;
            LstEntries.Items.Refresh();

            // 统计
            var allList = all.ToList();
            string summary =
                "总计 " + allList.Count + " 条记忆  |  " +
                "对话 " + allList.Count(e => e.Type == MemoryType.Conversation) + " / " +
                "操作 " + allList.Count(e => e.Type == MemoryType.Operation) + " / " +
                "偏好 " + allList.Count(e => e.Type == MemoryType.UserPreference) + " / " +
                "💾工作流 " + allList.Count(e => e.Type == MemoryType.Workflow) + " / " +
                "📚规则 " + allList.Count(e => e.Type == MemoryType.KnowledgeRule) + " / " +
                "🤖命令档案 " + allList.Count(e => e.Type == MemoryType.CmdArchive);
            if (!string.IsNullOrEmpty(search) || typeTag != "ALL")
                summary += "  |  当前筛选显示: " + rows.Count;
            TxtSummary.Text = summary;
        }

        private static string TypeDisplayName(MemoryType t)
        {
            switch (t)
            {
                case MemoryType.Conversation: return "💬 对话";
                case MemoryType.Operation: return "🛠 操作";
                case MemoryType.UserPreference: return "⭐ 偏好";
                case MemoryType.Workflow: return "💾 工作流";
                case MemoryType.KnowledgeRule: return "📚 规则";
                case MemoryType.CmdArchive: return "🤖 命令档案";
                default: return t.ToString();
            }
        }

        private static string SummarizeEntry(MemoryEntry e, int max)
        {
            string[] parts =
            {
                e.UserInput, e.AiResponse, e.ToolName, e.ToolArgs, e.Result
            };
            foreach (var p in parts)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    string clean = p.Replace("\n", " ").Replace("\r", "");
                    if (clean.Length > max) clean = clean.Substring(0, max) + "...";
                    return clean;
                }
            }
            return "(空)";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void CmbTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshList();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void LstEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = LstEntries.SelectedItem as MemoryRow;
            if (row == null) return;
            MemoryEntry en = row.Entry;
            var sb = new StringBuilder();
            sb.AppendLine("【时间】 " + en.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("【类型】 " + en.Type + "  (" + TypeDisplayName(en.Type) + ")");
            if (!string.IsNullOrEmpty(en.ToolName)) sb.AppendLine("【ToolName】 " + en.ToolName);
            if (!string.IsNullOrEmpty(en.UserInput)) sb.AppendLine("【用户/标题】\n" + en.UserInput + "\n");
            if (!string.IsNullOrEmpty(en.AiResponse)) sb.AppendLine("【AI/内容/步骤】\n" + en.AiResponse + "\n");
            if (!string.IsNullOrEmpty(en.ToolArgs)) sb.AppendLine("【参数/分类/关键词】\n" + en.ToolArgs + "\n");
            if (!string.IsNullOrEmpty(en.Result)) sb.AppendLine("【结果/经验】\n" + en.Result + "\n");

            RtbDetail.Document.Blocks.Clear();
            var para = new Paragraph();
            para.Inlines.Add(new Run(sb.ToString()));
            RtbDetail.Document.Blocks.Add(para);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var rows = LstEntries.SelectedItems.Cast<MemoryRow>().ToList();
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "请先在左侧列表选择要删除的项（按住 Ctrl 多选）", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this,
                string.Format("确认删除选中的 {0} 条记忆？（此操作不可恢复）", rows.Count),
                "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;

            int del = 0;
            foreach (var r in rows)
            {
                DateTime exact = r.Entry.Timestamp;
                string typeExact = r.Entry.Type.ToString();
                string uExact = r.Entry.UserInput ?? "";
                if (_memory.DeleteMemory(en =>
                    en.Timestamp == exact &&
                    en.Type.ToString() == typeExact &&
                    (en.UserInput ?? "") == uExact))
                    del++;
            }
            MessageBox.Show(this, "✅ 已删除 " + del + " 条记忆", "完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshList();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string data = _memory.ExportAllMemoryAsJsonLines();
                var dlg = new SaveFileDialog
                {
                    Filter = "记忆 JSONL 文件 (*.jsonl)|*.jsonl|所有文件 (*.*)|*.*",
                    FileName = "MyRevitAddin_Memory_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    File.WriteAllText(dlg.FileName, data, new UTF8Encoding(false));
                    MessageBox.Show(this,
                        "✅ 已导出 " + _memory.LoadAllMemory().Count + " 条记忆到:\n" + dlg.FileName,
                        "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败: " + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "记忆 JSONL 文件 (*.jsonl)|*.jsonl|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) != true) return;
                string content = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                int n = _memory.ImportMemoryFromJsonLines(content);
                MessageBox.Show(this,
                    "✅ 成功导入 " + n + " 条记忆",
                    "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导入失败: " + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
