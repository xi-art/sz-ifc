using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchParamCopy
{
    using WinComboBox = System.Windows.Forms.ComboBox;

    // ============================================================
    // 数据结构
    // ============================================================

    internal class CatItem
    {
        public BuiltInCategory Bic { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    internal class ParamInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string GroupName { get; set; }
        public string Scope { get; set; }       // "类型" / "实例"
        public string StorageType { get; set; }
        public bool IsReadOnly { get; set; }

        public override string ToString()
        {
            string s = Scope == "类型" ? "类" : "实";
            return $"[{s}] {DisplayName}  ({GroupName})";
        }
    }

    internal class CopyRow
    {
        public bool Selected { get; set; } = true;
        public string TypeName { get; set; }
        public int InstanceCount { get; set; }
        public string SourceVal { get; set; } = "";
        public string TargetOld { get; set; } = "";
        public string TargetNew { get; set; } = "";
        public ElementId TypeId { get; set; }
        public string CategoryName { get; set; }
    }

    // ============================================================
    // 主对话框
    // ============================================================
    internal class BatchParamCopyDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly List<ElementId> _selectedIds;

        private List<CatItem> _categories = new List<CatItem>();
        private List<ParamInfo> _allParams = new List<ParamInfo>();
        private List<CopyRow> _allRows = new List<CopyRow>();
        private BindingList<CopyRow> _viewRows;

        private WinComboBox _cmbCategory;
        private RadioButton _rbSelected;
        private RadioButton _rbAllModel;
        private CheckedListBox _clbSourceParams;
        private CheckedListBox _clbTargetParams;
        private DataGridView _dgv;
        private Label _lblStat;
        private Button _btnApply;
        private Button _btnCancel;
        private bool _suppress;

        // 当前选中的源参数名和目标参数名
        private string _sourceParamName;
        private string _sourceParamScope;
        private string _targetParamName;
        private string _targetParamScope;

        public BatchParamCopyDialog(Document doc, UIDocument uiDoc, List<ElementId> selectedIds)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _selectedIds = selectedIds ?? new List<ElementId>();
            InitUI();
            Load += (s, e) => OnLoad();
        }

        private void InitUI()
        {
            Text = "批量参数互拷 — 选源参数 → 选目标参数 → 复制值";
            Size = new Size(1100, 700);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            // ===== 顶栏 =====
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.FromArgb(235, 240, 248) };
            
            _rbSelected = new RadioButton { Text = "仅处理选中构件", Left = 12, Top = 10, Checked = _selectedIds.Count > 0 };
            _rbAllModel = new RadioButton { Text = "处理全模型", Left = 140, Top = 10, Checked = _selectedIds.Count == 0 };
            _rbSelected.CheckedChanged += (s, e) => { if (!_suppress) OnCategoryChanged(); };
            _rbAllModel.CheckedChanged += (s, e) => { if (!_suppress) OnCategoryChanged(); };

            var lblCat = new Label { Text = "筛选类别：", AutoSize = true, Location = new Point(12, 43) };
            _cmbCategory = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 40),
                Width = 200
            };
            _cmbCategory.SelectedIndexChanged += (s, e) => OnCategoryChanged();

            var btnRefresh = new Button
            {
                Text = "刷新",
                Location = new Point(295, 39),
                Size = new Size(60, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.Click += (s, e) => OnCategoryChanged();
            
            var lblSelectionHint = new Label
            {
                Text = _selectedIds.Count > 0 ? string.Format("已选中 {0} 个构件", _selectedIds.Count) : "未选中任何构件",
                AutoSize = true,
                Location = new Point(370, 43),
                ForeColor = Color.DarkSlateBlue
            };

            pnlTop.Controls.AddRange(new Control[] { _rbSelected, _rbAllModel, lblCat, _cmbCategory, btnRefresh, lblSelectionHint });

            // ===== 左侧：源参数 + 目标参数 =====
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 320,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.FromArgb(220, 225, 230)
            };

            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };

            // 源参数
            var gbSource = new GroupBox
            {
                Text = "① 源参数（取值）— 单选",
                Left = 6, Top = 4, Width = 300, Height = 280,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var lblSourceHint = new Label
            {
                Text = "从这里读取参数值（只能选1个）",
                Left = 8, Top = 18, Width = 284, Height = 16,
                ForeColor = Color.DarkSlateBlue
            };
            _clbSourceParams = new CheckedListBox
            {
                Left = 8, Top = 38, Width = 284, Height = 200,
                CheckOnClick = true,
                IntegralHeight = false,
                Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            _clbSourceParams.ItemCheck += (s, e) =>
            {
                if (_suppress) return;
                // 单选：取消其他勾选
                if (e.NewValue == CheckState.Checked)
                {
                    _suppress = true;
                    for (int i = 0; i < _clbSourceParams.Items.Count; i++)
                        if (i != e.Index) _clbSourceParams.SetItemChecked(i, false);
                    _suppress = false;
                }
                this.BeginInvoke((Action)OnParamSelectionChanged);
            };
            gbSource.Controls.AddRange(new Control[] { lblSourceHint, _clbSourceParams });

            // 目标参数
            var gbTarget = new GroupBox
            {
                Text = "② 目标参数（写入）— 单选",
                Left = 6, Top = 290, Width = 300, Height = 280,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            var lblTargetHint = new Label
            {
                Text = "把源参数的值写到这个参数（只能选1个）",
                Left = 8, Top = 18, Width = 284, Height = 16,
                ForeColor = Color.DarkSlateBlue
            };
            _clbTargetParams = new CheckedListBox
            {
                Left = 8, Top = 38, Width = 284, Height = 200,
                CheckOnClick = true,
                IntegralHeight = false,
                Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            _clbTargetParams.ItemCheck += (s, e) =>
            {
                if (_suppress) return;
                if (e.NewValue == CheckState.Checked)
                {
                    _suppress = true;
                    for (int i = 0; i < _clbTargetParams.Items.Count; i++)
                        if (i != e.Index) _clbTargetParams.SetItemChecked(i, false);
                    _suppress = false;
                }
                this.BeginInvoke((Action)OnParamSelectionChanged);
            };
            gbTarget.Controls.AddRange(new Control[] { lblTargetHint, _clbTargetParams });

            pnlLeft.Controls.Add(gbSource);
            pnlLeft.Controls.Add(gbTarget);
            split.Panel1.Controls.Add(pnlLeft);

            // ===== 右侧：预览表格 =====
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
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

            var pnlRightTop = new Panel { Dock = DockStyle.Top, Height = 36 };
            var btnAll = new Button { Text = "全选", Left = 0, Top = 4, Size = new Size(60, 28), FlatStyle = FlatStyle.Flat };
            btnAll.Click += (s, e) => SetRows(true);
            var btnNone = new Button { Text = "全不选", Left = 66, Top = 4, Size = new Size(70, 28), FlatStyle = FlatStyle.Flat };
            btnNone.Click += (s, e) => SetRows(false);
            pnlRightTop.Controls.AddRange(new Control[] { btnAll, btnNone });

            pnlRight.Controls.Add(_dgv);
            pnlRight.Controls.Add(pnlRightTop);
            split.Panel2.Controls.Add(pnlRight);

            // ===== 底栏 =====
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStat = new Label { AutoSize = true, Location = new Point(12, 16), ForeColor = Color.FromArgb(60, 60, 60), Text = "请先选择源参数和目标参数" };
            _btnCancel = new Button { Text = "取消", Size = new Size(90, 32), FlatStyle = FlatStyle.Flat };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnApply = new Button
            {
                Text = "复制",
                Size = new Size(90, 32),
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

            Controls.Add(split);
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
            try { LoadCategories(); }
            catch (Exception ex) { MessageBox.Show("加载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void LoadCategories()
        {
            _categories.Clear();
            var bics = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_Planting,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_Railings,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_StructuralFoundation,
            };
            foreach (var bic in bics)
            {
                try
                {
                    Category cat = Category.GetCategory(_doc, bic);
                    if (cat != null && cat.AllowsBoundParameters)
                        _categories.Add(new CatItem { Bic = bic, Name = cat.Name });
                }
                catch { }
            }
            _categories = _categories.OrderBy(c => c.Name).ToList();
            _cmbCategory.Items.Clear();
            foreach (var c in _categories) _cmbCategory.Items.Add(c);

            int idx = _categories.FindIndex(c => c.Bic == BuiltInCategory.OST_Stairs);
            if (idx >= 0) _cmbCategory.SelectedIndex = idx;
            else if (_cmbCategory.Items.Count > 0) _cmbCategory.SelectedIndex = 0;
        }

        private void OnCategoryChanged()
        {
            if (!(_cmbCategory.SelectedItem is CatItem cat)) return;
            LoadData(cat);
            RefreshParamLists();
            RefreshPreview();
        }

        // ============================================================
        // 加载类型和参数
        // ============================================================
        private void LoadData(CatItem catItem)
        {
            _allParams.Clear();
            _allRows.Clear();

            List<Element> elements;

            if (_rbSelected.Checked && _selectedIds.Count > 0)
            {
                elements = _selectedIds
                    .Select(id => _doc.GetElement(id))
                    .Where(e => e != null && e.Category != null && e.Category.Id.IntegerValue == (int)catItem.Bic && !(e is ElementType))
                    .ToList();
            }
            else
            {
                elements = new FilteredElementCollector(_doc)
                    .OfCategory(catItem.Bic)
                    .WhereElementIsNotElementType()
                    .ToList();
            }

            var byType = new Dictionary<ElementId, List<Element>>();
            foreach (var elem in elements)
            {
                ElementId tid = elem.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) continue;
                if (!byType.ContainsKey(tid)) byType[tid] = new List<Element>();
                byType[tid].Add(elem);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 类型参数
            foreach (var kv in byType)
            {
                ElementType et = _doc.GetElement(kv.Key) as ElementType;
                if (et == null) continue;
                CollectParams(et, "类型", seen);
            }
            // 实例参数
            foreach (var kv in byType)
            {
                if (kv.Value.Count == 0) continue;
                CollectParams(kv.Value[0], "实例", seen);
            }

            _allParams = _allParams
                .OrderBy(p => p.Scope != "类型")
                .ThenBy(p => p.GroupName)
                .ThenBy(p => p.DisplayName)
                .ToList();

            // 创建行
            foreach (var kv in byType)
            {
                ElementType et = _doc.GetElement(kv.Key) as ElementType;
                if (et == null) continue;
                _allRows.Add(new CopyRow
                {
                    TypeId = et.Id,
                    TypeName = et.Name,
                    CategoryName = catItem.Name,
                    InstanceCount = kv.Value.Count
                });
            }
            _allRows = _allRows.OrderBy(r => r.TypeName).ToList();
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

                string grp = "其他";
                try { if (p.Definition.ParameterGroup != BuiltInParameterGroup.INVALID) grp = LabelUtils.GetLabelFor(p.Definition.ParameterGroup); } catch { }

                _allParams.Add(new ParamInfo
                {
                    Name = name,
                    DisplayName = display,
                    GroupName = grp,
                    Scope = scope,
                    StorageType = p.StorageType.ToString(),
                    IsReadOnly = p.IsReadOnly
                });
            }
        }

        private void RefreshParamLists()
        {
            _suppress = true;
            _clbSourceParams.Items.Clear();
            _clbTargetParams.Items.Clear();
            foreach (var p in _allParams)
            {
                _clbSourceParams.Items.Add(p);
                // 目标参数排除只读
                if (!p.IsReadOnly)
                    _clbTargetParams.Items.Add(p);
            }
            _suppress = false;
        }

        private void OnParamSelectionChanged()
        {
            _sourceParamName = null;
            _sourceParamScope = null;
            _targetParamName = null;
            _targetParamScope = null;

            for (int i = 0; i < _clbSourceParams.Items.Count; i++)
            {
                if (_clbSourceParams.GetItemChecked(i) && _clbSourceParams.Items[i] is ParamInfo pi)
                {
                    _sourceParamName = pi.Name;
                    _sourceParamScope = pi.Scope;
                    break;
                }
            }
            for (int i = 0; i < _clbTargetParams.Items.Count; i++)
            {
                if (_clbTargetParams.GetItemChecked(i) && _clbTargetParams.Items[i] is ParamInfo pi)
                {
                    _targetParamName = pi.Name;
                    _targetParamScope = pi.Scope;
                    break;
                }
            }

            RefreshPreview();
        }

        // ============================================================
        // 预览
        // ============================================================
        private void RefreshPreview()
        {
            bool hasSource = !string.IsNullOrEmpty(_sourceParamName);
            bool hasTarget = !string.IsNullOrEmpty(_targetParamName);

            // 读取值
            var catItem = _cmbCategory.SelectedItem as CatItem;
            if (catItem == null) return;

            var elements = new FilteredElementCollector(_doc)
                .OfCategory(catItem.Bic)
                .WhereElementIsNotElementType()
                .ToList();
            var byType = new Dictionary<ElementId, List<Element>>();
            foreach (var elem in elements)
            {
                ElementId tid = elem.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) continue;
                if (!byType.ContainsKey(tid)) byType[tid] = new List<Element>();
                byType[tid].Add(elem);
            }

            foreach (var row in _allRows)
            {
                // 源值
                if (hasSource)
                {
                    Element owner = GetOwner(row, _sourceParamScope, byType);
                    row.SourceVal = ReadParam(owner, _sourceParamName);
                }
                else row.SourceVal = "";

                // 目标旧值
                if (hasTarget)
                {
                    Element owner = GetOwner(row, _targetParamScope, byType);
                    row.TargetOld = ReadParam(owner, _targetParamName);
                    row.TargetNew = row.SourceVal;
                }
                else { row.TargetOld = ""; row.TargetNew = ""; }
            }

            _viewRows = new BindingList<CopyRow>(_allRows.ToList());

            // 构建表格
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
            _dgv.Columns.Add(MakeCol("TypeName", "类型名称", 160));
            _dgv.Columns.Add(MakeCol("InstanceCount", "实例数", 55));

            string srcHeader = hasSource ? $"源：{GetParamDisplay(_sourceParamName)}" : "源参数";
            _dgv.Columns.Add(MakeCol("SourceVal", srcHeader, 150));

            string tgtHeader = hasTarget ? $"目标：{GetParamDisplay(_targetParamName)}" : "目标参数";
            _dgv.Columns.Add(MakeCol("TargetOld", tgtHeader + "（旧）", 150));
            _dgv.Columns.Add(MakeCol("TargetNew", tgtHeader + "（新）", 150));

            _dgv.DataSource = _viewRows;
            _suppress = false;

            _btnApply.Enabled = hasSource && hasTarget;
            UpdateStats();
        }

        private Element GetOwner(CopyRow row, string scope, Dictionary<ElementId, List<Element>> byType)
        {
            if (scope == "类型")
                return _doc.GetElement(row.TypeId);
            else
            {
                if (byType.TryGetValue(row.TypeId, out var list) && list.Count > 0)
                    return list[0];
                return null;
            }
        }

        private Parameter FindParam(Element owner, string paramName)
        {
            if (owner == null || string.IsNullOrEmpty(paramName)) return null;
            foreach (Parameter p in owner.Parameters)
            {
                if (p?.Definition == null) continue;
                if (string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        // 用 AsValueString 读，返回与 Revit 界面一致的显示值
        private string ReadParam(Element owner, string paramName)
        {
            Parameter p = FindParam(owner, paramName);
            if (p == null) return "";
            try
            {
                // 优先 AsValueString —— 返回显示值（含单位转换），和 Revit 界面一致
                string vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs)) return vs;

                // 回退
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.AsString() ?? "";
                    case StorageType.Integer:
                        return p.AsInteger().ToString();
                    case StorageType.Double:
                        return p.AsDouble().ToString("0.######");
                    case StorageType.ElementId:
                        var eid = p.AsElementId();
                        if (eid == null || eid == ElementId.InvalidElementId) return "";
                        var refEl = owner.Document.GetElement(eid);
                        return refEl != null ? refEl.Name : eid.IntegerValue.ToString();
                    default:
                        return "";
                }
            }
            catch
            {
                try { return p.AsValueString() ?? ""; } catch { return ""; }
            }
        }

        // 用 SetValueString 写，接受显示值，Revit 自动处理单位转换
        private bool SetParamValue(Parameter p, string val)
        {
            if (p == null || p.IsReadOnly) return false;
            if (val == null) val = "";
            try
            {
                // 优先 SetValueString —— 接受显示值，自动处理单位
                bool ok = p.SetValueString(val);
                if (ok) return true;
            }
            catch { }

            // 回退：按 StorageType 直接 Set（内部单位）
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(val);
                        return true;
                    case StorageType.Integer:
                        int iv;
                        if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out iv)) { p.Set(iv); return true; }
                        double dv;
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) { p.Set((int)Math.Round(dv)); return true; }
                        return false;
                    case StorageType.Double:
                        double d;
                        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) { p.Set(d); return true; }
                        return false;
                    case StorageType.ElementId:
                        int idv;
                        if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out idv)) { p.Set(new ElementId(idv)); return true; }
                        return false;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // 验证：比较显示值，对数值型放宽（容差比较）
        private bool VerifyValue(string expected, string actual, StorageType st)
        {
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return true;
            // 数值型：尝试解析后容差比较
            if (st == StorageType.Double || st == StorageType.Integer)
            {
                double e, a;
                if (double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out e) &&
                    double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out a))
                {
                    if (Math.Abs(e - a) < 0.01) return true;
                }
            }
            return false;
        }

        private string GetParamDisplay(string name)
        {
            var pi = _allParams.FirstOrDefault(p => p.Name == name);
            return pi?.DisplayName ?? name;
        }

        private DataGridViewTextBoxColumn MakeCol(string prop, string header, int w)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, DataPropertyName = prop, Width = w, ReadOnly = true };
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
            if (_viewRows == null) { _lblStat.Text = "请先选择源参数和目标参数"; return; }
            int total = _viewRows.Count;
            int sel = _viewRows.Count(r => r.Selected);
            bool ready = !string.IsNullOrEmpty(_sourceParamName) && !string.IsNullOrEmpty(_targetParamName);

            if (!ready)
                _lblStat.Text = $"共 {total} 个类型 — 请选择源参数和目标参数";
            else
                _lblStat.Text = $"共 {total} 个类型，已选 {sel} — 将把「{GetParamDisplay(_sourceParamName)}」的值复制到「{GetParamDisplay(_targetParamName)}」";
        }

        // ============================================================
        // 应用
        // ============================================================
        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_sourceParamName) || string.IsNullOrEmpty(_targetParamName))
            { MessageBox.Show("请先选择源参数和目标参数。"); return; }

            var selRows = _viewRows.Where(r => r.Selected).ToList();
            if (selRows.Count == 0) { MessageBox.Show("请至少勾选一个类型。"); return; }

            // 获取当前类别
            var catItem = _cmbCategory.SelectedItem as CatItem;
            if (catItem == null) return;

            // 收集元素
            var elements = new FilteredElementCollector(_doc)
                .OfCategory(catItem.Bic)
                .WhereElementIsNotElementType()
                .ToList();
            var byType = new Dictionary<ElementId, List<Element>>();
            foreach (var elem in elements)
            {
                ElementId tid = elem.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) continue;
                if (!byType.ContainsKey(tid)) byType[tid] = new List<Element>();
                byType[tid].Add(elem);
            }

            // 确认
            string msg = $"即将把「{GetParamDisplay(_sourceParamName)}」({_sourceParamScope}) 的值\n" +
                         $"复制到「{GetParamDisplay(_targetParamName)}」({_targetParamScope})\n" +
                         $"共 {selRows.Count} 个类型。\n\n是否继续？";
            if (MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int okType = 0, failType = 0;
            int instAffected = 0;
            var diag = new List<string>();
            bool srcIsType = _sourceParamScope == "类型";
            bool tgtIsType = _targetParamScope == "类型";

            using (var t = new Transaction(_doc, "参数互拷"))
            {
                t.Start();
                try
                {
                    foreach (var row in selRows)
                    {
                        if (tgtIsType)
                        {
                            // ===== 目标是类型参数：写 ElementType =====
                            ElementType et = _doc.GetElement(row.TypeId) as ElementType;
                            if (et == null) { diag.Add($"目标类型不存在: {row.TypeName}"); failType++; continue; }

                            Parameter tp = FindParam(et, _targetParamName);
                            if (tp == null) { diag.Add($"目标参数缺失: {row.TypeName} / {_targetParamName}"); failType++; continue; }
                            if (tp.IsReadOnly) { diag.Add($"目标只读: {row.TypeName} / {_targetParamName}"); failType++; continue; }

                            Element srcOwner = GetOwner(row, _sourceParamScope, byType);
                            if (srcOwner == null) { diag.Add($"源不存在: {row.TypeName}"); failType++; continue; }
                            string srcVal = ReadParam(srcOwner, _sourceParamName);

                            try
                            {
                                bool written = SetParamValue(tp, srcVal);
                                if (!written) { failType++; diag.Add($"写入失败: {row.TypeName} {_targetParamName}"); continue; }

                                string after = ReadParam(et, _targetParamName);
                                if (VerifyValue(srcVal, after, tp.StorageType))
                                {
                                    okType++;
                                    if (diag.Count < 10)
                                        diag.Add($"OK[类型→类型]: {row.TypeName} '{row.TargetOld}' → '{after}'");
                                }
                                else
                                {
                                    failType++;
                                    diag.Add($"VERIFY[类型→类型]: {row.TypeName} 期望'{srcVal}' 实际'{after}' [{tp.StorageType}]");
                                }
                            }
                            catch (Exception ex)
                            {
                                failType++;
                                diag.Add($"异常[类型→类型]: {row.TypeName} {ex.Message}");
                            }
                        }
                        else
                        {
                            // ===== 目标是实例参数：写该类型下所有实例 =====
                            if (!byType.TryGetValue(row.TypeId, out var instList) || instList.Count == 0)
                            {
                                diag.Add($"无实例: {row.TypeName}");
                                failType++;
                                continue;
                            }

                            int typeOk = 0, typeFail = 0;
                            int detailCount = 0;

                            if (srcIsType)
                            {
                                // --- 源是类型：所有实例赋同一个值 ---
                                ElementType srcEt = _doc.GetElement(row.TypeId) as ElementType;
                                if (srcEt == null) { diag.Add($"源类型不存在: {row.TypeName}"); failType++; continue; }
                                string srcVal = ReadParam(srcEt, _sourceParamName);

                                foreach (var elem in instList)
                                {
                                    Parameter tp = FindParam(elem, _targetParamName);
                                    if (tp == null) { typeFail++; continue; }
                                    if (tp.IsReadOnly) { typeFail++; continue; }

                                    try
                                    {
                                        bool written = SetParamValue(tp, srcVal);
                                        if (!written) { typeFail++; continue; }

                                        string after = ReadParam(elem, _targetParamName);
                                        if (VerifyValue(srcVal, after, tp.StorageType))
                                        {
                                            typeOk++;
                                            instAffected++;
                                            if (detailCount < 3 && diag.Count < 10)
                                            {
                                                diag.Add($"OK[类型→实例]: {row.TypeName} #{elem.Id.IntegerValue} → '{after}'");
                                                detailCount++;
                                            }
                                        }
                                        else { typeFail++; }
                                    }
                                    catch { typeFail++; }
                                }
                            }
                            else
                            {
                                // --- 源是实例：每个实例读自己的源值，写自己的目标值 ---
                                foreach (var elem in instList)
                                {
                                    Parameter sp = FindParam(elem, _sourceParamName);
                                    if (sp == null) { typeFail++; continue; }
                                    string srcVal = ReadParam(elem, _sourceParamName);

                                    Parameter tp = FindParam(elem, _targetParamName);
                                    if (tp == null) { typeFail++; continue; }
                                    if (tp.IsReadOnly) { typeFail++; continue; }

                                    try
                                    {
                                        bool written = SetParamValue(tp, srcVal);
                                        if (!written) { typeFail++; continue; }

                                        string after = ReadParam(elem, _targetParamName);
                                        if (VerifyValue(srcVal, after, tp.StorageType))
                                        {
                                            typeOk++;
                                            instAffected++;
                                            if (detailCount < 3 && diag.Count < 10)
                                            {
                                                diag.Add($"OK[实例→实例]: {row.TypeName} #{elem.Id.IntegerValue} '{row.TargetOld}' → '{after}'");
                                                detailCount++;
                                            }
                                        }
                                        else { typeFail++; }
                                    }
                                    catch { typeFail++; }
                                }
                            }

                            if (typeOk > 0)
                            {
                                okType++;
                                if (typeFail > 0 && diag.Count < 20)
                                    diag.Add($"  {row.TypeName}: 成功 {typeOk} / 失败 {typeFail} 个实例");
                            }
                            else
                            {
                                failType++;
                                diag.Add($"失败[实例]: {row.TypeName} 所有 {instList.Count} 个实例均未写入");
                            }
                        }
                    }

                    var res = t.Commit();
                    if (res != TransactionStatus.Committed)
                    { MessageBox.Show("事务未提交成功（" + res + "）。", "警告"); return; }
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
            sb.AppendLine("✓ 复制完成");
            sb.AppendLine();
            sb.AppendLine($"  成功类型：{okType} 个");
            sb.AppendLine($"  失败类型：{failType} 个");
            if (!tgtIsType)
                sb.AppendLine($"  影响实例：{instAffected} 个");
            if (diag.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- 明细（前 20 条）---");
                foreach (var d in diag.Take(20)) sb.AppendLine("  " + d);
                if (diag.Count > 20) sb.AppendLine($"  ... 另 {diag.Count - 20} 条");
            }
            MessageBox.Show(sb.ToString(), "完成", MessageBoxButtons.OK, failType == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            RefreshPreview();
        }
    }
}
