using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MyRevitAddin.BatchRenameFamilies
{
    public enum RenameTarget
    {
        FamilyName = 0,
        FamilySymbolName = 1,
        Both = 2
    }

    public class RenameRule
    {
        public int Order { get; set; }
        public string RuleType { get; set; }
        public string Arg1 { get; set; }
        public string Arg2 { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class RenameItem
    {
        public ElementId Id { get; set; }
        public ElementId ParentId { get; set; }
        public string Kind { get; set; }
        public string CategoryName { get; set; }
        public string ParentName { get; set; }
        public string ParentNewName { get; set; }
        public string OriginalName { get; set; }
        public string NewName { get; set; }
        public string Remark { get; set; }
        public bool Selected { get; set; } = true;
    }

    public partial class BatchRenameFamiliesDialog : Form
    {
        private readonly Document _doc;

        private List<RenameRule> _rules;
        private List<RenameItem> _items;
        private List<RenameItem> _allItems;
        private bool _isLoadingData;
        private bool _isRefreshingPreview;
        private BindingSource _bindingSource;
        private int _lastCheckIndex = -1;
        private bool _headerCheckboxChecked = true;

        private Panel _pTop;
        private Panel _pRules;
        private Label _lblPreview;
        private Panel _pBottom;
        private TableLayoutPanel _tlpMain;

        public List<RenameItem> ItemsToApply { get; private set; } = new List<RenameItem>();
        public RenameTarget Target { get; private set; } = RenameTarget.FamilyName;

        private ComboBox _cmbCategory;
        private DataGridView _dgvPreview;

        private TextBox _txtPrefix;
        private TextBox _txtSuffix;
        private TextBox _txtFind;
        private TextBox _txtReplace;
        private CheckBox _chkIgnoreCase;
        private CheckBox _chkRegex;
        private NumericUpDown _numInsertPos;
        private TextBox _txtInsertText;
        private NumericUpDown _numTrimStart;
        private NumericUpDown _numTrimEnd;
        private TextBox _txtCounterPrefix;
        private NumericUpDown _numCounterStart;
        private NumericUpDown _numCounterDigits;
        private CheckBox _chkUseCategoryPrefix;
        private CheckBox _chkEnableCounter;

        static BatchRenameFamiliesDialog()
        {
            MiniLog.Info("Dialog:STATIC-CTOR:RUN");
        }

        public BatchRenameFamiliesDialog(Document doc)
        {
            MiniLog.Info("CTOR:BODY-ENTER docIsNull=" + (doc == null ? "YES" : "NO"));
            _rules = new List<RenameRule>();
            MiniLog.Info("CTOR:_rules inited");
            _items = new List<RenameItem>();
            MiniLog.Info("CTOR:_items inited");
            _doc = doc;
            MiniLog.Info("CTOR:Fields done");
            try
            {
                MiniLog.Info("CTOR:InitializeComponent:START");
                InitializeComponent();
                MiniLog.Info("CTOR:InitializeComponent:END");
            }
            catch (Exception ex)
            {
                MiniLog.Error("CTOR:InitializeComponent", ex);
                throw;
            }
            try { MiniLog.Info("CTOR:LoadData:START"); LoadData(); MiniLog.Info("CTOR:LoadData:END"); }
            catch (Exception ex) { MiniLog.Error("CTOR:LoadData", ex); try { MessageBox.Show("加载族列表失败: " + ex.Message + "\n\n" + ex.StackTrace, "提示"); } catch { } }
            try { MiniLog.Info("CTOR:RefreshPreview:START"); RefreshPreview(); MiniLog.Info("CTOR:RefreshPreview:END"); }
            catch (Exception ex) { MiniLog.Error("CTOR:RefreshPreview", ex); try { MessageBox.Show("生成预览失败: " + ex.Message + "\n\n" + ex.StackTrace, "提示"); } catch { } }
            this.Shown += (s, e) =>
            {
                MiniLog.Info("FORM:Shown BEGIN SelectedIndex=" + _cmbCategory.SelectedIndex);
                this.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MiniLog.Info("FORM:BeginInvoke category idx=" + _cmbCategory.SelectedIndex);
                        if (_cmbCategory.Items.Count > 0) _cmbCategory.SelectedIndex = 0;
                        ApplyCategoryFilterOnly();
                        RefreshPreview();
                        RebindGrid();
                        if (_tlpMain != null) { _tlpMain.PerformLayout(); _tlpMain.Refresh(); }
                    }
                    catch (Exception ex) { MiniLog.Error("FORM:BeginInvoke", ex); }
                }));
            };
            MiniLog.Info("CTOR:BODY-EXIT");
        }

        private void InitializeComponent()
        {
            MiniLog.Info("INIT:FORM-PROPS:START");
            this.Text = "批量族重命名（深圳报建版）";
            this.Size = new Size(1400, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(1300, 720);
            this.Font = new Font("Microsoft YaHei UI", 9f);
            MiniLog.Info("INIT:FORM-PROPS:END");

            MiniLog.Info("INIT:pTop:START");
            Panel pTop = new Panel();
            pTop.Dock = DockStyle.Fill;
            pTop.Padding = new Padding(10, 8, 10, 4);

            Label lblMode = new Label();
            lblMode.Text = "目标：族类型 (FamilySymbol 名称) — 一行一个子类型（如 700x500），父族名见【父族】列";
            lblMode.Location = new Point(10, 8);
            lblMode.Size = new Size(500, 22);
            lblMode.ForeColor = Color.DarkSlateBlue;
            lblMode.Font = new Font(this.Font, FontStyle.Bold);
            lblMode.TextAlign = ContentAlignment.MiddleLeft;
            lblMode.AutoEllipsis = true;
            pTop.Controls.Add(lblMode);

            Label lblCat = new Label();
            lblCat.Text = "类别过滤:";
            lblCat.Location = new Point(10, 40);
            lblCat.Size = new Size(65, 23);
            lblCat.TextAlign = ContentAlignment.MiddleLeft;
            pTop.Controls.Add(lblCat);

            _cmbCategory = new ComboBox();
            _cmbCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCategory.Location = new Point(78, 38);
            _cmbCategory.Size = new Size(250, 25);
            _cmbCategory.SelectedIndexChanged += (s, e) =>
            {
                if (_isLoadingData) return;
                try { ApplyCategoryFilterOnly(); RefreshPreview(); }
                catch { }
            };
            pTop.Controls.Add(_cmbCategory);

            Button btnReload = new Button();
            btnReload.Text = "刷新列表";
            btnReload.Location = new Point(338, 36);
            btnReload.Size = new Size(88, 28);
            btnReload.Click += (s, e) => { try { _allItems = null; LoadData(); RefreshPreview(); } catch (Exception ex) { MessageBox.Show("刷新失败: " + ex.Message); } };
            pTop.Controls.Add(btnReload);
            MiniLog.Info("INIT:pTop:END");

            MiniLog.Info("INIT:pRules:START");
            Panel pRules = new Panel();
            pRules.Dock = DockStyle.Fill;
            pRules.Padding = new Padding(8, 5, 8, 5);
            pRules.BackColor = Color.FromArgb(248, 248, 248);

            const int GW = 245;
            const int GH = 192;
            const int CX1 = 10;
            const int CX2 = 262;
            const int CY1 = 8;
            const int CY2 = 194;

            GroupBox g1 = new GroupBox();
            g1.Text = "1. 前后缀";
            g1.Location = new Point(CX1, CY1);
            g1.Size = new Size(GW, GH);
            pRules.Controls.Add(g1);

            Label lp1 = new Label() { Text = "前缀:", Location = new Point(10, 28), Size = new Size(45, 22) };
            _txtPrefix = new TextBox() { Location = new Point(58, 26), Size = new Size(175, 23) };
            _txtPrefix.TextChanged += (s, e) => RefreshPreview();
            g1.Controls.Add(lp1); g1.Controls.Add(_txtPrefix);

            Label lp2 = new Label() { Text = "后缀:", Location = new Point(10, 58), Size = new Size(45, 22) };
            _txtSuffix = new TextBox() { Location = new Point(58, 56), Size = new Size(175, 23) };
            _txtSuffix.TextChanged += (s, e) => RefreshPreview();
            g1.Controls.Add(lp2); g1.Controls.Add(_txtSuffix);

            CheckBox chkCatP = new CheckBox();
            chkCatP.Text = "自动追加类别作前缀 (如 门_)";
            chkCatP.Location = new Point(10, 88);
            chkCatP.Size = new Size(225, 22);
            chkCatP.CheckedChanged += (s, e) => { _chkUseCategoryPrefix.Checked = chkCatP.Checked; RefreshPreview(); };
            _chkUseCategoryPrefix = chkCatP;
            g1.Controls.Add(_chkUseCategoryPrefix);

            Label lp3 = new Label() { Text = "末尾追加序号:", Location = new Point(10, 114), Size = new Size(90, 22) };
            _txtCounterPrefix = new TextBox() { Location = new Point(10, 136), Size = new Size(50, 23), Text = "_" };
            _numCounterStart = new NumericUpDown() { Location = new Point(65, 136), Size = new Size(50, 23), Minimum = 0, Maximum = 99999, Value = 1 };
            _numCounterDigits = new NumericUpDown() { Location = new Point(120, 136), Size = new Size(45, 23), Minimum = 0, Maximum = 10, Value = 0 };
            _chkEnableCounter = new CheckBox() { Text = "启用序号（不勾选=不添加）", Location = new Point(10, 162), Size = new Size(220, 20), Checked = false };
            _chkEnableCounter.CheckedChanged += (s, e) => RefreshPreview();
            _txtCounterPrefix.TextChanged += (s, e) => RefreshPreview();
            _numCounterStart.ValueChanged += (s, e) => RefreshPreview();
            _numCounterDigits.ValueChanged += (s, e) => RefreshPreview();
            Label lc2 = new Label() { Text = "起始", Location = new Point(65, 118), Size = new Size(50, 15), ForeColor = Color.DimGray };
            Label lc3 = new Label() { Text = "位数", Location = new Point(120, 118), Size = new Size(45, 15), ForeColor = Color.DimGray };
            g1.Controls.AddRange(new Control[] { lp3, _txtCounterPrefix, _numCounterStart, _numCounterDigits, _chkEnableCounter, lc2, lc3 });

            GroupBox g2 = new GroupBox();
            g2.Text = "2. 查找 / 替换（正则）";
            g2.Location = new Point(CX2, CY1);
            g2.Size = new Size(GW, GH);
            pRules.Controls.Add(g2);

            Label lf1 = new Label() { Text = "查找:", Location = new Point(10, 28), Size = new Size(50, 22) };
            _txtFind = new TextBox() { Location = new Point(62, 26), Size = new Size(170, 23) };
            _txtFind.TextChanged += (s, e) => RefreshPreview();
            g2.Controls.Add(lf1); g2.Controls.Add(_txtFind);

            Label lf2 = new Label() { Text = "替换为:", Location = new Point(10, 58), Size = new Size(50, 22) };
            _txtReplace = new TextBox() { Location = new Point(62, 56), Size = new Size(170, 23) };
            _txtReplace.TextChanged += (s, e) => RefreshPreview();
            g2.Controls.Add(lf2); g2.Controls.Add(_txtReplace);

            _chkIgnoreCase = new CheckBox() { Text = "忽略大小写", Location = new Point(10, 86), Size = new Size(100, 22) };
            _chkRegex = new CheckBox() { Text = "正则表达式", Location = new Point(115, 86), Size = new Size(120, 22) };
            _chkIgnoreCase.CheckedChanged += (s, e) => RefreshPreview();
            _chkRegex.CheckedChanged += (s, e) => RefreshPreview();
            g2.Controls.Add(_chkIgnoreCase); g2.Controls.Add(_chkRegex);

            Label lf3 = new Label() { Text = "例：_→- 或 空格→空   正则：\\s→空（去空格） 正则：[（(].*?[)）]→删括号内", Location = new Point(8, 114), Size = new Size(228, 54), ForeColor = Color.DimGray };
            g2.Controls.Add(lf3);

            GroupBox g3 = new GroupBox();
            g3.Text = "3. 插入 / 截断";
            g3.Location = new Point(CX1, CY2);
            g3.Size = new Size(GW, GH);
            pRules.Controls.Add(g3);

            Label li1 = new Label() { Text = "在第 N 位插入:", Location = new Point(10, 28), Size = new Size(88, 22) };
            _numInsertPos = new NumericUpDown() { Location = new Point(100, 26), Size = new Size(50, 23), Minimum = 0, Maximum = 999 };
            Label li2 = new Label() { Text = "插入文本:", Location = new Point(10, 58), Size = new Size(88, 22) };
            _txtInsertText = new TextBox() { Location = new Point(100, 56), Size = new Size(130, 23) };
            _numInsertPos.ValueChanged += (s, e) => RefreshPreview();
            _txtInsertText.TextChanged += (s, e) => RefreshPreview();
            g3.Controls.AddRange(new Control[] { li1, _numInsertPos, li2, _txtInsertText });

            Label lt1 = new Label() { Text = "截前 N 字:", Location = new Point(10, 88), Size = new Size(88, 22) };
            _numTrimStart = new NumericUpDown() { Location = new Point(100, 86), Size = new Size(50, 23), Minimum = 0, Maximum = 999 };
            Label lt2 = new Label() { Text = "截后 N 字:", Location = new Point(10, 118), Size = new Size(88, 22) };
            _numTrimEnd = new NumericUpDown() { Location = new Point(100, 116), Size = new Size(50, 23), Minimum = 0, Maximum = 999 };
            _numTrimStart.ValueChanged += (s, e) => RefreshPreview();
            _numTrimEnd.ValueChanged += (s, e) => RefreshPreview();
            g3.Controls.AddRange(new Control[] { lt1, _numTrimStart, lt2, _numTrimEnd });

            Label li3 = new Label() { Text = "注：先插入/截断，再应用前后缀与查找替换。", Location = new Point(8, 148), Size = new Size(228, 24), ForeColor = Color.DimGray };
            g3.Controls.Add(li3);

            GroupBox g4 = new GroupBox();
            g4.Text = "4. 快速预设（深圳报建）";
            g4.Location = new Point(CX2, CY2);
            g4.Size = new Size(GW, GH);
            pRules.Controls.Add(g4);

            int by = 24;
            string[] presets = new string[]
            {
                "预设1: 前缀[建筑-] + 下划线改短横",
                "预设2: 前缀[结构-] + 后缀[-A版]",
                "预设3: 前缀[机电-] + 去所有空格",
                "预设4: 类别前缀(门_/窗_) + 序号3位",
                "预设5: 仅加类别前缀（不加序号）",
                "预设6: 清空所有规则"
            };
            foreach (var p in presets)
            {
                Button bp = new Button();
                bp.Text = p;
                bp.Location = new Point(8, by);
                bp.Size = new Size(228, 24);
                bp.TextAlign = ContentAlignment.MiddleLeft;
                bp.Font = new Font(this.Font.FontFamily, 8.2f, FontStyle.Regular);
                bp.Click += (s, e) => ApplyPreset(p);
                g4.Controls.Add(bp);
                by += 28;
            }
            MiniLog.Info("INIT:pRules:END");

            MiniLog.Info("INIT:lblPreview:START");
            Label lblPreview = new Label();
            lblPreview.Text = "使用提示：（操作见下方说明，预览结果请查看右侧列表）";
            lblPreview.Dock = DockStyle.Fill;
            lblPreview.Padding = new Padding(12, 4, 10, 0);
            lblPreview.Font = new Font(this.Font.FontFamily, 8.5f, FontStyle.Bold);
            lblPreview.ForeColor = Color.SteelBlue;
            lblPreview.TextAlign = ContentAlignment.MiddleLeft;
            MiniLog.Info("INIT:lblPreview:END");

            MiniLog.Info("INIT:_dgvPreview:START");
            _dgvPreview = new DataGridView();
            _dgvPreview.Dock = DockStyle.Fill;
            _dgvPreview.AllowUserToAddRows = false;
            _dgvPreview.AllowUserToDeleteRows = true;
            _dgvPreview.AllowUserToResizeRows = false;
            _dgvPreview.RowHeadersVisible = false;
            _dgvPreview.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvPreview.MultiSelect = true;
            _dgvPreview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _dgvPreview.BackgroundColor = Color.White;
            _dgvPreview.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _dgvPreview.CellValueChanged += DgvPreview_CellValueChanged;
            _dgvPreview.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    var col = _dgvPreview.Columns[e.ColumnIndex];
                    if (col.Name == "NewName" || col.Name == "ParentNewName")
                    {
                        _dgvPreview.BeginEdit(true);
                    }
                }
            };
            _dgvPreview.CellEndEdit += DgvPreview_CellEndEdit;
            _dgvPreview.CellContentClick += DgvPreview_CellContentClick;
            _dgvPreview.CellPainting += DgvPreview_CellPainting;
            _dgvPreview.ColumnHeaderMouseClick += DgvPreview_ColumnHeaderMouseClick;
            MiniLog.Info("INIT:_dgvPreview:PROPS-DONE");
            MiniLog.Info("INIT:cms:START");
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Items.Add("恢复选中行为原名称", null, (s, e) => {
                try
                {
                    foreach (DataGridViewRow r in _dgvPreview.SelectedRows)
                    {
                        var it = r.DataBoundItem as RenameItem;
                        if (it != null) { it.NewName = it.OriginalName; it.Remark = ""; }
                    }
                    RebindGrid();
                }
                catch (Exception ex) { MiniLog.Error("CMS-恢复", ex); }
            });
            cms.Items.Add("从列表移除选中行", null, (s, e) => {
                try
                {
                    var remove = new List<RenameItem>();
                    foreach (DataGridViewRow r in _dgvPreview.SelectedRows)
                    {
                        var it = r.DataBoundItem as RenameItem;
                        if (it != null) remove.Add(it);
                    }
                    foreach (var r in remove) _items.Remove(r);
                    RebindGrid();
                }
                catch (Exception ex) { MiniLog.Error("CMS-移除", ex); }
            });
            cms.Items.Add("撤销移除（重新载入列表）", null, (s, e) => { try { LoadData(); RefreshPreview(); } catch (Exception ex) { MiniLog.Error("CMS-撤销移除", ex); } });
            _dgvPreview.ContextMenuStrip = cms;
            MiniLog.Info("INIT:cms:END");
            MiniLog.Info("INIT:_dgvPreview:END");

            MiniLog.Info("INIT:pBottom:START");
            // ======================== 底部按钮 ========================
            Panel pBottom = new Panel();
            pBottom.Dock = DockStyle.Fill;
            pBottom.Padding = new Padding(10, 10, 10, 10);
            Label lblStat = new Label() { Location = new Point(15, 15), Size = new Size(430, 28), Name = "lblStat", ForeColor = Color.DimGray };
            pBottom.Controls.Add(lblStat);
            Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(900, 12), Size = new Size(100, 32) };
            Button btnOK = new Button() { Text = "应用到项目", DialogResult = DialogResult.OK, Location = new Point(1010, 12), Size = new Size(115, 32) };
            btnOK.Click += (s, e) => { try { CollectResult(); } catch (Exception ex) { MiniLog.Error("OK-Click-CollectResult", ex); } };
            pBottom.Controls.Add(btnCancel); pBottom.Controls.Add(btnOK);
            this.AcceptButton = btnOK; this.CancelButton = btnCancel;
            MiniLog.Info("INIT:pBottom:END");

            MiniLog.Info("INIT:InitGridColumns:START");
            InitGridColumns();
            MiniLog.Info("INIT:InitGridColumns:END");

            _pTop = pTop;
            _pRules = pRules;
            _lblPreview = lblPreview;
            _pBottom = pBottom;
            MiniLog.Info("INIT:SAVE-REFS done");

            MiniLog.Info("INIT:TLP:START-MASTER-LR-LAYOUT");
            _tlpMain = new TableLayoutPanel();
            _tlpMain.Dock = DockStyle.Fill;
            _tlpMain.ColumnCount = 2;
            _tlpMain.RowCount = 2;
            _tlpMain.ColumnStyles.Clear();
            _tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 540F));
            _tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tlpMain.RowStyles.Clear();
            _tlpMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tlpMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 55F));
            _tlpMain.Padding = new Padding(0);

            TableLayoutPanel tlpLeft = new TableLayoutPanel();
            tlpLeft.Dock = DockStyle.Fill;
            tlpLeft.ColumnCount = 1;
            tlpLeft.RowCount = 4;
            tlpLeft.ColumnStyles.Clear();
            tlpLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tlpLeft.RowStyles.Clear();
            tlpLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            tlpLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 390F));
            tlpLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            tlpLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tlpLeft.Padding = new Padding(0);
            tlpLeft.Controls.Add(pTop, 0, 0);
            tlpLeft.Controls.Add(pRules, 0, 1);
            tlpLeft.Controls.Add(lblPreview, 0, 2);
            Panel pUsage = new Panel();
            pUsage.Dock = DockStyle.Fill;
            pUsage.BackColor = Color.WhiteSmoke;
            pUsage.Padding = new Padding(14, 8, 14, 8);
            Label lblUsage = new Label();
            lblUsage.Dock = DockStyle.Fill;
            lblUsage.Font = new Font(this.Font.FontFamily, 8.5f, FontStyle.Regular);
            lblUsage.ForeColor = Color.DimGray;
            lblUsage.Text = "操作说明：\r\n" +
                           "1. 在上方 4 个区域设置重命名规则，按优先级：插入/截断 → 前后缀/类别前缀 → 查找替换 → 末尾序号；\r\n" +
                           "2. 右侧预览列表会实时显示计算结果，[原名称] 保持不变，[新名称] 可双击修改单行；\r\n" +
                           "3. 右键列表支持「从列表移除选中行」或「恢复选中行为原名称」；\r\n" +
                           "4. 点击底部「应用到项目」会开启 1 个事务，把所有 [新名称 ≠ 原名称] 的族一次性写入。\r\n\r\n" +
                           "快捷操作：\r\n" +
                           "· 推荐直接按【4.快速预设】的深圳报建模板一键填入，再手动微调；\r\n" +
                           "· 重名时自动加 _dup1、_dup2… 后缀，避免 Revit 报错；\r\n" +
                           "· 可先用「类别过滤」筛到只看某几类，减少误改范围。";
            lblUsage.TextAlign = ContentAlignment.TopLeft;
            pUsage.Controls.Add(lblUsage);
            tlpLeft.Controls.Add(pUsage, 0, 3);

            _tlpMain.Controls.Add(tlpLeft, 0, 0);
            _tlpMain.Controls.Add(_dgvPreview, 1, 0);
            _tlpMain.Controls.Add(pBottom, 0, 1);
            _tlpMain.SetColumnSpan(pBottom, 2);
            MiniLog.Info("INIT:TLP:MASTER ROWS=" + _tlpMain.RowCount + " COLS=" + _tlpMain.ColumnCount);
            this.Controls.Add(_tlpMain);
            MiniLog.Info("INIT:TLP+ADD done");
            MiniLog.Info("INIT:ALL-DONE");
        }

        private void InitGridColumns()
        {
            MiniLog.Info("InitGridColumns:AutoGenerate=true");
            _dgvPreview.AutoGenerateColumns = true;
            _dgvPreview.ColumnHeadersVisible = true;
            _dgvPreview.ColumnHeadersHeight = 28;
            _dgvPreview.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _dgvPreview.EnableHeadersVisualStyles = true;
            _dgvPreview.RowHeadersVisible = false;
            _bindingSource = new BindingSource();
            MiniLog.Info("InitGridColumns:BindingSource+AutoGen+ColumnHeadersFixed");
        }

        private void LoadData()
        {
            if (_isLoadingData) { MiniLog.Info("LoadData:SKIP (reentry guard)"); return; }
            _isLoadingData = true;
            try
            {
                LoadDataImpl();
            }
            finally
            {
                _isLoadingData = false;
                MiniLog.Info("LoadData:REENTRY-GUARD-RESET");
            }
            MiniLog.Info("LoadData:END");
        }

        private void ApplyCategoryFilterOnly()
        {
            if (_allItems == null || _allItems.Count == 0) return;
            MiniLog.Info("ApplyCategoryFilterOnly:START allItems.Count=" + _allItems.Count + " SelectedIndex=" + _cmbCategory.SelectedIndex);
            List<RenameItem> filtered;
            if (_cmbCategory.SelectedIndex > 0 && _cmbCategory.SelectedItem != null)
            {
                string catF = _cmbCategory.SelectedItem.ToString() ?? "";
                filtered = _allItems.Where(i => i.CategoryName == catF).ToList();
                MiniLog.Info("ApplyCategoryFilterOnly: -> " + catF + " count=" + filtered.Count);
            }
            else
            {
                filtered = _allItems.ToList();
                MiniLog.Info("ApplyCategoryFilterOnly: ALL (idx<=0 or null) count=" + filtered.Count);
            }
            _items = filtered.OrderBy(i => i.CategoryName).ThenBy(i => i.OriginalName).ToList();
        }

        private void LoadDataImpl()
        {
            Target = RenameTarget.FamilySymbolName;

            var categories = new SortedSet<string>();
            var items = new List<RenameItem>();

            if (_allItems == null)
            {
                MiniLog.Info("LoadData:STEP1 collect FamilySymbols (族类型子集)");
                Dictionary<ElementId, Family> familyCache = new Dictionary<ElementId, Family>();
                int processed = 0, skipped = 0;
                try
                {
                    using (var collSym = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)))
                    {
                        foreach (Element sym in collSym)
                        {
                            try
                            {
                                FamilySymbol fs = sym as FamilySymbol;
                                if (fs == null) { skipped++; continue; }
                                Family fam = null;
                                try { fam = fs.Family; } catch { fam = null; }
                                if (fam == null) { skipped++; continue; }
                                if (!familyCache.ContainsKey(fam.Id)) familyCache[fam.Id] = fam;

                                string catName = null;
                                try { catName = fam.FamilyCategory?.Name; } catch { }
                                if (string.IsNullOrEmpty(catName)) catName = "(不可读族类别)";

                                string symName = null;
                                try { symName = fs.Name; } catch { symName = null; }
                                if (string.IsNullOrEmpty(symName)) { skipped++; continue; }

                                string famName = null;
                                try { famName = fam.Name; } catch { famName = null; }
                                if (string.IsNullOrEmpty(famName)) famName = "(无名族)";

                                categories.Add(catName);
                                items.Add(new RenameItem
                                {
                                    Id = fs.Id,
                                    ParentId = fam.Id,
                                    Kind = "FamilySymbol",
                                    CategoryName = catName,
                                    ParentName = famName,
                                    ParentNewName = famName,
                                    OriginalName = symName,
                                    NewName = symName,
                                    Selected = true
                                });
                                processed++;
                            }
                            catch
                            {
                                skipped++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MiniLog.Error("LoadData:STEP1", ex);
                }
                MiniLog.Info("LoadData:STEP1 familyCache=" + familyCache.Count + " symItems=" + items.Count + " skipped=" + skipped);

                // STEP1b: 单独扫描 Family 补全类别，并为无 Symbol 的族生成可改父族名的占位行
                try
                {
                    using (var collFam = new FilteredElementCollector(_doc).OfClass(typeof(Family)))
                    {
                        foreach (Element e in collFam)
                        {
                            Family fam = e as Family;
                            if (fam == null) continue;
                            string catName = null;
                            try { catName = fam.FamilyCategory?.Name; } catch { }
                            if (string.IsNullOrEmpty(catName)) catName = "(不可读族类别)";
                            categories.Add(catName);

                            if (!familyCache.ContainsKey(fam.Id))
                            {
                                string famName = null;
                                try { famName = fam.Name; } catch { }
                                if (string.IsNullOrEmpty(famName)) famName = "(无名族)";
                                familyCache[fam.Id] = fam;

                                // 如果该 Family 没有任何 Symbol 被前面扫到，添加一个占位行以便修改父族名
                                bool hasSymbol = items.Any(i => i.ParentId != null && i.ParentId.Equals(fam.Id));
                                if (!hasSymbol)
                                {
                                    items.Add(new RenameItem
                                    {
                                        Id = fam.Id,
                                        ParentId = fam.Id,
                                        Kind = "Family",
                                        CategoryName = catName,
                                        ParentName = famName,
                                        ParentNewName = famName,
                                        OriginalName = "(父族)" + famName,
                                        NewName = "(父族)" + famName,
                                        Selected = true
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MiniLog.Error("LoadData:STEP1b", ex);
                }

                // STEP1c: 扫描系统族类型（墙体、楼板、屋顶、天花板等）
                try
                {
                    AddSystemTypeItems<WallType>(_doc, items, categories, "墙体");
                    AddSystemTypeItems<FloorType>(_doc, items, categories, "楼板");
                    AddSystemTypeItems<RoofType>(_doc, items, categories, "屋顶");
                    AddSystemTypeItems<CeilingType>(_doc, items, categories, "天花板");
                }
                catch (Exception ex)
                {
                    MiniLog.Error("LoadData:STEP1c-SystemTypes", ex);
                }

                _allItems = items.OrderBy(i => i.CategoryName).ThenBy(i => i.ParentName).ThenBy(i => i.OriginalName).ToList();
            }
            else
            {
                MiniLog.Info("LoadData:USE-CACHE _allItems.Count=" + _allItems.Count);
                items = _allItems.ToList();
                foreach (var it in _allItems) categories.Add(it.CategoryName);
            }

            // 额外从所有 Family 再补一次类别，确保即使某些族没有 Symbol 也能出现在下拉
            try
            {
                using (var allFam = new FilteredElementCollector(_doc).OfClass(typeof(Family)))
                {
                    foreach (Family fam in allFam.Cast<Family>())
                    {
                        string catName = null;
                        try { catName = fam.FamilyCategory?.Name; } catch { }
                        if (!string.IsNullOrEmpty(catName)) categories.Add(catName);
                    }
                }
            }
            catch (Exception ex) { MiniLog.Error("LoadData:EXTRA-CATEGORIES", ex); }

            MiniLog.Info("LoadData:STEP3 bind category dropdown count=" + categories.Count);
            _cmbCategory.BeginUpdate();
            _cmbCategory.Items.Clear();
            _cmbCategory.Items.Add("全部类别");
            foreach (var c in categories.OrderBy(c => c)) _cmbCategory.Items.Add(c);
            _cmbCategory.EndUpdate();
            int idx = 0;
            if (_cmbCategory.Items.Count > 0) _cmbCategory.SelectedIndex = idx;
            MiniLog.Info("LoadData:STEP3 SelectedIndex=" + _cmbCategory.SelectedIndex + " Text=" + (_cmbCategory.SelectedItem?.ToString() ?? "NULL") + " ItemsCount=" + _cmbCategory.Items.Count);

            ApplyCategoryFilterOnly();
            MiniLog.Info("LoadData:FINAL items.Count=" + _items.Count);
        }

        private void ApplyPreset(string preset)
        {
            // 先把输入控件重置
            _txtPrefix.Text = ""; _txtSuffix.Text = "";
            _txtFind.Text = ""; _txtReplace.Text = "";
            _chkIgnoreCase.Checked = false; _chkRegex.Checked = false;
            _numInsertPos.Value = 0; _txtInsertText.Text = "";
            _numTrimStart.Value = 0; _numTrimEnd.Value = 0;
            _chkUseCategoryPrefix.Checked = false;
            _chkEnableCounter.Checked = false; // 重置：默认不启用序号
            _numCounterDigits.Value = 0; _numCounterStart.Value = 1; _txtCounterPrefix.Text = "_";

            if (preset.StartsWith("预设1"))
            {
                _txtPrefix.Text = "建筑-";
                _txtFind.Text = "_"; _txtReplace.Text = "-";
            }
            else if (preset.StartsWith("预设2"))
            {
                _txtPrefix.Text = "结构-";
                _txtSuffix.Text = "-A版";
            }
            else if (preset.StartsWith("预设3"))
            {
                _txtPrefix.Text = "机电-";
                _txtFind.Text = @"\s"; _txtReplace.Text = "";
                _chkRegex.Checked = true;
            }
            else if (preset.StartsWith("预设4"))
            {
                _chkUseCategoryPrefix.Checked = true;
                _chkEnableCounter.Checked = true; // 启用序号
                _txtCounterPrefix.Text = "_";
                _numCounterStart.Value = 1;
                _numCounterDigits.Value = 3;
            }
            else if (preset.StartsWith("预设5"))
            {
                // 仅加类别前缀，不加任何序号后缀
                _chkUseCategoryPrefix.Checked = true;
                _chkEnableCounter.Checked = false; // 明确不启用序号
                _txtPrefix.Text = "";
                _txtSuffix.Text = "";
                _numCounterDigits.Value = 0;
            }
            else if (preset.StartsWith("预设6"))
            {
                // 清空所有规则（含关闭序号）
                _chkEnableCounter.Checked = false;
                _numCounterDigits.Value = 0;
            }
            RefreshPreview();
        }

        private string ApplyAllRules(string original, int counterIndex, string categoryName)
        {
            string s = original ?? "";

            // A. 先截前后（避免插入后位置变）
            int ts = (int)_numTrimStart.Value;
            int te = (int)_numTrimEnd.Value;
            if (ts > 0) s = ts < s.Length ? s.Substring(ts) : "";
            if (te > 0) s = te < s.Length ? s.Substring(0, s.Length - te) : "";

            // B. 插入
            int pos = (int)_numInsertPos.Value;
            string insertT = _txtInsertText?.Text ?? "";
            if (insertT.Length > 0)
            {
                pos = Math.Min(Math.Max(0, pos), s.Length);
                s = s.Insert(pos, insertT);
            }

            // C. 类别前缀
            if (_chkUseCategoryPrefix.Checked && !string.IsNullOrEmpty(categoryName))
            {
                string prefix = categoryName.Trim() + "_";
                if (!s.StartsWith(prefix, StringComparison.Ordinal))
                    s = prefix + s;
            }

            // D. 查找替换（正则/普通）
            string f = _txtFind?.Text ?? "";
            string r = _txtReplace?.Text ?? "";
            if (f.Length > 0)
            {
                try
                {
                    if (_chkRegex.Checked)
                    {
                        RegexOptions opt = _chkIgnoreCase.Checked ? RegexOptions.IgnoreCase : RegexOptions.None;
                        s = Regex.Replace(s, f, r, opt);
                    }
                    else
                    {
                        StringComparison sc = _chkIgnoreCase.Checked ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        s = ReplaceCaseSensitive(s, f, r, sc);
                    }
                }
                catch { /* ignore regex errors */ }
            }

            // E. 前后缀
            string pre = _txtPrefix?.Text ?? "";
            string suf = _txtSuffix?.Text ?? "";
            if (pre.Length > 0 && !s.StartsWith(pre, StringComparison.Ordinal)) s = pre + s;
            if (suf.Length > 0 && !s.EndsWith(suf, StringComparison.Ordinal)) s = s + suf;

            // F. 序号（必须勾选【启用序号】才生效）
            if (_chkEnableCounter != null && _chkEnableCounter.Checked && _numCounterDigits.Value > 0 && counterIndex >= 0)
            {
                string sep = _txtCounterPrefix?.Text ?? "";
                int start = (int)_numCounterStart.Value;
                int digits = (int)_numCounterDigits.Value;
                string num = (start + counterIndex).ToString().PadLeft(digits, '0');
                s = s + sep + num;
            }

            return s;
        }

        private static string ReplaceCaseSensitive(string s, string f, string r, StringComparison sc)
        {
            if (string.IsNullOrEmpty(f)) return s;
            int idx = 0;
            while ((idx = s.IndexOf(f, idx, sc)) >= 0)
            {
                s = s.Remove(idx, f.Length).Insert(idx, r);
                idx += r.Length;
                if (idx > s.Length) break;
            }
            return s;
        }

        private void RefreshPreview()
        {
            if (_items == null) return;
            Dictionary<string, int> newNames = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> countersByKind = new Dictionary<string, int>();
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                string catKey = it.Kind + "|" + it.CategoryName;
                int idx;
                if (!countersByKind.TryGetValue(catKey, out idx)) idx = 0;
                countersByKind[catKey] = idx + 1;

                // 如果用户手动修改过（NewName != OriginalName 且备注里有手动标记），跳过自动规则
                bool manual = !string.IsNullOrEmpty(it.Remark) && it.Remark.StartsWith("手动");
                string computed;
                if (manual)
                {
                    computed = it.NewName ?? it.OriginalName;
                }
                else
                {
                    computed = ApplyAllRules(it.OriginalName, idx, it.CategoryName);
                }

                // 空校验
                if (string.IsNullOrWhiteSpace(computed))
                {
                    it.NewName = it.OriginalName;
                    it.Remark = "计算结果为空";
                    continue;
                }

                // 重复检查
                string uniqueKey = it.Kind + "|" + computed;
                int dupCount;
                if (newNames.TryGetValue(uniqueKey, out dupCount))
                {
                    dupCount++;
                    newNames[uniqueKey] = dupCount;
                    computed = computed + "_dup" + dupCount;
                    it.Remark = "已自动去重(dup" + dupCount + ")";
                }
                else
                {
                    newNames[uniqueKey] = 0;
                    if (!manual) it.Remark = computed == it.OriginalName ? "不变" : "";
                }
                it.NewName = computed;
            }
            RebindGrid();
            UpdateStats();
        }

        private void RebindGrid()
        {
            _isRefreshingPreview = true;
            try
            {
                _dgvPreview.ColumnHeadersVisible = true;
                _dgvPreview.ColumnHeadersHeight = 28;
                _dgvPreview.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                _dgvPreview.EnableHeadersVisualStyles = true;
                _dgvPreview.RowHeadersVisible = false;
                _dgvPreview.AutoGenerateColumns = false;
                _dgvPreview.MultiSelect = true;
                _dgvPreview.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                _dgvPreview.Columns.Clear();

                if (_bindingSource == null) _bindingSource = new BindingSource();
                _bindingSource.DataSource = _items;
                _dgvPreview.DataSource = _bindingSource;

                Action<DataGridViewColumn, string, string, bool, int, bool, DataGridViewAutoSizeColumnMode> addCol = (c, hdr, dp, ro, width, visible, mode) =>
                {
                    c.HeaderText = hdr;
                    c.DataPropertyName = dp;
                    c.ReadOnly = ro;
                    c.Width = width;
                    c.AutoSizeMode = mode;
                    c.MinimumWidth = 40;
                    c.Visible = visible;
                    _dgvPreview.Columns.Add(c);
                };

                var fill = DataGridViewAutoSizeColumnMode.Fill;
                var none = DataGridViewAutoSizeColumnMode.None;

                var chkCol = new DataGridViewCheckBoxColumn();
                chkCol.ReadOnly = true; // 完全由 CellContentClick 控制，避免与默认行为冲突
                addCol(chkCol, "选择", "Selected", true, 55, true, none);
                addCol(new DataGridViewTextBoxColumn(), "Id", "Id", true, 0, false, none);
                addCol(new DataGridViewTextBoxColumn(), "ParentId", "ParentId", true, 0, false, none);
                addCol(new DataGridViewTextBoxColumn(), "Kind", "Kind", true, 0, false, none);
                addCol(new DataGridViewTextBoxColumn(), "类别", "CategoryName", true, 90, true, none);
                addCol(new DataGridViewTextBoxColumn(), "父族", "ParentName", false, 110, true, none);
                addCol(new DataGridViewTextBoxColumn(), "父族新名", "ParentNewName", false, 110, true, none);
                addCol(new DataGridViewTextBoxColumn(), "原名称（子类型）", "OriginalName", true, 170, true, fill);
                addCol(new DataGridViewTextBoxColumn(), "新名称（可双击编辑）", "NewName", false, 170, true, fill);
                addCol(new DataGridViewTextBoxColumn(), "备注", "Remark", true, 90, true, none);

                _dgvPreview.CurrentCellDirtyStateChanged += (s, e) =>
                {
                    if (_dgvPreview.IsCurrentCellDirty && _dgvPreview.CurrentCell.OwningColumn is DataGridViewCheckBoxColumn)
                        _dgvPreview.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };

                MiniLog.Info("RebindGrid:manual Columns=" + _dgvPreview.Columns.Count + " Rows=" + _dgvPreview.Rows.Count);
                _dgvPreview.Invalidate();
                _dgvPreview.Update();
                if (_dgvPreview.RowCount > 0)
                {
                    try { _dgvPreview.FirstDisplayedScrollingRowIndex = 0; } catch { }
                }
            }
            finally
            {
                _isRefreshingPreview = false;
            }
        }

        private void UpdateStats()
        {
            int total = _items.Count;
            int selected = _items.Count(i => i.Selected);
            int changed = _items.Count(i => i.Selected && i.NewName != i.OriginalName);
            int parentChanged = _items.Count(i => i.Selected && i.ParentNewName != i.ParentName);
            int dup = _items.Count(i => i.Selected && !string.IsNullOrEmpty(i.Remark) && i.Remark.Contains("dup"));
            Label lbl = this.Controls.Find("lblStat", true).FirstOrDefault() as Label;
            if (lbl != null)
                lbl.Text = $"共 {total} 条，已选 {selected} 条，子类型将修改 {changed} 条，父族将修改 {parentChanged} 条，_dup {dup}。";
        }

        private void DgvPreview_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_isRefreshingPreview) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = _dgvPreview.Columns[e.ColumnIndex];
            var it = _dgvPreview.Rows[e.RowIndex].DataBoundItem as RenameItem;
            if (it == null) return;
            if (col.Name == "NewName" || col.Name == "ParentNewName")
            {
                if (!string.IsNullOrEmpty(it.Remark) && !it.Remark.StartsWith("手动"))
                    it.Remark = "手动";
                else if (string.IsNullOrEmpty(it.Remark))
                    it.Remark = "手动";
                if (col.Name == "NewName")
                {
                    var same = _items.Where(x => x != it && x.Kind == it.Kind && x.NewName == it.NewName).ToList();
                    if (same.Count > 0)
                    {
                        if (it.Remark.StartsWith("手动"))
                            it.Remark += " / 冲突";
                        else it.Remark = "冲突";
                    }
                }
                UpdateStats();
            }
            else if (col.Name == "Selected")
            {
                UpdateStats();
            }
        }

        private void CollectResult()
        {
            ItemsToApply = _items
                .Where(i => i.Selected && ((!string.IsNullOrEmpty(i.NewName) && i.NewName != i.OriginalName) || (!string.IsNullOrEmpty(i.ParentNewName) && i.ParentNewName != i.ParentName)))
                .ToList();
            if (ItemsToApply.Count == 0)
            {
                if (MessageBox.Show("预览列表中没有勾选且需要修改的项。\n\n是否仍然关闭窗口（不应用）？", "没有修改项",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                {
                    this.DialogResult = DialogResult.None;
                }
                return;
            }
            int typeCount = ItemsToApply.Count(i => i.Kind == "FamilySymbol" && i.NewName != i.OriginalName);
            int parentCount = ItemsToApply.Count(i => i.ParentNewName != i.ParentName);
            string msg = $"共 {ItemsToApply.Count} 条待修改（总 {_items.Count} 条预览）：子类型 {typeCount} 条，父族 {parentCount} 条。\n\n将用一个大事务一次性写入。是否继续？";
            if (MessageBox.Show(msg, "确认应用", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                this.DialogResult = DialogResult.None;
            }
        }

        // 通用方法：添加系统族类型到列表
        private void AddSystemTypeItems<T>(Document doc, List<RenameItem> items, SortedSet<string> categories, string defaultCategory) where T : ElementType
        {
            try
            {
                using (var coll = new FilteredElementCollector(doc).OfClass(typeof(T)))
                {
                    foreach (ElementType et in coll.Cast<ElementType>())
                    {
                        if (et == null) continue;
                        string catName = null;
                        try { catName = et.Category?.Name; } catch { }
                        if (string.IsNullOrEmpty(catName)) catName = defaultCategory;
                        categories.Add(catName);
                        string typeName = null;
                        try { typeName = et.Name; } catch { }
                        if (string.IsNullOrEmpty(typeName)) typeName = "(无名系统类型)";
                        items.Add(new RenameItem
                        {
                            Id = et.Id,
                            ParentId = et.Id,
                            Kind = "SystemType",
                            CategoryName = catName,
                            ParentName = catName,
                            ParentNewName = catName,
                            OriginalName = typeName,
                            NewName = typeName,
                            Selected = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MiniLog.Error("AddSystemTypeItems<" + typeof(T).Name + ">", ex);
            }
        }

        // 父族新名同步：编辑完成后，把同 ParentId 的所有子类型的 ParentNewName 统一
        private void DgvPreview_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = _dgvPreview.Columns[e.ColumnIndex];
            var it = _dgvPreview.Rows[e.RowIndex].DataBoundItem as RenameItem;
            if (it == null) return;
            if (col.Name == "ParentNewName")
            {
                string newVal = it.ParentNewName ?? "";
                foreach (var same in _items.Where(x => x.ParentId != null && x.ParentId.Equals(it.ParentId)))
                    same.ParentNewName = newVal;
                RebindGrid();
            }
        }

        // 复选框支持 Shift/Ctrl 批量选中（完全由代码控制，避免与默认切换冲突）
        private void DgvPreview_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = _dgvPreview.Columns[e.ColumnIndex];
            if (col.Name != "Selected") return;
            var row = _dgvPreview.Rows[e.RowIndex];
            var it = row.DataBoundItem as RenameItem;
            if (it == null) return;

            bool newVal = !it.Selected;

            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && _lastCheckIndex >= 0)
            {
                int min = Math.Min(_lastCheckIndex, e.RowIndex);
                int max = Math.Max(_lastCheckIndex, e.RowIndex);
                for (int i = min; i <= max; i++)
                {
                    var r = _dgvPreview.Rows[i];
                    var item = r.DataBoundItem as RenameItem;
                    if (item != null) item.Selected = newVal;
                }
            }
            else if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                it.Selected = newVal;
            }
            else
            {
                it.Selected = newVal;
            }
            _lastCheckIndex = e.RowIndex;
            _dgvPreview.Refresh();
            UpdateStats();
        }

        // 表头绘制全选复选框
        private void DgvPreview_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                var col = _dgvPreview.Columns[e.ColumnIndex];
                if (col.Name == "Selected")
                {
                    e.PaintBackground(e.CellBounds, true);
                    int cbSize = 14;
                    int x = e.CellBounds.X + (e.CellBounds.Width - cbSize) / 2;
                    int y = e.CellBounds.Y + (e.CellBounds.Height - cbSize) / 2;
                    CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(x, y), _headerCheckboxChecked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
                    e.Handled = true;
                }
            }
        }

        // 表头点击全选/全不选
        private void DgvPreview_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            var col = _dgvPreview.Columns[e.ColumnIndex];
            if (col.Name != "Selected") return;
            _headerCheckboxChecked = !_headerCheckboxChecked;
            foreach (var it in _items) it.Selected = _headerCheckboxChecked;
            RebindGrid();
            UpdateStats();
            _dgvPreview.InvalidateColumn(e.ColumnIndex);
        }
    }
}
