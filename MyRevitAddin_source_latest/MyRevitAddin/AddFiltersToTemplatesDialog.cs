using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MyRevitAddin
{
    /// <summary>
    /// 批量添加过滤器到视图样板对话框
    ///
    /// 左侧：视图样板列表（可多选）
    /// 右侧：过滤器列表（可多选）
    /// 底部：操作说明 + 按钮
    /// </summary>
    public class AddFiltersToTemplatesDialog : Form
    {
        private readonly List<View> _allTemplates;
        private readonly List<Element> _allFilters;

        private CheckedListBox ChkTemplates;    // 视图样板勾选列表
        private CheckedListBox ChkFilters;      // 过滤器勾选列表
        private Label LblTemplateCount;
        private Label LblFilterCount;
        private Label LblTotal;
        private Button BtnOk;
        private Button BtnCancel;

        // 公开属性
        public List<View> SelectedTemplates => GetSelectedTemplates();
        public List<Element> SelectedFilters => GetSelectedFilters();

        public AddFiltersToTemplatesDialog(List<View> allTemplates, List<Element> allFilters)
        {
            _allTemplates = allTemplates;
            _allFilters = allFilters;

            InitializeUI();
            UpdateTotal();
        }

        private void InitializeUI()
        {
            this.Text = "批量添加过滤器到视图样板";
            this.Size = new Size(820, 580);
            this.MinimumSize = new Size(700, 450);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = Color.White;

            var font = new Font("Microsoft YaHei UI", 9F);
            var boldFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            var smallFont = new Font("Microsoft YaHei UI", 8F);

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
                Text = "批量添加过滤器到视图样板",
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
                Text = "💡 左侧勾选视图样板 → 右侧勾选要添加的过滤器 → 点击确定（过滤器将被添加并设为不可见）",
                Font = smallFont,
                ForeColor = Color.FromArgb(80, 80, 80),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            subTitlePanel.Controls.Add(subTitle);

            // ===== 主体：左侧样板 + 右侧过滤器 =====
            var mainSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6,
                BorderStyle = BorderStyle.None,
                Padding = new Padding(8, 8, 8, 0)
            };
            mainSplitter.SplitterDistance = (int)(this.ClientSize.Width * 0.5);

            // 左侧面板：视图样板
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 4, 0) };
            var leftHeader = new Label
            {
                Text = "📋 视图样板（可多选）",
                Font = boldFont,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, 4)
            };

            ChkTemplates = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                Font = font,
                CheckOnClick = true,
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Sorted = true
            };
            ChkTemplates.Items.AddRange(_allTemplates.Select(t => t.Name).ToArray());
            ChkTemplates.ItemCheck += (s, e) => { BeginInvoke((MethodInvoker)UpdateTotal); };

            // 全选按钮
            var selectAllTemplatesBtn = new Button
            {
                Text = "全选",
                Font = smallFont,
                Width = 60,
                Height = 22,
                Top = 28,
                Left = 4
            };
            selectAllTemplatesBtn.Click += (s, e) =>
            {
                for (int i = 0; i < ChkTemplates.Items.Count; i++)
                    ChkTemplates.SetItemChecked(i, true);
            };

            var clearTemplatesBtn = new Button
            {
                Text = "清空",
                Font = smallFont,
                Width = 60,
                Height = 22,
                Top = 28,
                Left = 68
            };
            clearTemplatesBtn.Click += (s, e) =>
            {
                for (int i = 0; i < ChkTemplates.Items.Count; i++)
                    ChkTemplates.SetItemChecked(i, false);
            };

            LblTemplateCount = new Label
            {
                Text = $"共 {_allTemplates.Count} 个样板",
                Font = smallFont,
                ForeColor = Color.Gray,
                AutoSize = true,
                Top = 32,
                Left = 136
            };

            leftPanel.Controls.AddRange(new Control[]
                { ChkTemplates, leftHeader, selectAllTemplatesBtn, clearTemplatesBtn, LblTemplateCount });

            // 右侧面板：过滤器
            var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 0, 0) };
            var rightHeader = new Label
            {
                Text = "🎯 过滤器（可多选）",
                Font = boldFont,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(0, 0, 0, 4)
            };

            ChkFilters = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                Font = font,
                CheckOnClick = true,
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle,
                Sorted = true
            };
            ChkFilters.Items.AddRange(_allFilters.Select(f => f.Name).ToArray());
            ChkFilters.ItemCheck += (s, e) => { BeginInvoke((MethodInvoker)UpdateTotal); };

            var selectAllFiltersBtn = new Button
            {
                Text = "全选",
                Font = smallFont,
                Width = 60,
                Height = 22,
                Top = 28,
                Left = 4
            };
            selectAllFiltersBtn.Click += (s, e) =>
            {
                for (int i = 0; i < ChkFilters.Items.Count; i++)
                    ChkFilters.SetItemChecked(i, true);
            };

            var clearFiltersBtn = new Button
            {
                Text = "清空",
                Font = smallFont,
                Width = 60,
                Height = 22,
                Top = 28,
                Left = 68
            };
            clearFiltersBtn.Click += (s, e) =>
            {
                for (int i = 0; i < ChkFilters.Items.Count; i++)
                    ChkFilters.SetItemChecked(i, false);
            };

            LblFilterCount = new Label
            {
                Text = $"共 {_allFilters.Count} 个过滤器",
                Font = smallFont,
                ForeColor = Color.Gray,
                AutoSize = true,
                Top = 32,
                Left = 136
            };

            rightPanel.Controls.AddRange(new Control[]
                { ChkFilters, rightHeader, selectAllFiltersBtn, clearFiltersBtn, LblFilterCount });

            mainSplitter.Panel1.Controls.Add(leftPanel);
            mainSplitter.Panel2.Controls.Add(rightPanel);

            // ===== 底部 =====
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = Color.FromArgb(245, 245, 245),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(16, 10, 16, 8)
            };

            LblTotal = new Label
            {
                Text = "预计添加 0 个过滤器",
                Font = boldFont,
                ForeColor = Color.FromArgb(0, 120, 212),
                Location = new Point(16, 12),
                AutoSize = true
            };

            var descLabel = new Label
            {
                Text = "所有选中的过滤器将被添加到视图样板中，并设为不可见（取消勾选）",
                Font = smallFont,
                ForeColor = Color.Gray,
                Location = new Point(16, 36),
                AutoSize = true
            };

            BtnOk = new Button
            {
                Text = "✅ 确定",
                Width = 100,
                Height = 32,
                Font = font,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(bottomPanel.Width - 220, 18)
            };
            BtnOk.Click += BtnOk_Click;

            BtnCancel = new Button
            {
                Text = "取消",
                Width = 80,
                Height = 32,
                Font = font,
                DialogResult = DialogResult.Cancel,
                Location = new Point(bottomPanel.Width - 110, 18)
            };
            BtnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            bottomPanel.Controls.AddRange(new Control[]
                { LblTotal, descLabel, BtnOk, BtnCancel });

            // 动态调整按钮位置
            this.Resize += (s, e) =>
            {
                BtnOk.Location = new Point(bottomPanel.Width - 220, 18);
                BtnCancel.Location = new Point(bottomPanel.Width - 110, 18);
            };

            // 加载
            this.Controls.Add(mainSplitter);
            this.Controls.Add(subTitlePanel);
            this.Controls.Add(titlePanel);
            this.Controls.Add(bottomPanel);
        }

        private List<View> GetSelectedTemplates()
        {
            var selected = new List<View>();
            foreach (int idx in ChkTemplates.CheckedIndices)
            {
                if (idx >= 0 && idx < _allTemplates.Count)
                    selected.Add(_allTemplates[idx]);
            }
            return selected;
        }

        private List<Element> GetSelectedFilters()
        {
            var selected = new List<Element>();
            foreach (int idx in ChkFilters.CheckedIndices)
            {
                if (idx >= 0 && idx < _allFilters.Count)
                    selected.Add(_allFilters[idx]);
            }
            return selected;
        }

        private void UpdateTotal()
        {
            int templateCount = GetSelectedTemplates().Count;
            int filterCount = GetSelectedFilters().Count;
            int total = templateCount * filterCount;

            LblTotal.Text = $"预计添加 {total} 个过滤器到 {templateCount} 个视图样板";
            LblTotal.ForeColor = total > 200 ? Color.Red :
                                 total > 100 ? Color.Orange : Color.FromArgb(0, 120, 212);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var selectedTemplates = GetSelectedTemplates();
            var selectedFilters = GetSelectedFilters();

            if (selectedTemplates.Count == 0)
            {
                MessageBox.Show("请在左侧勾选至少一个视图样板。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (selectedFilters.Count == 0)
            {
                MessageBox.Show("请在右侧勾选至少一个过滤器。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int total = selectedTemplates.Count * selectedFilters.Count;
            if (total > 300)
            {
                var confirm = MessageBox.Show(
                    $"即将添加 {total} 个过滤器设置。\n是否继续？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
