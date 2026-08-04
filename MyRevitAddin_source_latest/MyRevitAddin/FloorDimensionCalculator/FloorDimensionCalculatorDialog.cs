using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MyRevitAddin.FloorDimensionCalculator
{
    using WinComboBox = System.Windows.Forms.ComboBox;

    internal class DimensionRow
    {
        public bool Selected { get; set; } = true;
        public string TypeName { get; set; }
        public int ElemId { get; set; }
        public double AreaSqm { get; set; }
        public double PerimeterMm { get; set; }
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

    internal class FloorDimensionCalculatorDialog : Form
    {
        private readonly Document _doc;

        private List<DimensionRow> _allRows = new List<DimensionRow>();
        private BindingList<DimensionRow> _viewRows;
        private List<ParamItem> _allParams = new List<ParamItem>();

        private WinComboBox _cmbAreaParam;
        private WinComboBox _cmbPerimeterParam;
        private WinComboBox _cmbLengthParam;
        private WinComboBox _cmbWidthParam;
        private DataGridView _dgv;
        private Label _lblStat;
        private Button _btnApply;
        private Button _btnCancel;
        private Button _btnRefresh;
        private bool _suppress;

        private const double MM_PER_FOOT = 304.8;
        private const double SQM_PER_SQFT = 0.09290304;

        public FloorDimensionCalculatorDialog(Document doc)
        {
            _doc = doc;
            InitUI();
            Load += (s, e) => OnLoad();
        }

        private void InitUI()
        {
            Text = "楼板长宽计算 — 面积+周长推算矩形尺寸";
            Size = new Size(1100, 680);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(235, 240, 248) };

            var lblArea = new Label { Text = "面积参数：", AutoSize = true, Location = new Point(12, 12) };
            _cmbAreaParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 9),
                Width = 200
            };
            _cmbAreaParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblPeri = new Label { Text = "周长参数：", AutoSize = true, Location = new Point(300, 12) };
            _cmbPerimeterParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(380, 9),
                Width = 200
            };
            _cmbPerimeterParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblLen = new Label { Text = "长度参数：", AutoSize = true, Location = new Point(12, 46) };
            _cmbLengthParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 43),
                Width = 200
            };
            _cmbLengthParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblWid = new Label { Text = "宽度参数：", AutoSize = true, Location = new Point(300, 46) };
            _cmbWidthParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(380, 43),
                Width = 200
            };
            _cmbWidthParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            _btnRefresh = new Button
            {
                Text = "刷新数据",
                Location = new Point(595, 42),
                Size = new Size(85, 28),
                FlatStyle = FlatStyle.Flat
            };
            _btnRefresh.Click += (s, e) => ReloadData();

            var lblHint = new Label
            {
                Text = "公式：L=[P/2+√((P/2)²-4A)]/2，W=A/L（A=面积，P=周长）",
                AutoSize = true,
                Location = new Point(700, 46),
                ForeColor = Color.DarkSlateBlue
            };

            pnlTop.Controls.AddRange(new Control[] { lblArea, _cmbAreaParam, lblPeri, _cmbPerimeterParam, lblLen, _cmbLengthParam, lblWid, _cmbWidthParam, _btnRefresh, lblHint });

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

            var floors = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .Cast<Floor>()
                .ToList();

            var seenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var floor in floors.Take(20))
            {
                CollectParams(floor, "实例", seenParams);
                ElementType et = _doc.GetElement(floor.GetTypeId()) as ElementType;
                if (et != null) CollectParams(et, "类型", seenParams);
            }

            _allParams = _allParams.OrderBy(p => p.Scope != "实例")
                                   .ThenBy(p => p.DisplayName)
                                   .ToList();

            _cmbAreaParam.Items.Clear();
            _cmbPerimeterParam.Items.Clear();
            _cmbLengthParam.Items.Clear();
            _cmbWidthParam.Items.Clear();
            foreach (var p in _allParams)
            {
                _cmbAreaParam.Items.Add(p);
                _cmbPerimeterParam.Items.Add(p);
                if (p.Scope == "实例")
                {
                    _cmbLengthParam.Items.Add(p);
                    _cmbWidthParam.Items.Add(p);
                }
            }

            AutoSelectParam(_cmbAreaParam, new[] { "面积", "Area" });
            AutoSelectParam(_cmbPerimeterParam, new[] { "周长", "Perimeter" });
            AutoSelectParam(_cmbLengthParam, new[] { "长度", "Length" });
            AutoSelectParam(_cmbWidthParam, new[] { "宽度", "Width" });

            foreach (var floor in floors)
            {
                ElementType et = _doc.GetElement(floor.GetTypeId()) as ElementType;
                _allRows.Add(new DimensionRow
                {
                    TypeName = et?.Name ?? "?",
                    ElemId = floor.Id.IntegerValue,
                    ElementId = floor.Id
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
            var areaParam = _cmbAreaParam.SelectedItem as ParamItem;
            var periParam = _cmbPerimeterParam.SelectedItem as ParamItem;
            var lenParam = _cmbLengthParam.SelectedItem as ParamItem;
            var widParam = _cmbWidthParam.SelectedItem as ParamItem;

            foreach (var row in _allRows)
            {
                Element elem = _doc.GetElement(row.ElementId);
                if (elem == null) { row.Status = "元素不存在"; continue; }
                ElementType et = _doc.GetElement(elem.GetTypeId()) as ElementType;

                row.AreaSqm = ReadParamAsSqm(elem, et, areaParam);
                row.PerimeterMm = ReadParamAsMm(elem, et, periParam);

                double areaSqMm = row.AreaSqm * 1000000;
                double halfPerimeter = row.PerimeterMm / 2;

                if (row.AreaSqm <= 0) row.Status = "面积缺失";
                else if (row.PerimeterMm <= 0) row.Status = "周长缺失";
                else if (halfPerimeter * halfPerimeter < 4 * areaSqMm - 1) row.Status = "非矩形";
                else
                {
                    double discriminant = halfPerimeter * halfPerimeter - 4 * areaSqMm;
                    double sqrtDisc = Math.Sqrt(Math.Max(0, discriminant));
                    row.CalculatedLength = (halfPerimeter + sqrtDisc) / 2;
                    row.CalculatedWidth = (halfPerimeter - sqrtDisc) / 2;
                    row.Status = "OK";
                }

                row.CurrentLength = ReadParamDisplay(elem, lenParam);
                row.CurrentWidth = ReadParamDisplay(elem, widParam);
            }

            _viewRows = new BindingList<DimensionRow>(_allRows.ToList());

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
            _dgv.Columns.Add(MakeCol("AreaSqm", "面积(m²)", 90));
            _dgv.Columns.Add(MakeCol("PerimeterMm", "周长(mm)", 90));
            _dgv.Columns.Add(MakeCol("CalculatedLength", "计算长度(mm)", 100));
            _dgv.Columns.Add(MakeCol("CalculatedWidth", "计算宽度(mm)", 100));
            _dgv.Columns.Add(MakeCol("CurrentLength", "当前长度", 80));
            _dgv.Columns.Add(MakeCol("CurrentWidth", "当前宽度", 80));
            _dgv.Columns.Add(MakeCol("Status", "状态", 60));

            _dgv.DataSource = _viewRows;

            _btnApply.Enabled = areaParam != null && periParam != null && lenParam != null && widParam != null;
            UpdateStats();
        }

        private double ReadParamAsSqm(Element elem, ElementType et, ParamItem pi)
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
                    if (val > 0) return val * SQM_PER_SQFT;
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
                    Format = prop == "AreaSqm" ? "0.00" : (prop == "PerimeterMm" || prop == "CalculatedLength" || prop == "CalculatedWidth" ? "0" : null),
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
            _lblStat.Text = $"共 {total} 个楼板，已选 {sel} 个，有效 {valid} 个";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var lenParam = _cmbLengthParam.SelectedItem as ParamItem;
            var widParam = _cmbWidthParam.SelectedItem as ParamItem;

            if (lenParam == null || widParam == null) { MessageBox.Show("请选择长度和宽度参数。"); return; }

            var selRows = _viewRows.Where(r => r.Selected && r.Status == "OK").ToList();
            if (selRows.Count == 0) { MessageBox.Show("没有可应用的有效行。"); return; }

            string msg = $"即将计算 {selRows.Count} 个楼板的长宽尺寸\n" +
                         $"长度写入「{lenParam.DisplayName}」，宽度写入「{widParam.DisplayName}」\n\n是否继续？";
            if (MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int ok = 0, fail = 0;
            var diag = new List<string>();

            using (var t = new Transaction(_doc, "楼板长宽计算"))
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
            sb.AppendLine("✓ 楼板长宽计算完成");
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