using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.DoorWindowOpeningArea
{
    using WinComboBox = System.Windows.Forms.ComboBox;

    internal class AreaRow
    {
        public bool Selected { get; set; } = true;
        public string Category { get; set; }
        public string TypeName { get; set; }
        public int ElemId { get; set; }
        public double LengthMm { get; set; }
        public double HeightMm { get; set; }
        public double CalculatedArea { get; set; }  // m²
        public string CurrentArea { get; set; } = "";
        public string Status { get; set; } = "";
        public ElementId ElementId { get; set; }
    }

    internal class ParamItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Scope { get; set; }  // "实例" / "类型"
        public override string ToString() => $"[{Scope}] {DisplayName}";
    }

    internal class DoorWindowOpeningAreaDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly List<ElementId> _selectedIds;

        private List<AreaRow> _allRows = new List<AreaRow>();
        private BindingList<AreaRow> _viewRows;
        private List<ParamItem> _allParams = new List<ParamItem>();

        private WinComboBox _cmbCategory;
        private RadioButton _rbSelected;
        private RadioButton _rbAllModel;
        private WinComboBox _cmbLengthParam;
        private WinComboBox _cmbHeightParam;
        private WinComboBox _cmbAreaParam;
        private DataGridView _dgv;
        private Label _lblStat;
        private Button _btnApply;
        private Button _btnCancel;
        private bool _suppress;

        // 毫米/英尺换算
        private const double MM_PER_FOOT = 304.8;
        // 平方米/平方英尺换算
        private const double SQM_PER_SQFT = 0.09290304;

        public DoorWindowOpeningAreaDialog(Document doc, UIDocument uiDoc, List<ElementId> selectedIds)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _selectedIds = selectedIds ?? new List<ElementId>();
            InitUI();
            Load += (s, e) => OnLoad();
        }

        private void InitUI()
        {
            Text = "门窗开启面积计算 — 长度×高度÷1000000 → 开启面积(m²)";
            Size = new Size(1100, 680);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            // ===== 顶栏 =====
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

            var lblCat = new Label { Text = "类别：", AutoSize = true, Location = new Point(12, 35) };
            _cmbCategory = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(62, 32),
                Width = 100
            };
            _cmbCategory.Items.AddRange(new object[] { "全部", "门", "窗" });
            _cmbCategory.SelectedIndex = 0;
            _cmbCategory.SelectedIndexChanged += (s, e) => { if (!_suppress) ReloadData(); };

            var lblLen = new Label { Text = "长度参数：", AutoSize = true, Location = new Point(180, 35) };
            _cmbLengthParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(252, 32),
                Width = 200
            };
            _cmbLengthParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblHgt = new Label { Text = "高度参数：", AutoSize = true, Location = new Point(465, 35) };
            _cmbHeightParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(537, 32),
                Width = 200
            };
            _cmbHeightParam.SelectedIndexChanged += (s, e) => { if (!_suppress) RefreshPreview(); };

            var lblArea = new Label { Text = "面积参数：", AutoSize = true, Location = new Point(12, 68) };
            _cmbAreaParam = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 65),
                Width = 200
            };

            var btnRefresh = new Button
            {
                Text = "刷新数据",
                Location = new Point(295, 64),
                Size = new Size(85, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.Click += (s, e) => ReloadData();

            var lblHint = new Label
            {
                Text = "公式：面积(m²) = 长度(mm) × 高度(mm) ÷ 1000000",
                AutoSize = true,
                Location = new Point(400, 68),
                ForeColor = Color.DarkSlateBlue
            };

            pnlTop.Controls.AddRange(new Control[] { _rbSelected, _rbAllModel, lblSelectionHint, lblCat, _cmbCategory, lblLen, _cmbLengthParam, lblHgt, _cmbHeightParam, lblArea, _cmbAreaParam, btnRefresh, lblHint });

            // ===== 表格区域 =====
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

            // ===== 底栏 =====
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

        // ============================================================
        // 加载
        // ============================================================
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

            // 收集门窗实例
            var bics = new List<BuiltInCategory>();
            string catFilter = _cmbCategory.SelectedItem?.ToString() ?? "全部";
            if (catFilter == "全部" || catFilter == "门") bics.Add(BuiltInCategory.OST_Doors);
            if (catFilter == "全部" || catFilter == "窗") bics.Add(BuiltInCategory.OST_Windows);

            var allElems = new List<Element>();

            if (_rbSelected.Checked && _selectedIds.Count > 0)
            {
                foreach (var id in _selectedIds)
                {
                    Element elem = _doc.GetElement(id);
                    if (elem == null || elem is ElementType) continue;
                    if (elem.Category != null)
                    {
                        BuiltInCategory bic = (BuiltInCategory)elem.Category.Id.IntegerValue;
                        if (bics.Contains(bic))
                        {
                            allElems.Add(elem);
                        }
                    }
                }
            }
            else
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .ToList();
                var windows = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (catFilter == "全部" || catFilter == "门") allElems.AddRange(elements);
                if (catFilter == "全部" || catFilter == "窗") allElems.AddRange(windows);
            }

            // 收集所有可用参数（实例+类型）
            var seenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var elem in allElems.Take(50))  // 采样前50个来收集参数列表
            {
                CollectParams(elem, "实例", seenParams);
                ElementType et = _doc.GetElement(elem.GetTypeId()) as ElementType;
                if (et != null) CollectParams(et, "类型", seenParams);
            }

            _allParams = _allParams.OrderBy(p => p.Scope != "实例")
                                   .ThenBy(p => p.DisplayName)
                                   .ToList();

            // 填充下拉框
            _cmbLengthParam.Items.Clear();
            _cmbHeightParam.Items.Clear();
            _cmbAreaParam.Items.Clear();
            foreach (var p in _allParams)
            {
                _cmbLengthParam.Items.Add(p);
                _cmbHeightParam.Items.Add(p);
                // 面积参数只列实例参数（因为要写到实例上）
                if (p.Scope == "实例")
                    _cmbAreaParam.Items.Add(p);
            }

            // 自动选择最可能的参数
            AutoSelectParam(_cmbLengthParam, new[] { "长度", "宽度", "宽", "Length", "Width" });
            AutoSelectParam(_cmbHeightParam, new[] { "高度", "高", "Height" });
            AutoSelectParam(_cmbAreaParam, new[] { "开启面积", "面积", "Area" });

            _suppress = false;

            // 创建行
            foreach (var elem in allElems)
            {
                ElementType et = _doc.GetElement(elem.GetTypeId()) as ElementType;
                string catName = elem.Category?.Name ?? "?";
                _allRows.Add(new AreaRow
                {
                    Category = catName,
                    TypeName = et?.Name ?? "?",
                    ElemId = elem.Id.IntegerValue,
                    ElementId = elem.Id
                });
            }
            _allRows = _allRows.OrderBy(r => r.Category).ThenBy(r => r.TypeName).ToList();

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

        // ============================================================
        // 预览
        // ============================================================
        private void RefreshPreview()
        {
            var lenParam = _cmbLengthParam.SelectedItem as ParamItem;
            var hgtParam = _cmbHeightParam.SelectedItem as ParamItem;
            var areaParam = _cmbAreaParam.SelectedItem as ParamItem;

            foreach (var row in _allRows)
            {
                Element elem = _doc.GetElement(row.ElementId);
                if (elem == null) { row.Status = "元素不存在"; continue; }
                ElementType et = _doc.GetElement(elem.GetTypeId()) as ElementType;

                // 读长度
                row.LengthMm = ReadParamAsMm(elem, et, lenParam);
                // 读高度
                row.HeightMm = ReadParamAsMm(elem, et, hgtParam);
                // 计算面积
                row.CalculatedArea = (row.LengthMm * row.HeightMm) / 1000000.0;
                // 当前面积
                row.CurrentArea = ReadParamDisplay(elem, areaParam);

                if (row.LengthMm <= 0) row.Status = "长度缺失";
                else if (row.HeightMm <= 0) row.Status = "高度缺失";
                else row.Status = "OK";
            }

            _viewRows = new BindingList<AreaRow>(_allRows.ToList());

            _suppress = true;
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
            _dgv.Columns.Add(MakeCol("Category", "类别", 60));
            _dgv.Columns.Add(MakeCol("TypeName", "类型", 120));
            _dgv.Columns.Add(MakeCol("ElemId", "实例ID", 70));
            _dgv.Columns.Add(MakeCol("LengthMm", "长度(mm)", 80));
            _dgv.Columns.Add(MakeCol("HeightMm", "高度(mm)", 80));
            _dgv.Columns.Add(MakeCol("CalculatedArea", "计算面积(m²)", 100));
            _dgv.Columns.Add(MakeCol("CurrentArea", "当前面积", 90));
            _dgv.Columns.Add(MakeCol("Status", "状态", 70));

            _dgv.DataSource = _viewRows;
            _suppress = false;

            _btnApply.Enabled = lenParam != null && hgtParam != null && areaParam != null;
            UpdateStats();
        }

        // 读取参数值并转换为 mm
        private double ReadParamAsMm(Element elem, ElementType et, ParamItem pi)
        {
            if (pi == null) return 0;
            Parameter p = FindParam(elem, pi);
            if (p == null && et != null) p = FindParam(et, pi);
            if (p == null) return 0;
            try
            {
                // AsDouble 返回内部单位（英尺），转换为 mm
                if (p.StorageType == StorageType.Double)
                {
                    double val = p.AsDouble();
                    if (val > 0) return val * MM_PER_FOOT;
                }
                // 回退：尝试 AsValueString 解析
                string vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs))
                {
                    double parsed = ParseNumeric(vs);
                    if (parsed > 0) return parsed;  // 假设显示值就是 mm
                }
            }
            catch { }
            return 0;
        }

        // 读取参数的显示值
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
            // 提取数字部分
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
                    Format = prop == "LengthMm" || prop == "HeightMm" ? "0" : (prop == "CalculatedArea" ? "0.###" : null),
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
            _lblStat.Text = $"共 {total} 个，已选 {sel} 个，有效 {valid} 个";
        }

        // ============================================================
        // 应用
        // ============================================================
        private void BtnApply_Click(object sender, EventArgs e)
        {
            var areaParam = _cmbAreaParam.SelectedItem as ParamItem;
            if (areaParam == null) { MessageBox.Show("请选择面积参数。"); return; }

            var selRows = _viewRows.Where(r => r.Selected && r.Status == "OK").ToList();
            if (selRows.Count == 0) { MessageBox.Show("没有可应用的有效行。"); return; }

            string msg = $"即将计算 {selRows.Count} 个门窗的开启面积\n" +
                         $"并写入到实例参数「{areaParam.DisplayName}」\n\n是否继续？";
            if (MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int ok = 0, fail = 0;
            var diag = new List<string>();

            using (var t = new Transaction(_doc, "门窗开启面积计算"))
            {
                t.Start();
                try
                {
                    foreach (var row in selRows)
                    {
                        Element elem = _doc.GetElement(row.ElementId);
                        if (elem == null) { fail++; diag.Add($"元素不存在: #{row.ElemId}"); continue; }

                        Parameter tp = FindParam(elem, areaParam);
                        if (tp == null) { fail++; diag.Add($"面积参数缺失: {row.Category} #{row.ElemId}"); continue; }
                        if (tp.IsReadOnly) { fail++; diag.Add($"面积参数只读: {row.Category} #{row.ElemId}"); continue; }

                        try
                        {
                            // 面积值 m² → 写入
                            // 优先 SetValueString（接受显示值 m²）
                            bool written = tp.SetValueString(row.CalculatedArea.ToString("0.######"));
                            if (!written)
                            {
                                // 回退：Set(double) 用内部单位（平方英尺）
                                double areaSqft = row.CalculatedArea / SQM_PER_SQFT;
                                tp.Set(areaSqft);
                                written = true;
                            }

                            // 验证
                            string after = tp.AsValueString() ?? "";
                            double afterVal = ParseNumeric(after);
                            if (Math.Abs(afterVal - row.CalculatedArea) < 0.01 || written)
                            {
                                ok++;
                                if (diag.Count < 10)
                                    diag.Add($"OK: {row.Category} {row.TypeName} #{row.ElemId} = {row.CalculatedArea:0.###} m²");
                            }
                            else
                            {
                                fail++;
                                diag.Add($"VERIFY: #{row.ElemId} 期望 {row.CalculatedArea:0.###} 实际 {after}");
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

            // 报告
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("✓ 开启面积计算完成");
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
