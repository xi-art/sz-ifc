using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.ColumnDimensionCalculator
{
    using WinComboBox = System.Windows.Forms.ComboBox;

    internal class ColumnRow
    {
        public bool Selected { get; set; } = true;
        public string TypeName { get; set; }
        public int ElemId { get; set; }
        public double VolumeCbm { get; set; }
        public double HeightMm { get; set; }
        public double CalculatedLength { get; set; }
        public double CalculatedWidth { get; set; }
        public string CurrentLength { get; set; } = "";
        public string CurrentWidth { get; set; } = "";
        public string Status { get; set; } = "";
        public ElementId ElementId { get; set; }
    }

    internal class ParamItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Scope { get; set; }
        public override string ToString() => $"[{Scope}] {DisplayName}";
    }

    internal class ColumnDimensionCalculatorDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly List<ElementId> _selectedIds;

        private List<ColumnRow> _allRows = new List<ColumnRow>();
        private BindingList<ColumnRow> _viewRows;
        private List<ParamItem> _allParams = new List<ParamItem>();

        private WinComboBox _cmbVolumeParam;
        private WinComboBox _cmbTopLevelParam;
        private WinComboBox _cmbTopOffsetParam;
        private WinComboBox _cmbBaseLevelParam;
        private WinComboBox _cmbBaseOffsetParam;
        private WinComboBox _cmbLengthParam;
        private WinComboBox _cmbWidthParam;
        private RadioButton _rbSelected;
        private RadioButton _rbAllModel;
        private DataGridView _dgv;
        private Label _lblStat;
        private Button _btnApply;
        private Button _btnCancel;
        private Button _btnRefresh;
        private bool _suppress;

        private const double MM_PER_FOOT = 304.8;
        private const double CBM_PER_CUBIC_FT = 0.0283168466;

        public ColumnDimensionCalculatorDialog(Document doc, UIDocument uiDoc, List<ElementId> selectedIds)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _selectedIds = selectedIds ?? new List<ElementId>();
            InitUI();
            Load += (s, e) => OnLoad();
        }

        private void InitUI()
        {
            Text = "异形柱长宽计算 — 体积÷高度推算正方形尺寸";
            Size = new Size(1100, 680);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.FromArgb(235, 240, 248) };

            _rbSelected = new RadioButton { Text = "仅处理选中构件", Left = 12, Top = 5, Checked = _selectedIds.Count > 0 };
            _rbAllModel = new RadioButton { Text = "处理全模型", Left = 140, Top = 5, Checked = _selectedIds.Count == 0 };
            _rbSelected.CheckedChanged += (s, e) => { if (!_suppress) ReloadData(); };
            _rbAllModel.CheckedChanged += (s, e) => { if (!_suppress) ReloadData(); };

            var lblSelectionHint = new Label
            {
                Text = _selectedIds.Count > 0 ? string.Format("已选中 {0} 个构件", _selectedIds.Count) : "未选中任何构件",
                AutoSize = true,
                Location = new Point(270, 8),
                ForeColor = Color.DarkSlateBlue
            };

            var lblVol = new Label { Text = "体积参数：", AutoSize = true, Location = new Point(12, 35) };
            _cmbVolumeParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 32),
                Width = 160
            };
            _cmbVolumeParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblTopLevel = new Label { Text = "顶部标高：", AutoSize = true, Location = new Point(260, 35) };
            _cmbTopLevelParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(330, 32),
                Width = 130
            };
            _cmbTopLevelParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblTopOffset = new Label { Text = "顶部偏移：", AutoSize = true, Location = new Point(470, 35) };
            _cmbTopOffsetParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(540, 32),
                Width = 130
            };
            _cmbTopOffsetParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblBaseLevel = new Label { Text = "底部标高：", AutoSize = true, Location = new Point(12, 68) };
            _cmbBaseLevelParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 65),
                Width = 130
            };
            _cmbBaseLevelParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblBaseOffset = new Label { Text = "底部偏移：", AutoSize = true, Location = new Point(230, 68) };
            _cmbBaseOffsetParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(300, 65),
                Width = 130
            };
            _cmbBaseOffsetParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblLen = new Label { Text = "长度参数：", AutoSize = true, Location = new Point(450, 68) };
            _cmbLengthParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(520, 65),
                Width = 150
            };
            _cmbLengthParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblWid = new Label { Text = "宽度参数：", AutoSize = true, Location = new Point(690, 68) };
            _cmbWidthParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(760, 65),
                Width = 150
            };
            _cmbWidthParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            _btnRefresh = new Button
            {
                Text = "刷新数据",
                Location = new Point(930, 64),
                Size = new Size(85, 28),
                FlatStyle = FlatStyle.Flat
            };
            _btnRefresh.Click += (s, e) => ReloadData();

            var lblHint = new Label
            {
                Text = "公式：高度=(顶标高+顶偏移)-(底标高+底偏移)，S=√(V/H)",
                AutoSize = true,
                Location = new Point(12, 80),
                ForeColor = Color.DarkSlateBlue
            };

            pnlTop.Controls.AddRange(new Control[] { _rbSelected, _rbAllModel, lblSelectionHint, lblVol, _cmbVolumeParam, lblTopLevel, _cmbTopLevelParam, lblTopOffset, _cmbTopOffsetParam, lblBaseLevel, _cmbBaseLevelParam, lblBaseOffset, _cmbBaseOffsetParam, lblLen, _cmbLengthParam, lblWid, _cmbWidthParam, _btnRefresh, lblHint });
            pnlTop.Height = 110;

            var pnlMid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };

            var pnlGridTop = new Panel { Dock = DockStyle.Top, Height = 36 };
            var btnAll = new Button { Text = "全选", Left = 0, Top = 4, Size = new Size(60, 28), FlatStyle = FlatStyle.Flat };
            btnAll.Click += (s, e) => SetRows(true);
            var btnNone = new Button { Text = "全不选", Left = 66, Top = 4, Size = new Size(70, 28), FlatStyle = FlatStyle.Flat };
            btnNone.Click += (s, e) => SetRows(false);
            pnlGridTop.Controls.AddRange(new Control[] { btnAll, btnNone });

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Microsoft YaHei UI", 9F), Padding = new Padding(2) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(235, 240, 248),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };

            pnlMid.Controls.Add(_dgv);
            pnlMid.Controls.Add(pnlGridTop);

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStat = new Label { AutoSize = true, Location = new Point(12, 16), ForeColor = Color.FromArgb(60, 60, 60), Text = "正在加载..." };
            _btnCancel = new Button { Text = "取消", Size = new Size(90, 32), FlatStyle = FlatStyle.Flat };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnApply = new Button
            {
                Text = "计算并写入",
                Size = new Size(110, 32),
                BackColor = Color.FromArgb(40, 90, 160),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _btnApply.Click += BtnApply_Click;
            pnlBottom.Controls.Add(_lblStat);
            pnlBottom.Controls.Add(_btnCancel);
            pnlBottom.Controls.Add(_btnApply);
            pnlBottom.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(pnlBottom.Width - 210, 10);
                _btnApply.Location = new Point(pnlBottom.Width - 110, 10);
            };

            Controls.Add(pnlMid);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;
        }

        private void OnLoad()
        {
            try { ReloadData(); }
            catch (Exception ex) { MessageBox.Show("加载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ReloadData()
        {
            _suppress = true;
            _allRows.Clear();
            _allParams.Clear();

            List<FamilyInstance> columns;

            if (_rbSelected.Checked && _selectedIds.Count > 0)
            {
                columns = _selectedIds
                    .Select(id => _doc.GetElement(id))
                    .Where(e => e != null && e.Category != null && e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns && !(e is ElementType))
                    .Cast<FamilyInstance>()
                    .ToList();
            }
            else
            {
                columns = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();
            }

            var seenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns.Take(20))
            {
                CollectParams(column, "实例", seenParams);
                ElementType et = _doc.GetElement(column.GetTypeId()) as ElementType;
                if (et != null) CollectParams(et, "类型", seenParams);
            }

            _allParams = _allParams.OrderBy(p => p.Scope != "实例")
                                   .ThenBy(p => p.DisplayName)
                                   .ToList();

            _cmbVolumeParam.Items.Clear();
            _cmbTopLevelParam.Items.Clear();
            _cmbTopOffsetParam.Items.Clear();
            _cmbBaseLevelParam.Items.Clear();
            _cmbBaseOffsetParam.Items.Clear();
            _cmbLengthParam.Items.Clear();
            _cmbWidthParam.Items.Clear();
            foreach (var p in _allParams)
            {
                _cmbVolumeParam.Items.Add(p);
                _cmbTopLevelParam.Items.Add(p);
                _cmbTopOffsetParam.Items.Add(p);
                _cmbBaseLevelParam.Items.Add(p);
                _cmbBaseOffsetParam.Items.Add(p);
                if (p.Scope == "实例")
                {
                    _cmbLengthParam.Items.Add(p);
                    _cmbWidthParam.Items.Add(p);
                }
            }

            AutoSelectParam(_cmbVolumeParam, new[] { "体积", "Volume" });
            AutoSelectParam(_cmbTopLevelParam, new[] { "顶部标高", "顶标高", "Top Level", "顶部" });
            AutoSelectParam(_cmbTopOffsetParam, new[] { "顶部偏移", "顶偏移", "Top Offset" });
            AutoSelectParam(_cmbBaseLevelParam, new[] { "底部标高", "底标高", "Base Level", "底部" });
            AutoSelectParam(_cmbBaseOffsetParam, new[] { "底部偏移", "底偏移", "Base Offset" });
            AutoSelectParam(_cmbLengthParam, new[] { "柱横截面长度", "长度", "Length" });
            AutoSelectParam(_cmbWidthParam, new[] { "柱横截面宽度", "宽度", "Width" });

            foreach (var column in columns)
            {
                ElementType et = _doc.GetElement(column.GetTypeId()) as ElementType;
                _allRows.Add(new ColumnRow
                {
                    TypeName = et?.Name ?? "?",
                    ElemId = column.Id.IntegerValue,
                    ElementId = column.Id
                });
            }
            _allRows = _allRows.OrderBy(r => r.TypeName).ThenBy(r => r.ElemId).ToList();

            _suppress = false;
            RefreshPreview();
        }

        private void CollectParams(Element elem, string scope, HashSet<string> seen)
        {
            if (elem == null) return;
            foreach (Parameter p in elem.Parameters)
            {
                if (p?.Definition == null) continue;
                string name = p.Definition.Name;
                string key = name + "|" + scope;
                if (seen.Contains(key)) continue;
                seen.Add(key);

                string display = name;
                try
                {
                    if (p.Id?.IntegerValue >= 0 && Enum.IsDefined(typeof(BuiltInParameter), p.Id.IntegerValue))
                    {
                        var bip = (BuiltInParameter)p.Id.IntegerValue;
                        string loc = LabelUtils.GetLabelFor(bip);
                        if (!string.IsNullOrEmpty(loc)) display = loc;
                    }
                }
                catch { }

                _allParams.Add(new ParamItem { Name = name, DisplayName = display, Scope = scope });
            }
        }

        private void AutoSelectParam(WinComboBox cmb, string[] keywords)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (!(cmb.Items[i] is ParamItem pi)) continue;
                foreach (var kw in keywords)
                {
                    if (pi.DisplayName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pi.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cmb.SelectedIndex = i;
                        return;
                    }
                }
            }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private void RefreshPreview()
        {
            var volParam = _cmbVolumeParam.SelectedItem as ParamItem;
            var topLevelParam = _cmbTopLevelParam.SelectedItem as ParamItem;
            var topOffsetParam = _cmbTopOffsetParam.SelectedItem as ParamItem;
            var baseLevelParam = _cmbBaseLevelParam.SelectedItem as ParamItem;
            var baseOffsetParam = _cmbBaseOffsetParam.SelectedItem as ParamItem;
            var lenParam = _cmbLengthParam.SelectedItem as ParamItem;
            var widParam = _cmbWidthParam.SelectedItem as ParamItem;

            foreach (var row in _allRows)
            {
                Element elem = _doc.GetElement(row.ElementId);
                if (elem == null) { row.Status = "元素不存在"; continue; }
                ElementType et = _doc.GetElement(elem.GetTypeId()) as ElementType;

                row.VolumeCbm = ReadParamAsCbm(elem, et, volParam);
                row.HeightMm = CalculateHeight(elem, et, topLevelParam, topOffsetParam, baseLevelParam, baseOffsetParam);

                if (row.VolumeCbm <= 0) row.Status = "体积缺失";
                else if (row.HeightMm <= 0) row.Status = "高度缺失";
                else
                {
                    double volumeCbmm = row.VolumeCbm * 1000000000;
                    double areaSqmm = volumeCbmm / row.HeightMm;
                    double sideMm = Math.Sqrt(Math.Max(0, areaSqmm));
                    row.CalculatedLength = sideMm;
                    row.CalculatedWidth = sideMm;
                    row.Status = "OK";
                }

                row.CurrentLength = ReadParamDisplay(elem, lenParam);
                row.CurrentWidth = ReadParamDisplay(elem, widParam);
            }

            _viewRows = new BindingList<ColumnRow>(_allRows.ToList());

            _dgv.DataSource = null;
            _dgv.AutoGenerateColumns = false;
            _dgv.Columns.Clear();

            var colCheck = new DataGridViewCheckBoxColumn
            {
                HeaderText = "应用",
                DataPropertyName = "Selected",
                Width = 45,
                ReadOnly = false
            };
            _dgv.Columns.Add(colCheck);
            _dgv.Columns.Add(MakeCol("TypeName", "类型", 150));
            _dgv.Columns.Add(MakeCol("ElemId", "实例ID", 70));
            _dgv.Columns.Add(MakeCol("VolumeCbm", "体积(m³)", 90));
            _dgv.Columns.Add(MakeCol("HeightMm", "高度(mm)", 90));
            _dgv.Columns.Add(MakeCol("CalculatedLength", "计算长度(mm)", 100));
            _dgv.Columns.Add(MakeCol("CalculatedWidth", "计算宽度(mm)", 100));
            _dgv.Columns.Add(MakeCol("CurrentLength", "当前长度", 80));
            _dgv.Columns.Add(MakeCol("CurrentWidth", "当前宽度", 80));
            _dgv.Columns.Add(MakeCol("Status", "状态", 60));

            _dgv.DataSource = _viewRows;

            _btnApply.Enabled = volParam != null && topLevelParam != null && topOffsetParam != null && baseLevelParam != null && baseOffsetParam != null && lenParam != null && widParam != null;
            UpdateStats();
        }

        private double ReadParamAsCbm(Element elem, ElementType et, ParamItem pi)
        {
            if (pi == null) return 0;
            Parameter p = FindParam(elem, pi);
            if (p == null && et != null) p = FindParam(et, pi);
            if (p == null) return 0;
            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    double val = p.AsDouble();
                    if (val > 0) return val * CBM_PER_CUBIC_FT;
                }
                string vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs))
                {
                    double parsed = ParseNumeric(vs);
                    if (parsed > 0) return parsed;
                }
            }
            catch { }
            return 0;
        }

        private double CalculateHeight(Element elem, ElementType et, ParamItem topLevelParam, ParamItem topOffsetParam, ParamItem baseLevelParam, ParamItem baseOffsetParam)
        {
            try
            {
                double topLevelM = ReadLevelElevation(elem, et, topLevelParam);
                double topOffsetMm = ReadParamAsMm(elem, et, topOffsetParam);
                double baseLevelM = ReadLevelElevation(elem, et, baseLevelParam);
                double baseOffsetMm = ReadParamAsMm(elem, et, baseOffsetParam);

                double topTotalMm = topLevelM * 1000 + topOffsetMm;
                double baseTotalMm = baseLevelM * 1000 + baseOffsetMm;
                double heightMm = topTotalMm - baseTotalMm;

                return heightMm > 0 ? heightMm : 0;
            }
            catch { }
            return 0;
        }

        private double ReadLevelElevation(Element elem, ElementType et, ParamItem pi)
        {
            if (pi == null) return 0;
            Parameter p = FindParam(elem, pi);
            if (p == null && et != null) p = FindParam(et, pi);
            if (p == null) return 0;
            try
            {
                if (p.StorageType == StorageType.ElementId)
                {
                    ElementId levelId = p.AsElementId();
                    Level level = _doc.GetElement(levelId) as Level;
                    if (level != null)
                    {
                        return level.Elevation * MM_PER_FOOT / 1000;
                    }
                }
                else if (p.StorageType == StorageType.Double)
                {
                    return p.AsDouble() * MM_PER_FOOT / 1000;
                }
                string vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs))
                {
                    double parsed = ParseNumeric(vs);
                    if (parsed > 0) return parsed;
                }
            }
            catch { }
            return 0;
        }

        private double ReadParamAsMm(Element elem, ElementType et, ParamItem pi)
        {
            if (pi == null) return 0;
            Parameter p = FindParam(elem, pi);
            if (p == null && et != null) p = FindParam(et, pi);
            if (p == null) return 0;
            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    double val = p.AsDouble();
                    if (val > 0) return val * MM_PER_FOOT;
                }
                string vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs))
                {
                    double parsed = ParseNumeric(vs);
                    if (parsed > 0) return parsed;
                }
            }
            catch { }
            return 0;
        }

        private string ReadParamDisplay(Element elem, ParamItem pi)
        {
            if (pi == null) return "";
            Parameter p = FindParam(elem, pi);
            if (p == null) return "";
            try { return p.AsValueString() ?? ""; }
            catch { return ""; }
        }

        private Parameter FindParam(Element owner, ParamItem pi)
        {
            if (owner == null || pi == null) return null;
            foreach (Parameter p in owner.Parameters)
            {
                if (p?.Definition == null) continue;
                if (string.Equals(p.Definition.Name, pi.Name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private double ParseNumeric(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsDigit(c) || c == '.' || c == '-') sb.Append(c);
                else if (sb.Length > 0) break;
            }
            double d;
            if (double.TryParse(sb.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;
            return 0;
        }

        private DataGridViewTextBoxColumn MakeCol(string prop, string header, int w)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = prop,
                Width = w,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Format = prop == "VolumeCbm" ? "0.000" : (prop == "HeightMm" || prop == "CalculatedLength" || prop == "CalculatedWidth" ? "0" : null),
                    Alignment = DataGridViewContentAlignment.MiddleRight
                }
            };
        }

        private void SetRows(bool sel)
        {
            if (_viewRows == null) return;
            foreach (var r in _viewRows) r.Selected = sel;
            _dgv.Invalidate();
            UpdateStats();
        }

        private void UpdateStats()
        {
            if (_viewRows == null) { _lblStat.Text = "正在加载..."; return; }
            int total = _viewRows.Count;
            int sel = _viewRows.Count(r => r.Selected);
            int valid = _viewRows.Count(r => r.Selected && r.Status == "OK");
            _lblStat.Text = $"共 {total} 个异形柱，已选 {sel} 个，有效 {valid} 个";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var lenParam = _cmbLengthParam.SelectedItem as ParamItem;
            var widParam = _cmbWidthParam.SelectedItem as ParamItem;

            if (lenParam == null || widParam == null) { MessageBox.Show("请选择长度和宽度参数。"); return; }

            var selRows = _viewRows.Where(r => r.Selected && r.Status == "OK").ToList();
            if (selRows.Count == 0) { MessageBox.Show("没有可应用的有效行。"); return; }

            string msg = $"即将计算 {selRows.Count} 个异形柱的长宽尺寸\n" +
                         $"长度写入「{lenParam.DisplayName}」，宽度写入「{widParam.DisplayName}」\n\n是否继续？";
            if (MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int ok = 0, fail = 0;
            var diag = new List<string>();

            using (var t = new Transaction(_doc, "异形柱长宽计算"))
            {
                t.Start();
                try
                {
                    foreach (var row in selRows)
                    {
                        Element elem = _doc.GetElement(row.ElementId);
                        if (elem == null) { fail++; diag.Add($"元素不存在: #{row.ElemId}"); continue; }

                        Parameter lengthParam = FindParam(elem, lenParam);
                        Parameter widthParam = FindParam(elem, widParam);

                        bool hasLength = lengthParam != null && !lengthParam.IsReadOnly;
                        bool hasWidth = widthParam != null && !widthParam.IsReadOnly;

                        if (!hasLength && !hasWidth)
                        {
                            fail++; diag.Add($"参数缺失: #{row.ElemId}"); continue;
                        }

                        try
                        {
                            bool written = false;

                            if (hasLength)
                            {
                                bool okLen = lengthParam.SetValueString(row.CalculatedLength.ToString("0"));
                                if (!okLen)
                                {
                                    double valFt = row.CalculatedLength / MM_PER_FOOT;
                                    lengthParam.Set(valFt);
                                }
                                written = true;
                            }

                            if (hasWidth)
                            {
                                bool okWid = widthParam.SetValueString(row.CalculatedWidth.ToString("0"));
                                if (!okWid)
                                {
                                    double valFt = row.CalculatedWidth / MM_PER_FOOT;
                                    widthParam.Set(valFt);
                                }
                                written = true;
                            }

                            if (written)
                            {
                                ok++;
                                if (diag.Count < 10)
                                    diag.Add($"OK: {row.TypeName} #{row.ElemId} L={row.CalculatedLength:0} W={row.CalculatedWidth:0} mm");
                            }
                            else
                            {
                                fail++;
                                diag.Add($"写入失败: #{row.ElemId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            diag.Add($"异常: #{row.ElemId} {ex.Message}");
                        }
                    }

                    var res = t.Commit();
                    if (res != TransactionStatus.Committed)
                    {
                        MessageBox.Show("事务未提交成功（" + res + "）。", "警告");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    MessageBox.Show("事务失败已回滚：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("✓ 异形柱长宽计算完成");
            sb.AppendLine();
            sb.AppendLine($"  成功：{ok} 个");
            sb.AppendLine($"  失败：{fail} 个");
            if (diag.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- 明细（前 20 条）---");
                foreach (var d in diag.Take(20)) sb.AppendLine("  " + d);
                if (diag.Count > 20) sb.AppendLine($"  ... 另 {diag.Count - 20} 条");
            }
            MessageBox.Show(sb.ToString(), "完成", MessageBoxButtons.OK, fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            RefreshPreview();
        }
    }
}