using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MyRevitAddin.PipeFittingSzMarker
{
    internal class PipeFittingSzMarkerDialog : Form
    {
        private readonly Document _doc;
        private readonly List<ElementId> _selectedIds;

        // 类别定义
        private class CatOption
        {
            public bool Apply { get; set; }
            public BuiltInCategory Bic { get; set; }
            public string Name { get; set; }
            public string Desc { get; set; }
        }

        // 预览行
        private class PreviewRow
        {
            public bool Selected { get; set; } = true;
            public ElementId ElementId { get; set; }
            public string CategoryName { get; set; }
            public string ElementName { get; set; }
            public string MatchValue { get; set; }
            public string CurrentValue { get; set; } = "";
            public string HasParam { get; set; } = "";
        }

        private RadioButton _rbSelected, _rbAll;
        private CheckedListBox _clbCats;
        private DataGridView _dgv;
        private Label _lblStat;
        private Button _btnApply, _btnCancel, _btnRefresh;
        private BindingList<PreviewRow> _viewRows;
        private List<CatOption> _cats;
        private bool _suppress;

        public List<SzMarkerAssignment> Assignments { get; private set; }

        public PipeFittingSzMarkerDialog(Document doc, List<ElementId> selectedIds)
        {
            _doc = doc;
            _selectedIds = selectedIds ?? new List<ElementId>();
            _cats = new List<CatOption>
            {
                new CatOption { Apply = true, Bic = BuiltInCategory.OST_PipeFitting,       Name = "管道管件", Desc = "管三通、弯头、四通、活接头等" },
                new CatOption { Apply = true, Bic = BuiltInCategory.OST_DuctFitting,       Name = "风管管件", Desc = "风管三通、弯头、四通等" },
                new CatOption { Apply = true, Bic = BuiltInCategory.OST_CableTrayFitting, Name = "桥架配件", Desc = "桥架弯头、三通、四通等（若名称匹配）" },
                new CatOption { Apply = true, Bic = BuiltInCategory.OST_ConduitFitting,    Name = "线管配件", Desc = "线管弯头、三通等（若名称匹配）" }
            };

            InitUI();
            Load += (s, e) =>
            {
                RefreshCats();
                ReloadData();
            };
        }

        private void InitUI()
        {
            Text = "管件深圳构件标识填充（按名称关键字）";
            Size = new Size(980, 640);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            // 顶栏
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.FromArgb(235, 240, 248) };
            _rbSelected = new RadioButton { Text = "仅处理选中管件", Left = 12, Top = 8, AutoSize = true, Checked = _selectedIds.Count > 0 };
            _rbAll = new RadioButton { Text = "处理全模型", Left = 160, Top = 8, AutoSize = true, Checked = _selectedIds.Count == 0 };
            _rbSelected.CheckedChanged += (s, e) => { if (!_suppress) ReloadData(); };
            _rbAll.CheckedChanged += (s, e) => { if (!_suppress) ReloadData(); };

            var lblHint = new Label
            {
                Left = 12, Top = 32, Width = 700, Height = 20,
                Text = "匹配映射：" + PipeFittingSzMarkerCommand.MappingTip + "  → 写入参数「深圳构件标识」（参数名大小写无关）",
                ForeColor = Color.DarkSlateBlue
            };
            var lblHintSelected = new Label
            {
                Left = 12, Top = 56, Width = 520, Height = 18,
                Text = _selectedIds.Count > 0
                    ? "当前在 Revit 中已选中 " + _selectedIds.Count + " 个构件（可先选再打开）"
                    : "当前未选中（可在 Revit 框选管件后重新打开，或使用「处理全模型」）",
                ForeColor = Color.DarkSlateGray
            };

            _btnRefresh = new Button { Text = "刷新", Left = 580, Top = 52, Size = new Size(60, 28), FlatStyle = FlatStyle.Flat };
            _btnRefresh.Click += (s, e) => ReloadData();

            pnlTop.Controls.AddRange(new Control[] { _rbSelected, _rbAll, lblHint, lblHintSelected, _btnRefresh });

            // 左侧：类别勾选（纯 Dock 布局替代 SplitContainer，彻底避免 SplitterDistance 越界）
            var pnlLeft = new Panel
            {
                Dock = DockStyle.Left,
                Width = 220,
                MinimumSize = new Size(180, 0),
                BackColor = Color.FromArgb(220, 225, 230)
            };
            // 右侧与预览之间加一条竖分隔条（可视化）
            var pnlSeparator = new Panel
            {
                Dock = DockStyle.Left,
                Width = 2,
                BackColor = Color.FromArgb(200, 205, 215)
            };

            var gbCats = new GroupBox
            {
                Text = "参与的管件类别",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            _clbCats = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            var btnAllCats = new Button { Text = "全选", Dock = DockStyle.Top, Height = 28, FlatStyle = FlatStyle.Flat };
            var btnNoCats = new Button { Text = "全不选", Dock = DockStyle.Top, Height = 28, FlatStyle = FlatStyle.Flat };
            btnAllCats.Click += (s, e) => SetCatsAll(true);
            btnNoCats.Click += (s, e) => SetCatsAll(false);
            gbCats.Controls.Add(_clbCats);
            gbCats.Controls.Add(btnAllCats);
            gbCats.Controls.Add(btnNoCats);
            gbCats.Controls.SetChildIndex(_clbCats, 0);
            pnlLeft.Controls.Add(gbCats);

            // 右侧：预览表
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
            var pnlRTop = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(0) };
            var btnSelAll = new Button { Text = "全选构件", Left = 0, Top = 2, Size = new Size(90, 26), FlatStyle = FlatStyle.Flat };
            var btnSelNone = new Button { Text = "全不选", Left = 96, Top = 2, Size = new Size(80, 26), FlatStyle = FlatStyle.Flat };
            btnSelAll.Click += (s, e) => SetRows(true);
            btnSelNone.Click += (s, e) => SetRows(false);
            pnlRTop.Controls.AddRange(new Control[] { btnSelAll, btnSelNone });

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(220, 225, 230),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(248, 250, 252) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(220, 230, 245),
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                EnableHeadersVisualStyles = false,
                ReadOnly = false
            };
            var colSel = new DataGridViewCheckBoxColumn
            {
                Name = "Selected", HeaderText = "选择", DataPropertyName = "Selected", Width = 55,
                TrueValue = true, FalseValue = false, ReadOnly = false
            };
            var colCat = new DataGridViewTextBoxColumn
            {
                Name = "CategoryName", HeaderText = "类别", DataPropertyName = "CategoryName",
                Width = 100, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colName = new DataGridViewTextBoxColumn
            {
                Name = "ElementName", HeaderText = "管件类型名称", DataPropertyName = "ElementName",
                Width = 260, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colMatch = new DataGridViewTextBoxColumn
            {
                Name = "MatchValue", HeaderText = "匹配到→新值", DataPropertyName = "MatchValue",
                Width = 110, ReadOnly = true
            };
            colMatch.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(253, 246, 230),
                ForeColor = Color.FromArgb(190, 90, 20),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };
            var colCur = new DataGridViewTextBoxColumn
            {
                Name = "CurrentValue", HeaderText = "当前「深圳构件标识」", DataPropertyName = "CurrentValue",
                Width = 160, ReadOnly = true
            };
            var colFlag = new DataGridViewTextBoxColumn
            {
                Name = "HasParam", HeaderText = "状态", DataPropertyName = "HasParam",
                Width = 80, ReadOnly = true
            };
            colFlag.DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };
            var colId = new DataGridViewTextBoxColumn
            {
                Name = "ElementId", HeaderText = "ID", DataPropertyName = "ElementId",
                Width = 80, ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            };

            _dgv.Columns.AddRange(new DataGridViewColumn[] { colSel, colCat, colName, colMatch, colCur, colFlag, colId });
            _dgv.CellValueChanged += (s, e) => UpdateStat();
            _dgv.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgv.IsCurrentCellDirty) _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _dgv.DataBindingComplete += (s, e) => UpdateStat();

            var gbRight = new GroupBox { Text = "预览（勾选的管件才会应用）", Dock = DockStyle.Fill, Padding = new Padding(6) };
            gbRight.Controls.Add(pnlRTop);
            gbRight.Controls.Add(_dgv);
            gbRight.Controls.SetChildIndex(_dgv, 0);
            pnlRight.Controls.Add(gbRight);

            // 底栏
            var pnlBot = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStat = new Label { AutoSize = false, Left = 12, Top = 16, Width = 700, Height = 22, ForeColor = Color.DarkSlateGray };
            _btnCancel = new Button
            {
                Text = "取消", DialogResult = DialogResult.Cancel,
                Size = new Size(80, 30), Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                FlatStyle = FlatStyle.Flat
            };
            _btnApply = new Button
            {
                Text = "应用填充",
                Size = new Size(100, 30), Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.FromArgb(70, 130, 200),
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            this.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(ClientSize.Width - 188, 11);
                _btnApply.Location = new Point(ClientSize.Width - 294, 11);
            };
            _btnCancel.Location = new Point(980 - 188, 11);
            _btnApply.Location = new Point(980 - 294, 11);
            _btnApply.Click += (s, e) =>
            {
                if (!BuildAssignments()) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            pnlBot.Controls.AddRange(new Control[] { _lblStat, _btnCancel, _btnApply });

            // 装配（Dock 顺序关键：先 Fill，再 Left（后加的在最左），再 Top/Bottom）
            Controls.Add(pnlRight);
            Controls.Add(pnlSeparator);
            Controls.Add(pnlLeft);
            Controls.Add(pnlTop);
            Controls.Add(pnlBot);
        }

        private void RefreshCats()
        {
            _suppress = true;
            _clbCats.ItemCheck -= OnClbCatsItemCheck;
            _clbCats.Items.Clear();
            for (int i = 0; i < _cats.Count; i++)
            {
                var c = _cats[i];
                _clbCats.Items.Add(c, c.Apply ? CheckState.Checked : CheckState.Unchecked);
            }
            // 手动显示文字（重写 DrawMode）：改用简单方式，DisplayMember 无效就用文本对象列表
            _clbCats.Items.Clear();
            for (int i = 0; i < _cats.Count; i++)
            {
                var c = _cats[i];
                _clbCats.Items.Add(new CatListItem(c), c.Apply ? CheckState.Checked : CheckState.Unchecked);
            }
            _clbCats.ItemCheck += OnClbCatsItemCheck;
            _suppress = false;
        }

        private class CatListItem
        {
            public CatOption Option { get; private set; }
            public CatListItem(CatOption c) { Option = c; }
            public override string ToString()
            {
                return Option.Name + "  —  " + Option.Desc;
            }
        }

        private void OnClbCatsItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppress) return;
            // 更新 cats 状态（通过 CatListItem.Option 引用）
            var li = _clbCats.Items[e.Index] as CatListItem;
            if (li != null) li.Option.Apply = e.NewValue == CheckState.Checked;
            // ItemCheck 后立即刷新
            BeginInvoke((Action)(() => ReloadData()));
        }

        private void SetCatsAll(bool sel)
        {
            _suppress = true;
            _clbCats.ItemCheck -= OnClbCatsItemCheck;
            for (int i = 0; i < _clbCats.Items.Count; i++)
            {
                _clbCats.SetItemChecked(i, sel);
                var li = _clbCats.Items[i] as CatListItem;
                if (li != null) li.Option.Apply = sel;
            }
            _clbCats.ItemCheck += OnClbCatsItemCheck;
            _suppress = false;
            ReloadData();
        }

        private void ReloadData()
        {
            HashSet<ElementId> selSet = null;
            if (_rbSelected.Checked && _selectedIds.Count > 0)
                selSet = new HashSet<ElementId>(_selectedIds);

            var rows = new List<PreviewRow>();
            foreach (var c in _cats)
            {
                if (!c.Apply) continue;
                List<Element> elems;
                try
                {
                    var col = new FilteredElementCollector(_doc)
                        .OfCategory(c.Bic)
                        .WhereElementIsNotElementType();
                    if (selSet != null)
                        elems = col.Where(e => selSet.Contains(e.Id)).ToList();
                    else
                        elems = col.ToList();
                }
                catch { continue; }

                foreach (var e in elems)
                {
                    if (e == null || !e.IsValidObject) continue;
                    // 取类型名称
                    string typeName = "";
                    try
                    {
                        ElementId tid = e.GetTypeId();
                        Element t = _doc.GetElement(tid);
                        if (t != null) typeName = t.Name ?? "";
                    }
                    catch { }
                    // 如果类型名没匹配，试试 Element.Name 本身
                    string match = PipeFittingSzMarkerCommand.MatchKeyword(typeName);
                    if (match == null && !string.IsNullOrEmpty(e.Name))
                        match = PipeFittingSzMarkerCommand.MatchKeyword(e.Name);

                    if (match == null) continue;  // 没有匹配就不进预览表

                    // 查当前值
                    string cur = "";
                    bool hasParam = false;
                    try
                    {
                        Parameter p = PipeFittingSzMarkerCommand.FindParameterCaseInsensitive(e, "深圳构件标识");
                        if (p != null)
                        {
                            hasParam = true;
                            cur = p.AsString() ?? "";
                            if (string.IsNullOrEmpty(cur))
                                try { cur = p.AsValueString() ?? ""; } catch { }
                        }
                    }
                    catch { }

                    rows.Add(new PreviewRow
                    {
                        Selected = true,
                        ElementId = e.Id,
                        CategoryName = c.Name,
                        ElementName = typeName,
                        MatchValue = match,
                        CurrentValue = cur ?? "",
                        HasParam = hasParam ? (string.IsNullOrEmpty(cur) ? "空" : "已有值") : "缺参数"
                    });
                }
            }

            _viewRows = new BindingList<PreviewRow>(rows);
            _dgv.DataSource = null;
            _dgv.DataSource = _viewRows;
            if (_dgv.Columns.Contains("Selected"))
                _dgv.Columns["Selected"].ReadOnly = false;

            // 着色：缺参数 红色，已有值≠新值 橙色
            foreach (DataGridViewRow r in _dgv.Rows)
            {
                var pr = r.DataBoundItem as PreviewRow;
                if (pr == null) continue;
                if (pr.HasParam == "缺参数")
                {
                    r.DefaultCellStyle.ForeColor = Color.Firebrick;
                    r.Cells["HasParam"].Style.ForeColor = Color.Firebrick;
                }
                else if (pr.HasParam == "已有值" && pr.CurrentValue != pr.MatchValue)
                {
                    r.Cells["HasParam"].Style.ForeColor = Color.DarkOrange;
                    r.DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 240);
                }
            }

            UpdateStat();
        }

        private void SetRows(bool sel)
        {
            if (_viewRows == null) return;
            foreach (var r in _viewRows) r.Selected = sel;
            _dgv.DataSource = null;
            _viewRows = new BindingList<PreviewRow>(_viewRows.ToList());
            _dgv.DataSource = _viewRows;
            if (_dgv.Columns.Contains("Selected"))
                _dgv.Columns["Selected"].ReadOnly = false;
            UpdateStat();
        }

        private void UpdateStat()
        {
            if (_viewRows == null) return;
            int total = _viewRows.Count;
            int sel = _viewRows.Count(r => r.Selected);
            int noParam = _viewRows.Count(r => r.Selected && r.HasParam == "缺参数");
            int over = _viewRows.Count(r => r.Selected && r.HasParam == "已有值" && r.CurrentValue != r.MatchValue);
            _lblStat.Text = string.Format(
                "匹配到 {0} 个管件，已勾选 {1} 个；其中缺参数「深圳构件标识」{2} 个（这些将在统计里提示），覆盖非空值 {3} 个。",
                total, sel, noParam, over);
        }

        private bool BuildAssignments()
        {
            var list = new List<SzMarkerAssignment>();
            if (_viewRows != null)
            {
                foreach (var r in _viewRows)
                {
                    if (!r.Selected) continue;
                    list.Add(new SzMarkerAssignment
                    {
                        ElementId = r.ElementId,
                        CategoryName = r.CategoryName,
                        ElementName = r.ElementName,
                        MatchValue = r.MatchValue
                    });
                }
            }
            if (list.Count == 0)
            {
                MessageBox.Show("请至少勾选一个管件（或切换模式/类别后重新刷新）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            Assignments = list;
            return true;
        }
    }
}
