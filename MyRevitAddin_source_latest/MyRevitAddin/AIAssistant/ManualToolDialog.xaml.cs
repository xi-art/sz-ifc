using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MyRevitAddin.AIAssistant
{
    public partial class ManualToolDialog : Window
    {
        public string ResultToolName { get; private set; }
        public string ResultArgsJson { get; private set; }
        public string ResultJson { get; private set; }

        private static readonly Dictionary<string, string> ToolDescriptions = new Dictionary<string, string>
        {
            { "get_document_info", "当前文档/文件信息（名称、路径、图元数、保存状态等）。无参数。" },
            { "get_all_levels", "项目所有标高（按高度排序，含米单位）。无参数。" },
            { "get_project_sheets", "项目所有图纸（Sheet）列表：编号、名称、视口数量。无参数。" },
            { "get_all_views", "项目视图列表。参数: view_type = All / FloorPlan / ThreeD / DrawingSheet / Section / Elevation" },
            { "get_selected_elements", "当前在 Revit 界面中已选中的图元列表。无参数。" },
            { "select_elements_by_category", "按类别在 Revit 中批量选中图元。参数: category_name (中/英文)、limit (默认500)" },
            { "set_instance_parameter", "修改已选中图元的实例参数。参数: element_ids、param_name、param_value" },
            { "batch_set_parameter", "批量按规则设置参数。参数: param_name、param_value、可选 level_start/level_end 标高过滤" },
            { "replace_family", "替换已选中图元的族类型。参数: target_family_type_name" },
            { "save_memory", "保存记忆。参数: key、content" },
            { "search_memory", "搜索历史记忆。参数: keyword" }
        };

        public ManualToolDialog()
        {
            InitializeComponent();
            LoadTools();
        }

        private void LoadTools()
        {
            CmbTools.Items.Clear();
            foreach (var kv in ToolDescriptions)
            {
                CmbTools.Items.Add(kv.Key);
            }
            if (CmbTools.Items.Count > 0)
                CmbTools.SelectedIndex = 0;
        }

        private void CmbTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string tool = CmbTools.SelectedItem?.ToString() ?? "";
            if (ToolDescriptions.ContainsKey(tool))
                TxtToolDesc.Text = ToolDescriptions[tool];
            else
                TxtToolDesc.Text = "";

            // 给默认参数模板
            switch (tool)
            {
                case "get_all_views":
                    TxtArgs.Text = "{\n  \"view_type\": \"All\"\n}";
                    break;
                case "select_elements_by_category":
                    TxtArgs.Text = "{\n  \"category_name\": \"门\",\n  \"limit\": 500\n}";
                    break;
                case "search_memory":
                    TxtArgs.Text = "{\n  \"keyword\": \"参数设置\"\n}";
                    break;
                case "save_memory":
                    TxtArgs.Text = "{\n  \"key\": \"操作方法\",\n  \"content\": \"先选中门，再替换类型\"\n}";
                    break;
                case "batch_set_parameter":
                    TxtArgs.Text = "{\n  \"param_name\": \"楼层\",\n  \"param_value\": \"F1\",\n  \"level_start\": \"0.000\",\n  \"level_end\": \"4.000\"\n}";
                    break;
                case "set_instance_parameter":
                    TxtArgs.Text = "{\n  \"element_ids\": \"12345,67890\",\n  \"param_name\": \"系统类型\",\n  \"param_value\": \"暖通\"\n}";
                    break;
                case "replace_family":
                    TxtArgs.Text = "{\n  \"target_family_type_name\": \"M2\"\n}";
                    break;
                default:
                    TxtArgs.Text = "{}";
                    break;
            }
        }

        // ========== 快捷参数模板按钮 ==========

        private void Tpl_ViewsAll(object sender, RoutedEventArgs e)
        {
            if (!CmbTools.Items.Contains("get_all_views")) return;
            CmbTools.SelectedItem = "get_all_views";
            TxtArgs.Text = "{\n  \"view_type\": \"All\"\n}";
        }

        private void Tpl_ViewsFloor(object sender, RoutedEventArgs e)
        {
            if (!CmbTools.Items.Contains("get_all_views")) return;
            CmbTools.SelectedItem = "get_all_views";
            TxtArgs.Text = "{\n  \"view_type\": \"FloorPlan\"\n}";
        }

        private void Tpl_SelectDoor(object sender, RoutedEventArgs e)
        {
            if (!CmbTools.Items.Contains("select_elements_by_category")) return;
            CmbTools.SelectedItem = "select_elements_by_category";
            TxtArgs.Text = "{\n  \"category_name\": \"门\",\n  \"limit\": 500\n}";
        }

        private void Tpl_Clear(object sender, RoutedEventArgs e)
        {
            TxtArgs.Text = "{}";
        }

        // ========== 执行工具 ==========

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string toolName = CmbTools.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrEmpty(toolName))
            {
                MessageBox.Show(this, "请先选择要执行的工具", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string argsJson = (TxtArgs.Text ?? "").Trim();
            if (string.IsNullOrEmpty(argsJson)) argsJson = "{}";

            try
            {
                var executor = new RevitOperationExecutor(null);
                Dictionary<string, object> argsDict = ParseArgsJson(argsJson);

                TxtTip.Text = "正在执行工具 " + toolName + " ...";
                BtnRun.IsEnabled = false;
                string json = executor.ExecuteDirect(toolName, argsDict);

                // 显示结果
                SetResultText(json);
                TxtResultInfo.Text = "✅ 执行成功 · 返回 " + json.Length + " 字符 · " + DateTime.Now.ToString("HH:mm:ss");
                TxtTip.Text = "✅ 执行成功，可点「确定」把结果插入 AI 聊天窗口";

                ResultToolName = toolName;
                ResultArgsJson = argsJson;
                ResultJson = json;
                BtnOk.IsEnabled = true;
            }
            catch (Exception ex)
            {
                SetResultText("❌ 执行失败：\n" +
                              ex.GetType().Name + ": " + ex.Message + "\n\n" +
                              "StackTrace:\n" + ex.StackTrace);
                TxtResultInfo.Text = "❌ 执行失败";
                TxtTip.Text = "❌ 执行失败：" + ex.Message;
                ResultJson = null;
                BtnOk.IsEnabled = false;
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ResultJson))
            {
                MessageBox.Show(this, "请先执行工具并得到成功结果", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void BtnCopyResult_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var range = new TextRange(TxtResult.Document.ContentStart, TxtResult.Document.ContentEnd);
                if (!string.IsNullOrEmpty(range.Text))
                {
                    Clipboard.SetText(range.Text);
                    TxtResultInfo.Text = "✅ 已复制到剪贴板 · " + DateTime.Now.ToString("HH:mm:ss");
                }
            }
            catch { }
        }

        // ========== 辅助 ==========

        private void SetResultText(string text)
        {
            TxtResult.Document.Blocks.Clear();
            var para = new Paragraph();
            para.Inlines.Add(new Run(text ?? ""));
            TxtResult.Document.Blocks.Add(para);
        }

        private static Dictionary<string, object> ParseArgsJson(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json)) return result;
            json = json.Trim();
            if (json == "{}") return result;
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            // 简单但健壮的逗号分割：跳过 {} 里的内容
            int depth = 0;
            int start = 0;
            var pairs = new List<string>();
            for (int i = 0; i <= json.Length; i++)
            {
                if (i == json.Length)
                {
                    if (start < i) pairs.Add(json.Substring(start, i - start));
                    break;
                }
                char c = json[i];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    pairs.Add(json.Substring(start, i - start));
                    start = i + 1;
                }
            }

            foreach (var pair in pairs)
            {
                string trimmed = pair.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var kv = trimmed.Split(new[] { ':' }, 2);
                if (kv.Length == 2)
                {
                    string key = TrimQuotes(kv[0].Trim());
                    string val = TrimQuotes(kv[1].Trim());
                    // 尝试转换数字
                    if (int.TryParse(val, out int intVal))
                        result[key] = intVal;
                    else if (double.TryParse(val, out double dVal))
                        result[key] = dVal;
                    else if (val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        result[key] = true;
                    else if (val.Equals("false", StringComparison.OrdinalIgnoreCase))
                        result[key] = false;
                    else if (val.Equals("null", StringComparison.OrdinalIgnoreCase))
                        result[key] = null;
                    else
                        result[key] = val;
                }
            }
            return result;
        }

        private static string TrimQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2);
            // 去除单引号兜底
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                s = s.Substring(1, s.Length - 2);
            return Regex.Unescape(s);
        }
    }
}
