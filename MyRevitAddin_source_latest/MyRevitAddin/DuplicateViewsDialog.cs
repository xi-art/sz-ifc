using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MyRevitAddin
{
    /// <summary>
    /// 批量复制视图对话框
    /// 
    /// 功能：
    /// - 左侧：源视图列表（支持多选 Ctrl/Shift 点击）
    /// - 右侧：视图样板列表（支持多选）
    /// - 底部：命名规则（固定格式） + 预览 + 按钮
    /// 
    /// 命名规则：{原视图名}（{视图样板名}）
    /// 例如：F01（喷淋系统）
    /// </summary>
    public class DuplicateViewsDialog : Form
    {
        private readonly Document _doc;
        private readonly List<View> _allViews;
        private readonly List<View> _allTemplates;

        private ListBox LstViews;          // 源视图列表
        private Label LblViewCount;
        private Label LblTemplateCount;
        private Label LblPreview;
        private TextBox TxtNamingPattern;
        private Label LblTotal;
        private Button BtnOk;
        private Button BtnCancel;
        private CheckedListBox ChkViewTemplates; // 样板勾选列表

        // 公开属性
        public List<View> SelectedViews => GetSelectedViews();
        public List<View> SelectedTemplates => GetSelectedTemplates();
        public string NamingPattern => TxtNamingPattern.Text.Trim();

        // 固定值：每个源视图 × 每个选中的视图样板 = 复制份数
        public bool CopyOncePerTemplate => true;

        public DuplicateViewsDialog(Document doc, List<View> allViews)
        {
            _doc = doc;
            _allViews = allViews;

            // 收集所有视图样板
            _allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.ViewType != ViewType.Internal)
                .OrderBy(v => v.Name)
                .ToList();

            InitializeUI();
            UpdatePreview();
        }

        private void InitializeUI()
        {
            this.Text = "批量复制视图并设置视图样板";
            this.Size = new Size(820, 620);
            this.MinimumSize = new Size(700, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = Color.White;

            var font = new Font("Microsoft YaHei UI", 9F);
            var boldFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            var smallFont = new Font("Microsoft YaHei UI", 8F);
            var titleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);

            // ===== 顶部标题栏 =====
            var titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(0, 120, 212),
                Padding = new Padding(16, 0, 16, 0)
            };

            var titleLabel = new Label
            {
                Text = "批量复制视图并设置视图样板",
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            titlePanel.Controls.Add(titleLabel);

            // 副标题
            var subTitlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Color.FromArgb(240, 248, 255),
                Padding = new Padding(16, 0, 16, 0)
            };
            var subTitle = new Label
            {
                Text = "💡 操作说明：左侧选择要复制的源视图（支持 Ctrl/Shift 多选）→ 右侧勾选要应用的视图样板 → 点击确定",
                Font = smallFont,
                ForeColor = Color.FromArgb(80, 80, 80),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            subTitlePanel.Controls.Add(subTitle);

            // ===== 主体：左侧视图 + 右侧样板 =====
            var mainSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6,
                BorderStyle = BorderStyle.None,
                Padding = new Padding(8, 8, 8, 0)
            };
            mainSplitter.SplitterDistance = (int)(this.ClientSize.Width * 0.5);

            // 左侧面板：源视图
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 4, 0) };
            var leftHeader = new Label
            {
                Text = "📋 源视图",
                Font = boldFont,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, 4)
            };
            LstViews = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = font,
                SelectionMode = SelectionMode.MultiExtended,
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Sorted = false
            };
            LstViews.Items.AddRange(_allViews.Select(v =>
                $"{GetViewTypeLabel(v.ViewType)}  {v.Name}").ToArray());
            LstViews.SelectedIndexChanged += (s, e) => UpdatePreview();

            // 全选按钮
            var selectAllViewsBtn = new Button
            {
                Text = "全选",
                Font = smallFont,
                Width = 60,
                Height = 22,
                Top = 28,
                Left = 4
            };
            selectAllViewsBtn.Click += (s, e) =>
            {
                for (int i = 0; i < LstViews.Items.Count; i++)
                    LstViews.SetSelected(i, true);
            };
            LblViewCount = new Label
            {
                Text = $"共 {_allViews.Count} 个视图",
                Font = smallFont,
                ForeColor = Color.Gray,
                AutoSize = true,
                Top = 32,
                Left = 72
            };

            leftPanel.Controls.AddRange(new Control[] { LstViews, leftHeader, selectAllViewsBtn, LblViewCount });

            // 右侧面板：视图样板
            var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 0) };
            var rightHeader = new Label
            {
                Text = "🎯 视图样板（可多选）",
                Font = boldFont,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, 4)
            };

            ChkViewTemplates = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                Font = font,
                CheckOnClick = true,
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Sorted = true
            };
            ChkViewTemplates.Items.AddRange(_allTemplates.Select(t => t.Name).ToArray());
            ChkViewTemplates.ItemCheck += (s, e) => UpdatePreview();

            LblTemplateCount = new Label
            {
                Text = $"共 {_allTemplates.Count} 个样板",
                Font = smallFont,
                ForeColor = Color.Gray,
                AutoSize = true,
                Top = 32,
                Left = 4
            };

            rightPanel.Controls.AddRange(new Control[] { ChkViewTemplates, rightHeader, LblTemplateCount });

            mainSplitter.Panel1.Controls.Add(leftPanel);
            mainSplitter.Panel2.Controls.Add(rightPanel);

            // ===== 底部配置区 =====
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 110,
                BackColor = Color.FromArgb(245, 245, 245),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(16, 10, 16, 8)
            };

            // 命名规则
            var patternLabel = new Label
            {
                Text = "命名规则",
                Font = boldFont,
                Location = new Point(16, 12),
                AutoSize = true
            };
            TxtNamingPattern = new TextBox
            {
                Text = "{view}（{template}）",
                Font = new Font("Consolas", 10F),
                Location = new Point(16, 32),
                Width = 220,

            };

            // 示例
            var exampleLabel = new Label
            {
                Text = "示例：F01（喷淋系统）",
                Font = smallFont,
                ForeColor = Color.Gray,
                Location = new Point(244, 36),
                AutoSize = true
            };

            // 预览
            LblPreview = new Label
            {
                Text = "预览：",
                Font = smallFont,
                ForeColor = Color.FromArgb(0, 120, 212),
                Location = new Point(16, 60),
                AutoSize = true
            };

            // 预计数量
            LblTotal = new Label
            {
                Text = "预计生成 0 个视图",
                Font = boldFont,
                Location = new Point(400, 32),
                ForeColor = Color.FromArgb(0, 120, 212),
                AutoSize = true
            };

            // 按钮
            BtnOk = new Button
            {
                Text = "✅ 确定",
                Width = 100,
                Height = 32,
                Font = font,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(this.ClientSize.Width - 220, 60)
            };
            BtnOk.Click += BtnOk_Click;

            BtnCancel = new Button
            {
                Text = "取消",
                Width = 80,
                Height = 32,
                Font = font,
                DialogResult = DialogResult.Cancel,
                Location = new Point(this.ClientSize.Width - 110, 60)
            };
            BtnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            bottomPanel.Controls.AddRange(new Control[]
                { patternLabel, TxtNamingPattern, exampleLabel, LblPreview, LblTotal, BtnOk, BtnCancel });

            // 动态调整按钮位置（确保在底部面板内）
            BtnOk.Location = new Point(bottomPanel.Width - 188, 60);
            BtnCancel.Location = new Point(bottomPanel.Width - 88, 60);

            // 加载
            this.Controls.Add(mainSplitter);
            this.Controls.Add(subTitlePanel);
            this.Controls.Add(titlePanel);
            this.Controls.Add(bottomPanel);

            // 窗口大小变化时重定位按钮
            this.Resize += (s, e) =>
            {
                BtnOk.Location = new Point(bottomPanel.Width - 188, 60);
                BtnCancel.Location = new Point(bottomPanel.Width - 88, 60);
            };
        }

        private List<View> GetSelectedViews()
        {
            var selected = new List<View>();
            foreach (int idx in LstViews.SelectedIndices)
            {
                if (idx >= 0 && idx < _allViews.Count)
                    selected.Add(_allViews[idx]);
            }
            return selected;
        }

        private List<View> GetSelectedTemplates()
        {
            var selected = new List<View>();
            foreach (int idx in ChkViewTemplates.CheckedIndices)
            {
                if (idx >= 0 && idx < _allTemplates.Count)
                    selected.Add(_allTemplates[idx]);
            }
            return selected;
        }

        private void UpdatePreview()
        {
            var selectedViews = GetSelectedViews();
            var selectedTemplates = GetSelectedTemplates();

            int viewCount = selectedViews.Count;
            int templateCount = selectedTemplates.Count;
            int total = viewCount * templateCount;

            LblTotal.Text = $"预计生成 {total} 个视图";
            LblTotal.ForeColor = total > 200 ? Color.Red :
                                 total > 100 ? Color.Orange : Color.FromArgb(0, 120, 212);

            if (viewCount > 0 && templateCount > 0)
            {
                var pattern = TxtNamingPattern.Text;
                var firstView = selectedViews[0];
                var firstTemplate = selectedTemplates[0];
                var exampleName = BuildExampleName(firstView.Name, firstTemplate.Name, pattern);
                LblPreview.Text = $"预览：{exampleName}  （共 {viewCount} 个视图 × {templateCount} 个样板）";
            }
            else if (viewCount > 0 && templateCount == 0)
            {
                LblPreview.Text = "请在右侧勾选要应用的视图样板";
            }
            else
            {
                LblPreview.Text = "请在左侧选择要复制的源视图";
            }
        }

        private string BuildExampleName(string viewName, string templateName, string pattern)
        {
            return pattern
                .Replace("{view}", viewName)
                .Replace("{template}", templateName);
        }

        private string GetViewTypeLabel(ViewType vt)
        {
            // 中文视图类型标签
            switch (vt)
            {
                case ViewType.FloorPlan: return "[平面]";
                case ViewType.CeilingPlan: return "[天花]";

                case ViewType.Section: return "[剖面]";
                case ViewType.Elevation: return "[立面]";
                case ViewType.ThreeD: return "[3D]";
                case ViewType.Detail: return "[详图]";
                case ViewType.Legend: return "[图例]";
                case ViewType.DrawingSheet: return "[图纸]";
                case ViewType.Report: return "[报告]";
                case ViewType.Schedule: return "[明细表]";
                case ViewType.Walkthrough: return "[漫游]";


                default: return "[? ]";
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var selectedViews = GetSelectedViews();
            var selectedTemplates = GetSelectedTemplates();

            if (selectedViews.Count == 0)
            {
                MessageBox.Show("请在左侧选择至少一个源视图。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (selectedTemplates.Count == 0)
            {
                MessageBox.Show("请在右侧勾选至少一个视图样板。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pattern = TxtNamingPattern.Text.Trim();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                MessageBox.Show("命名规则不能为空。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 确认数量
            int total = selectedViews.Count * selectedTemplates.Count;
            if (total > 500)
            {
                var confirm = MessageBox.Show(
                    $"即将创建 {total} 个视图，数量较大。\n是否继续？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
