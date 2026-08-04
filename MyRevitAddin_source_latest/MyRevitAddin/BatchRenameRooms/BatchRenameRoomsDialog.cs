using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace MyRevitAddin.BatchRenameRooms
{
    // ============================================================
    // 数据模型
    // ============================================================
    public class RoomRenameItem
    {
        public ElementId RoomId { get; set; }
        public string LevelName { get; set; }
        public string Number { get; set; }        // 房间编号
        public double Area { get; set; }          // 面积（平方米）
        public string OriginalName { get; set; }  // 原名称
        public string NewName { get; set; }       // 预览新名称
        public string Remark { get; set; }        // 备注：手动 / 不变 / 去重
        public bool Selected { get; set; } = true;
    }

    // ============================================================
    // 规则配置
    // ============================================================
    public class RoomRenameRules
    {
        public bool EnablePrefix { get; set; }
        public string Prefix { get; set; } = "";

        public bool EnableSuffix { get; set; }
        public string Suffix { get; set; } = "";

        public bool EnableFindReplace { get; set; }
        public string Find { get; set; } = "";
        public string Replace { get; set; } = "";
        public bool IgnoreCase { get; set; } = true;
        public bool UseRegex { get; set; }

        public bool EnableCounter { get; set; }
        public string CounterPrefix { get; set; } = "-";
        public int CounterStart { get; set; } = 1;
        public int CounterDigits { get; set; } = 2;
        public CounterPosition CounterPos { get; set; } = CounterPosition.Suffix;

        public bool EnableTrim { get; set; }
        public int TrimStart { get; set; }
        public int TrimEnd { get; set; }
    }

    public enum CounterPosition
    {
        Prefix = 0,
        Suffix = 1
    }

    // ============================================================
    // 对话框
    // ============================================================
    public partial class BatchRenameRoomsDialog : Form
    {
        private readonly Document _doc;
        private List<RoomRenameItem> _allItems = new List<RoomRenameItem>();
        private BindingList<RoomRenameItem> _viewItems = new BindingList<RoomRenameItem>();
        private bool _suppressEvent;
        private int _lastCheckIndex = -1;

        // UI
        private ComboBox _cmbLevel;
        private CheckBox _chkSkipEmpty;
        private DataGridView _dgv;

        private CheckBox _chkPrefix;
        private TextBox _txtPrefix;

        private CheckBox _chkSuffix;
        private TextBox _txtSuffix;

        private CheckBox _chkFind;
        private TextBox _txtFind;
        private TextBox _txtReplace;
        private CheckBox _chkIgnoreCase;
        private CheckBox _chkRegex;

        private CheckBox _chkTrim;
        private NumericUpDown _numTrimStart;
        private NumericUpDown _numTrimEnd;

        private CheckBox _chkCounter;
        private ComboBox _cmbCounterPos;
        private TextBox _txtCounterPrefix;
        private NumericUpDown _numCounterStart;
        private NumericUpDown _numCounterDigits;

        private Button _btnPreview;
        private Button _btnSelectAll;
        private Button _btnSelectNone;
        private Button _btnApply;
        private Button _btnCancel;
        private Label _lblStat;

        public List<RoomRenameItem> ItemsToApply { get; private set; } = new List<RoomRenameItem>();

        public BatchRenameRoomsDialog(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            Load += (s, e) => OnLoadData();
        }

        // ============================================================
        // 初始化 UI
        // ============================================================
        private void InitializeComponent()
        {
            Text = "批量修改房间名称";
            Size = new Size(1100, 720);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            // ===== 顶部：楼层筛选 =====
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(235, 240, 248) };
            var lblLevel = new Label { Text = "楼层筛选：", AutoSize = true, Location = new Point(12, 15) };
            _cmbLevel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 11),
                Width = 240
            };
            _cmbLevel.SelectedIndexChanged += (s, e) => ApplyLevelFilter();
            _chkSkipEmpty = new CheckBox
            {
                Text = "跳过空名房间（Area=0 或无名称）",
                Checked = true,
                AutoSize = true,
                Location = new Point(340, 14)
            };
            _chkSkipEmpty.CheckedChanged += (s, e) => ApplyLevelFilter();
            pnlTop.Controls.AddRange(new Control[] { lblLevel, _cmbLevel, _chkSkipEmpty });

            // ===== 中部 Split：左规则 / 右预览 =====
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 380,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.FromArgb(220, 225, 230)
            };

            // === 左侧：规则 ===
            var pnlRules = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };
            int y = 8;

            // 1. 前后缀
            var gbAffix = MakeGroupBox("1. 前后缀", 8, ref y, 360, 80);
            _chkPrefix = new CheckBox { Text = "前缀：", Left = 16, Top = 24, Width = 60 };
            _txtPrefix = new TextBox { Left = 80, Top = 22, Width = 260 };
            _chkPrefix.CheckedChanged += (s, e) => RefreshPreview();
            _txtPrefix.TextChanged += (s, e) => { if (!_suppressEvent) RefreshPreview(); };
            _chkSuffix = new CheckBox { Text = "后缀：", Left = 16, Top = 50, Width = 60 };
            _txtSuffix = new TextBox { Left = 80, Top = 48, Width = 260 };
            _chkSuffix.CheckedChanged += (s, e) => RefreshPreview();
            _txtSuffix.TextChanged += (s, e) => { if (!_suppressEvent) RefreshPreview(); };
            gbAffix.Controls.AddRange(new Control[] { _chkPrefix, _txtPrefix, _chkSuffix, _txtSuffix });
            pnlRules.Controls.Add(gbAffix);

            // 2. 查找替换
            var gbFR = MakeGroupBox("2. 查找替换", 8, ref y, 360, 130);
            _chkFind = new CheckBox { Text = "启用", Left = 16, Top = 22, Width = 60 };
            _chkFind.CheckedChanged += (s, e) => RefreshPreview();
            var lblFind = new Label { Text = "查找：", Left = 16, Top = 50, Width = 50 };
            _txtFind = new TextBox { Left = 70, Top = 48, Width = 270 };
            _txtFind.TextChanged += (s, e) => { if (!_suppressEvent) RefreshPreview(); };
            var lblReplace = new Label { Text = "替换：", Left = 16, Top = 78, Width = 50 };
            _txtReplace = new TextBox { Left = 70, Top = 76, Width = 270 };
            _txtReplace.TextChanged += (s, e) => { if (!_suppressEvent) RefreshPreview(); };
            _chkIgnoreCase = new CheckBox { Text = "忽略大小写", Left = 16, Top = 104, Width = 100, Checked = true };
            _chkIgnoreCase.CheckedChanged += (s, e) => RefreshPreview();
            _chkRegex = new CheckBox { Text = "正则", Left = 130, Top = 104, Width = 60 };
            _chkRegex.CheckedChanged += (s, e) => RefreshPreview();
            gbFR.Controls.AddRange(new Control[] { _chkFind, lblFind, _txtFind, lblReplace, _txtReplace, _chkIgnoreCase, _chkRegex });
            pnlRules.Controls.Add(gbFR);

            // 3. 截取
            var gbTrim = MakeGroupBox("3. 截取字符", 8, ref y, 360, 80);
            _chkTrim = new CheckBox { Text = "启用", Left = 16, Top = 22, Width = 60 };
            _chkTrim.CheckedChanged += (s, e) => RefreshPreview();
            var lblTS = new Label { Text = "去掉前 N 字符：", Left = 16, Top = 50, Width = 100 };
            _numTrimStart = new NumericUpDown { Left = 120, Top = 48, Width = 60, Minimum = 0, Maximum = 99, Value = 0 };
            _numTrimStart.ValueChanged += (s, e) => RefreshPreview();
            var lblTE = new Label { Text = "去掉末 N 字符：", Left = 200, Top = 50, Width = 100 };
            _numTrimEnd = new NumericUpDown { Left = 304, Top = 48, Width = 40, Minimum = 0, Maximum = 99, Value = 0 };
            _numTrimEnd.ValueChanged += (s, e) => RefreshPreview();
            gbTrim.Controls.AddRange(new Control[] { _chkTrim, lblTS, _numTrimStart, lblTE, _numTrimEnd });
            pnlRules.Controls.Add(gbTrim);

            // 4. 编号
            var gbCounter = MakeGroupBox("4. 自动编号（按当前顺序）", 8, ref y, 360, 130);
            _chkCounter = new CheckBox { Text = "启用", Left = 16, Top = 22, Width = 60 };
            _chkCounter.CheckedChanged += (s, e) => RefreshPreview();
            var lblCP = new Label { Text = "位置：", Left = 16, Top = 50, Width = 50 };
            _cmbCounterPos = new ComboBox
            {
                Left = 70,
                Top = 48,
                Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbCounterPos.Items.AddRange(new object[] { "前缀", "后缀" });
            _cmbCounterPos.SelectedIndex = 1;
            _cmbCounterPos.SelectedIndexChanged += (s, e) => RefreshPreview();
            var lblCPre = new Label { Text = "连接符：", Left = 160, Top = 50, Width = 60 };
            _txtCounterPrefix = new TextBox { Left = 220, Top = 48, Width = 50, Text = "-" };
            _txtCounterPrefix.TextChanged += (s, e) => { if (!_suppressEvent) RefreshPreview(); };
            var lblCS = new Label { Text = "起始：", Left = 16, Top = 78, Width = 50 };
            _numCounterStart = new NumericUpDown { Left = 70, Top = 76, Width = 60, Minimum = 0, Maximum = 9999, Value = 1 };
            _numCounterStart.ValueChanged += (s, e) => RefreshPreview();
            var lblCD = new Label { Text = "位数：", Left = 150, Top = 78, Width = 50 };
            _numCounterDigits = new NumericUpDown { Left = 200, Top = 76, Width = 60, Minimum = 1, Maximum = 10, Value = 2 };
            _numCounterDigits.ValueChanged += (s, e) => RefreshPreview();
            gbCounter.Controls.AddRange(new Control[] { _chkCounter, lblCP, _cmbCounterPos, lblCPre, _txtCounterPrefix, lblCS, _numCounterStart, lblCD, _numCounterDigits });
            pnlRules.Controls.Add(gbCounter);

            // 提示
            var lblHint = new Label
            {
                Text = "说明：以上规则按顺序组合应用：前后缀 → 查找替换 → 截取 → 编号。\n" +
                       "手动修改了「新名称」列的项不会被规则覆盖。",
                Left = 8,
                Top = y + 6,
                Width = 360,
                Height = 40,
                ForeColor = Color.DarkSlateBlue
            };
            pnlRules.Controls.Add(lblHint);

            split.Panel1.Controls.Add(pnlRules);

            // === 右侧：预览 ===
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var pnlToolbar = new Panel { Dock = DockStyle.Top, Height = 36 };
            _btnPreview = new Button
            {
                Text = "重新生成预览",
                Location = new Point(0, 4),
                Size = new Size(120, 28),
                BackColor = Color.FromArgb(40, 90, 160),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnPreview.Click += (s, e) => RefreshPreview();
            _btnSelectAll = new Button { Text = "全选", Location = new Point(130, 4), Size = new Size(60, 28) };
            _btnSelectAll.Click += (s, e) => SetSelected(true);
            _btnSelectNone = new Button { Text = "全不选", Location = new Point(196, 4), Size = new Size(70, 28) };
            _btnSelectNone.Click += (s, e) => SetSelected(false);
            pnlToolbar.Controls.AddRange(new Control[] { _btnPreview, _btnSelectAll, _btnSelectNone });

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnEnter
            };

            pnlRight.Controls.Add(_dgv);
            pnlRight.Controls.Add(pnlToolbar);
            split.Panel2.Controls.Add(pnlRight);

            // ===== 底部：状态 + 按钮 =====
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStat = new Label
            {
                AutoSize = true,
                Location = new Point(12, 18),
                ForeColor = Color.FromArgb(60, 60, 60),
                Text = "就绪"
            };
            _btnCancel = new Button { Text = "取消", Size = new Size(90, 32), FlatStyle = FlatStyle.Flat };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnApply = new Button
            {
                Text = "应用",
                Size = new Size(90, 32),
                BackColor = Color.FromArgb(40, 90, 160),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnApply.Click += BtnApply_Click;
            pnlBottom.Controls.Add(_lblStat);
            pnlBottom.Controls.Add(_btnCancel);
            pnlBottom.Controls.Add(_btnApply);
            pnlBottom.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(pnlBottom.Width - 210, 12);
                _btnApply.Location = new Point(pnlBottom.Width - 110, 12);
            };

            // 组合
            Controls.Add(split);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;
        }

        private GroupBox MakeGroupBox(string title, int x, ref int y, int w, int h)
        {
            var gb = new GroupBox
            {
                Text = title,
                Left = x,
                Top = y,
                Width = w,
                Height = h
            };
            y += h + 8;
            return gb;
        }

        // ============================================================
        // 数据加载
        // ============================================================
        private void OnLoadData()
        {
            try
            {
                LoadLevels();
                LoadRooms();
                ApplyLevelFilter();
                RefreshPreview();
                RebindGrid();
                UpdateStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadLevels()
        {
            _cmbLevel.Items.Clear();
            _cmbLevel.Items.Add(new LevelItem { Id = ElementId.InvalidElementId, Name = "全部楼层" });
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            foreach (var l in levels)
                _cmbLevel.Items.Add(new LevelItem { Id = l.Id, Name = l.Name });
            _cmbLevel.SelectedIndex = 0;
        }

        private void LoadRooms()
        {
            _allItems.Clear();
            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            foreach (var r in rooms)
            {
                string name = "";
                try { name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""; }
                catch { }

                string num = "";
                try { num = r.Number ?? ""; } catch { }

                string lvlName = "";
                try { lvlName = (r.Level as Level)?.Name ?? ""; } catch { }

                double area = 0;
                try { area = r.Area; } catch { }

                _allItems.Add(new RoomRenameItem
                {
                    RoomId = r.Id,
                    LevelName = lvlName,
                    Number = num,
                    Area = area,
                    OriginalName = name,
                    NewName = name
                });
            }

            // 默认按楼层 + 编号 排序
            _allItems = _allItems.OrderBy(x => x.LevelName).ThenBy(x => x.Number).ToList();
        }

        private void ApplyLevelFilter()
        {
            if (_cmbLevel.SelectedItem is LevelItem li)
            {
                if (li.Id == ElementId.InvalidElementId)
                    _viewItems = new BindingList<RoomRenameItem>(_allItems.ToList());
                else
                {
                    var lvlName = li.Name;
                    _viewItems = new BindingList<RoomRenameItem>(
                        _allItems.Where(x => string.Equals(x.LevelName, lvlName, StringComparison.OrdinalIgnoreCase)).ToList());
                }
            }
            else
            {
                _viewItems = new BindingList<RoomRenameItem>(_allItems.ToList());
            }

            // 应用空名过滤
            if (_chkSkipEmpty.Checked)
            {
                var filtered = _viewItems.Where(x => !string.IsNullOrWhiteSpace(x.OriginalName) && x.Area > 0).ToList();
                _viewItems = new BindingList<RoomRenameItem>(filtered);
            }

            RebindGrid();
            RefreshPreview();
            UpdateStats();
        }

        private void SetSelected(bool sel)
        {
            foreach (var it in _viewItems) it.Selected = sel;
            _dgv.Invalidate();
            UpdateStats();
        }

        // ============================================================
        // 预览生成：按规则计算 NewName
        // ============================================================
        private void RefreshPreview()
        {
            if (_allItems == null || _allItems.Count == 0) return;

            var rules = CollectRules();
            var visible = new HashSet<RoomRenameItem>(_viewItems);

            // 先把所有项按顺序编号（仅视图内）
            int counterIdx = 0;
            foreach (var it in _viewItems)
            {
                // 已被用户手动改过（Remark 标记为「手动」）→ 跳过
                if (!string.IsNullOrEmpty(it.Remark) && it.Remark.StartsWith("手动"))
                {
                    counterIdx++;
                    continue;
                }

                string s = it.OriginalName ?? "";

                // 1) 前后缀
                if (rules.EnablePrefix && !string.IsNullOrEmpty(rules.Prefix))
                    s = rules.Prefix + s;
                if (rules.EnableSuffix && !string.IsNullOrEmpty(rules.Suffix))
                    s = s + rules.Suffix;

                // 2) 查找替换
                if (rules.EnableFindReplace && !string.IsNullOrEmpty(rules.Find))
                {
                    try
                    {
                        if (rules.UseRegex)
                        {
                            var opts = rules.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                            s = Regex.Replace(s, rules.Find, rules.Replace ?? "", opts);
                        }
                        else
                        {
                            var sc = rules.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                            s = ReplaceCaseSensitive(s, rules.Find, rules.Replace ?? "", sc);
                        }
                    }
                    catch (Exception ex)
                    {
                        it.Remark = "正则错误: " + ex.Message;
                        it.NewName = it.OriginalName;
                        counterIdx++;
                        continue;
                    }
                }

                // 3) 截取
                if (rules.EnableTrim)
                {
                    int ts = Math.Min((int)rules.TrimStart, s.Length);
                    int te = Math.Min((int)rules.TrimEnd, Math.Max(0, s.Length - ts));
                    if (ts > 0 || te > 0)
                        s = s.Substring(ts, s.Length - ts - te);
                }

                // 4) 编号（按当前视图顺序）
                if (rules.EnableCounter)
                {
                    string n = (rules.CounterStart + counterIdx).ToString("D" + rules.CounterDigits, CultureInfo.InvariantCulture);
                    string token = (rules.CounterPrefix ?? "") + n;
                    s = rules.CounterPos == CounterPosition.Prefix ? token + s : s + token;
                }

                it.NewName = s;
                if (it.NewName == it.OriginalName) it.Remark = "不变";
                else it.Remark = "";
                counterIdx++;
            }

            // 不在视图中的项也确保 NewName 至少等于 OriginalName（避免脏数据）
            foreach (var it in _allItems)
            {
                if (!visible.Contains(it))
                    it.NewName = it.OriginalName;
            }

            _dgv.Invalidate();
            UpdateStats();
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

        private RoomRenameRules CollectRules()
        {
            return new RoomRenameRules
            {
                EnablePrefix = _chkPrefix.Checked,
                Prefix = _txtPrefix.Text ?? "",
                EnableSuffix = _chkSuffix.Checked,
                Suffix = _txtSuffix.Text ?? "",
                EnableFindReplace = _chkFind.Checked,
                Find = _txtFind.Text ?? "",
                Replace = _txtReplace.Text ?? "",
                IgnoreCase = _chkIgnoreCase.Checked,
                UseRegex = _chkRegex.Checked,
                EnableCounter = _chkCounter.Checked,
                CounterPrefix = _txtCounterPrefix.Text ?? "",
                CounterStart = (int)_numCounterStart.Value,
                CounterDigits = (int)_numCounterDigits.Value,
                CounterPos = _cmbCounterPos.SelectedIndex == 0 ? CounterPosition.Prefix : CounterPosition.Suffix,
                EnableTrim = _chkTrim.Checked,
                TrimStart = (int)_numTrimStart.Value,
                TrimEnd = (int)_numTrimEnd.Value
            };
        }

        private void UpdateStats()
        {
            int total = _viewItems.Count;
            int sel = _viewItems.Count(x => x.Selected);
            int changed = _viewItems.Count(x => x.Selected && x.NewName != x.OriginalName && !string.IsNullOrEmpty(x.NewName));
            _lblStat.Text = $"共 {total} 个房间，已选 {sel}，将修改 {changed}（允许重名）。";
        }

        // ============================================================
        // 表格绑定
        // ============================================================
        private void RebindGrid()
        {
            _suppressEvent = true;
            try
            {
                _dgv.DataSource = null;
                _dgv.AutoGenerateColumns = false;
                _dgv.Columns.Clear();

                var chk = new DataGridViewCheckBoxColumn
                {
                    HeaderText = "应用",
                    DataPropertyName = "Selected",
                    ReadOnly = false,
                    Width = 50
                };
                _dgv.Columns.Add(chk);

                _dgv.Columns.Add(MakeTextCol("LevelName", "楼层", 110, true, false));
                _dgv.Columns.Add(MakeTextCol("Number", "编号", 80, true, true));
                _dgv.Columns.Add(MakeTextCol("OriginalName", "原名称", 220, true, true));
                _dgv.Columns.Add(MakeTextCol("NewName", "新名称（可编辑）", 260, false, true));
                _dgv.Columns.Add(MakeTextCol("Area", "面积(㎡)", 90, true, true));
                _dgv.Columns.Add(MakeTextCol("Remark", "备注", 100, true, true));

                _dgv.DataSource = _viewItems;
                _dgv.CellValueChanged += Dgv_CellValueChanged;
                _dgv.CellContentClick += Dgv_CellContentClick;
                _dgv.CurrentCellDirtyStateChanged += (s, e) =>
                {
                    if (_dgv.IsCurrentCellDirty) _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
            }
            finally
            {
                _suppressEvent = false;
            }
        }

        private DataGridViewTextBoxColumn MakeTextCol(string prop, string header, int width, bool readOnly, bool visible)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = prop,
                ReadOnly = readOnly,
                Width = width,
                Visible = visible
            };
        }

        private void Dgv_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressEvent || e.RowIndex < 0) return;
            var col = _dgv.Columns[e.ColumnIndex];
            if (col.DataPropertyName == "Selected")
            {
                UpdateStats();
                return;
            }
            if (col.DataPropertyName == "NewName")
            {
                if (_dgv.Rows[e.RowIndex].DataBoundItem is RoomRenameItem it)
                {
                    it.Remark = "手动";
                    UpdateStats();
                }
            }
        }

        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_dgv.Columns[e.ColumnIndex].DataPropertyName != "Selected") return;

            var row = _dgv.Rows[e.RowIndex];
            if (!(row.DataBoundItem is RoomRenameItem it)) return;
            bool newVal = !it.Selected;

            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift && _lastCheckIndex >= 0)
            {
                int min = Math.Min(_lastCheckIndex, e.RowIndex);
                int max = Math.Max(_lastCheckIndex, e.RowIndex);
                for (int i = min; i <= max; i++)
                {
                    if (_dgv.Rows[i].DataBoundItem is RoomRenameItem x) x.Selected = newVal;
                }
            }
            else
            {
                it.Selected = newVal;
            }
            _lastCheckIndex = e.RowIndex;
            _dgv.Refresh();
            UpdateStats();
        }

        // ============================================================
        // 应用
        // ============================================================
        private void BtnApply_Click(object sender, EventArgs e)
        {
            // 收集要应用项：已选 + 新名与原名不同 + 新名非空
            var toApply = _viewItems
                .Where(x => x.Selected
                    && !string.IsNullOrWhiteSpace(x.NewName)
                    && x.NewName != x.OriginalName)
                .ToList();

            if (toApply.Count == 0)
            {
                MessageBox.Show("没有需要修改的房间。请勾选「应用」并确保新名称与原名称不同。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.None;
                return;
            }

            // 二次确认
            string msg = $"即将修改 {toApply.Count} 个房间的名称（事务一次性提交）。\n\n是否继续？";
            if (MessageBox.Show(msg, "确认应用", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                this.DialogResult = DialogResult.None;
                return;
            }

            ItemsToApply = toApply;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ============================================================
        // 内部类
        // ============================================================
        private class LevelItem
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }
    }
}
