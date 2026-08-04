using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;
using Label = System.Windows.Forms.Label;
using Button = System.Windows.Forms.Button;

namespace MyRevitAddin.BatchFillFamilyParameters
{
    /// <summary>
    /// 批量填族参数（重写版，2026-07-10）
    /// 工作流：选类别 → 勾选族类型 → 参数表里勾「应用」+ 填「新值」→ 点应用
    /// </summary>
    public class BatchFillFamilyParametersDialog : Form
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        // UI
        private ComboBox _cmbCategory;
        private CheckedListBox _clbTypes;
        private TextBox _txtParamFilter;
        private DataGridView _dgvParams;
        private Label _lblStatus;
        private Button _btnApply;
        private Button _btnCancel;

        // Data
        private List<TypeInfo> _allTypes = new List<TypeInfo>();
        private List<ParamInfo> _allParams = new List<ParamInfo>();
        private List<ParamInfo> _filteredParams = new List<ParamInfo>();
        private List<string> _diag = new List<string>();
        private bool _suppressCellEvent;

        public BatchFillFamilyParametersDialog(UIApplication uiApp, Document doc)
        {
            _uiApp = uiApp;
            _doc = doc;
            InitializeComponent();
            Load += OnLoad;
        }

        // ============================================================
        // UI 初始化
        // ============================================================
        private void InitializeComponent()
        {
            Text = "批量填族参数";
            Size = new Size(1280, 760);
            MinimumSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Microsoft YaHei UI", 9F);

            // ===== 顶部：类别筛选 =====
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(235, 240, 248) };
            var lblCat = new Label { Text = "类别筛选：", AutoSize = true, Location = new Point(12, 15) };
            _cmbCategory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(82, 11), Width = 220 };
            _cmbCategory.SelectedIndexChanged += (s, e) => ApplyCategoryFilter();
            var btnRefresh = new Button { Text = "刷新", Location = new Point(312, 10), Width = 70, Height = 28, FlatStyle = FlatStyle.Flat };
            btnRefresh.Click += (s, e) => LoadData();
            var lblSelAll = new Label { Text = "全选", AutoSize = true, Location = new Point(400, 15), Cursor = Cursors.Hand, ForeColor = Color.FromArgb(40, 90, 160), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Underline) };
            lblSelAll.Click += (s, e) => SetAllTypesChecked(true);
            var lblSelNone = new Label { Text = "全不选", AutoSize = true, Location = new Point(440, 15), Cursor = Cursors.Hand, ForeColor = Color.FromArgb(40, 90, 160), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Underline) };
            lblSelNone.Click += (s, e) => SetAllTypesChecked(false);
            var lblCount = new Label { Text = "", AutoSize = true, Location = new Point(500, 15), ForeColor = Color.FromArgb(80, 80, 80) };
            lblCount.Name = "lblLeftCount";
            pnlTop.Controls.AddRange(new Control[] { lblCat, _cmbCategory, btnRefresh, lblSelAll, lblSelNone, lblCount });

            // ===== 主体：左侧类型 + 右侧参数 =====
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 420,
                BackColor = Color.FromArgb(220, 225, 230),
                FixedPanel = FixedPanel.Panel1
            };

            // 左侧：族类型
            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var lblTypes = new Label { Text = "族类型（多选 → 自动合并参数）", Dock = DockStyle.Top, Height = 24, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(40, 90, 160), BackColor = Color.FromArgb(245, 247, 250) };
            _clbTypes = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Font = new Font("Microsoft YaHei UI", 9F), BorderStyle = BorderStyle.FixedSingle };
            _clbTypes.ItemCheck += (s, e) => BeginInvoke((Action)OnTypeSelectionChanged);
            pnlLeft.Controls.Add(_clbTypes);
            pnlLeft.Controls.Add(lblTypes);
            splitMain.Panel1.Controls.Add(pnlLeft);

            // 右侧：参数
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var pnlParamTop = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(245, 247, 250) };
            var lblParam = new Label { Text = "参数（勾「应用」+ 填「新值」）", AutoSize = true, Location = new Point(0, 9), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(40, 90, 160) };
            var lblFilter = new Label { Text = "筛选：", AutoSize = true, Location = new Point(260, 9) };
            _txtParamFilter = new TextBox { Location = new Point(300, 6), Width = 240 };
            _txtParamFilter.TextChanged += (s, e) => ApplyParamFilter();
            pnlParamTop.Controls.AddRange(new Control[] { lblParam, lblFilter, _txtParamFilter });

            _dgvParams = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _dgvParams.Columns.AddRange(
                new DataGridViewCheckBoxColumn { Name = "cApply", HeaderText = "应用", Width = 50 },
                new DataGridViewTextBoxColumn { Name = "cName", HeaderText = "参数名", ReadOnly = true, Width = 180 },
                new DataGridViewTextBoxColumn { Name = "cScope", HeaderText = "作用域", ReadOnly = true, Width = 70 },
                new DataGridViewTextBoxColumn { Name = "cType", HeaderText = "类型", ReadOnly = true, Width = 80 },
                new DataGridViewTextBoxColumn { Name = "cCur", HeaderText = "当前值", ReadOnly = true, Width = 150 },
                new DataGridViewTextBoxColumn { Name = "cNew", HeaderText = "新值", Width = 220 },
                new DataGridViewTextBoxColumn { Name = "cUnit", HeaderText = "单位", ReadOnly = true, Width = 80 },
                new DataGridViewTextBoxColumn { Name = "cAvail", HeaderText = "适用/总数", ReadOnly = true, Width = 90 }
            );
            _dgvParams.CellValueChanged += DgvParams_CellValueChanged;
            _dgvParams.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvParams.IsCurrentCellDirty) _dgvParams.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            pnlRight.Controls.Add(_dgvParams);
            pnlRight.Controls.Add(pnlParamTop);
            splitMain.Panel2.Controls.Add(pnlRight);

            // ===== 底部：状态 + 按钮 =====
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStatus = new Label { AutoSize = true, Location = new Point(12, 16), ForeColor = Color.FromArgb(60, 60, 60) };
            _btnCancel = new Button { Text = "取消", Size = new Size(90, 32), FlatStyle = FlatStyle.Flat };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnApply = new Button { Text = "应用", Size = new Size(90, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 90, 160), ForeColor = Color.White };
            _btnApply.Click += BtnApply_Click;
            pnlBottom.Controls.Add(_lblStatus);
            pnlBottom.Controls.Add(_btnCancel);
            pnlBottom.Controls.Add(_btnApply);
            pnlBottom.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(pnlBottom.Width - 210, 9);
                _btnApply.Location = new Point(pnlBottom.Width - 110, 9);
            };

            // ===== 组合 =====
            Controls.Add(splitMain);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;
        }

        // ============================================================
        // 数据加载
        // ============================================================
        private void OnLoad(object sender, EventArgs e)
        {
            try { LoadData(); }
            catch (Exception ex) { MessageBox.Show("加载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void LoadData()
        {
            _allTypes.Clear();
            _allParams.Clear();
            _filteredParams.Clear();

            // 收集所有可修改的元素（不只是 FamilyInstance，还包括墙/楼板/屋顶/房间等系统族）
            var allElements = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && IsModifiableElement(e))
                .ToList();

            // 按 TypeId 分组
            _allTypes = allElements
                .GroupBy(e => e.GetTypeId() ?? ElementId.InvalidElementId)
                .Select(g =>
                {
                    var first = g.First();
                    var fi = first as FamilyInstance;
                    var et = first.Document.GetElement(first.GetTypeId()) as ElementType;
                    return new TypeInfo
                    {
                        Category = first.Category?.Name ?? "",
                        FamilyName = fi != null ? (fi.Symbol?.Family?.Name ?? "") : (first.Category?.Name ?? ""),
                        TypeName = fi != null ? (fi.Symbol?.Name ?? "") : (et?.Name ?? first.Name ?? ""),
                        InstanceCount = g.Count(),
                        SymbolId = fi?.Symbol?.Id,
                        FamilyId = fi?.Symbol?.Family?.Id,
                        FirstElementId = first.Id,
                        IsFamilyInstance = fi != null,
                        TypeId = first.GetTypeId()
                    };
                })
                .OrderBy(t => t.Category).ThenBy(t => t.FamilyName).ThenBy(t => t.TypeName)
                .ToList();

            // 类别下拉
            var cats = _allTypes.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
            _cmbCategory.BeginUpdate();
            _cmbCategory.Items.Clear();
            _cmbCategory.Items.Add("全部类别 (" + _allTypes.Count + ")");
            foreach (var c in cats) _cmbCategory.Items.Add(c + " (" + _allTypes.Count(t => t.Category == c) + ")");
            _cmbCategory.EndUpdate();
            _cmbCategory.SelectedIndex = 0;

            ApplyCategoryFilter();
        }

        private static bool IsModifiableElement(Element e)
        {
            if (e is View) return false;
            if (e is Autodesk.Revit.DB.Dimension) return false;
            if (e is FamilySymbol) return false;
            if (e is ElementType) return false;
            try { if (e.Parameters.Size == 0) return false; } catch { }
            return true;
        }

        private List<Element> GetElementsForType(TypeInfo row)
        {
            // 统一用 TypeId 过滤（适用于所有构件：族实例/墙/楼板/房间等）
            if (row.TypeId == null || row.TypeId == ElementId.InvalidElementId)
            {
                // 回退：至少返回第一个元素
                var fallback = _doc.GetElement(row.FirstElementId);
                return fallback != null ? new List<Element> { fallback } : new List<Element>();
            }
            return new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.GetTypeId() == row.TypeId)
                .ToList();
        }

        private void ApplyCategoryFilter()
        {
            string sel = _cmbCategory.SelectedItem as string ?? "";
            string catName = sel.Contains(" (") ? sel.Substring(0, sel.IndexOf(" (")) : sel;

            _clbTypes.BeginUpdate();
            _clbTypes.Items.Clear();
            foreach (var t in _allTypes)
            {
                if (catName == "全部类别" || t.Category == catName)
                {
                    _clbTypes.Items.Add(t, false);
                }
            }
            _clbTypes.EndUpdate();

            var lbl = Controls.Find("lblLeftCount", true).FirstOrDefault() as Label;
            if (lbl != null) lbl.Text = "当前显示 " + _clbTypes.Items.Count + " 个类型";

            OnTypeSelectionChanged();
        }

        private void SetAllTypesChecked(bool check)
        {
            _suppressCellEvent = true;
            for (int i = 0; i < _clbTypes.Items.Count; i++) _clbTypes.SetItemChecked(i, check);
            _suppressCellEvent = false;
            OnTypeSelectionChanged();
        }

        private void OnTypeSelectionChanged()
        {
            if (_suppressCellEvent) return;
            // 收集选中的类型
            var selected = _clbTypes.CheckedItems.Cast<TypeInfo>().ToList();

            // 计算参数的并集：跨所有选中类型
            _allParams = BuildParamUnion(selected);
            ApplyParamFilter();
        }

        private List<ParamInfo> BuildParamUnion(List<TypeInfo> types)
        {
            var result = new List<ParamInfo>();
            if (types.Count == 0) return result;

            // 从第一个元素取参数（支持任意元素类型，不只是 FamilySymbol）
            Element firstElem = _doc.GetElement(types[0].FirstElementId);
            if (firstElem == null) return result;

            // 实例参数
            foreach (Parameter p in firstElem.Parameters)
            {
                if (p == null || p.IsReadOnly) continue;
                string defName = p.Definition?.Name;
                if (string.IsNullOrEmpty(defName)) continue;
                if (result.Any(x => x.ParamName.Equals(defName, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(MakeParamInfo(p, "实例", firstElem, defName));
            }

            // 类型参数（从 ElementType 取，适用于所有构件）
            ElementType firstType = _doc.GetElement(firstElem.GetTypeId()) as ElementType;
            if (firstType != null)
            {
                foreach (Parameter p in firstType.Parameters)
                {
                    if (p == null || p.IsReadOnly) continue;
                    string defName = p.Definition?.Name;
                    if (string.IsNullOrEmpty(defName)) continue;
                    if (result.Any(x => x.ParamName.Equals(defName, StringComparison.OrdinalIgnoreCase) && x.Scope == "类型")) continue;
                    result.Add(MakeParamInfo(p, "类型", firstType, defName));
                }
            }

            // 统计每个参数在多少个选中类型中存在
            foreach (var pi in result)
            {
                int avail = 0;
                foreach (var t in types)
                {
                    Element elem = _doc.GetElement(t.FirstElementId);
                    if (elem == null) continue;
                    if (pi.Scope == "类型")
                    {
                        ElementType et = _doc.GetElement(elem.GetTypeId()) as ElementType;
                        if (et != null && HasReadableParameter(et, pi)) avail++;
                    }
                    else
                    {
                        if (HasReadableParameter(elem, pi)) avail++;
                    }
                }
                pi.AvailableCount = avail;
                pi.TotalCount = types.Count;
            }
            return result;
        }

        private bool HasReadableParameter(Element owner, ParamInfo pi)
        {
            if (owner == null) return false;
            foreach (Parameter p in owner.Parameters)
            {
                if (p == null || p.IsReadOnly) continue;
                if (string.Equals(p.Definition?.Name, pi.ParamName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(p.StorageType.ToString(), pi.StorageType, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private ParamInfo MakeParamInfo(Parameter p, string scope, Element owner, string defName)
        {
            string current = "";
            try { current = p.AsValueString(); }
            catch { try { current = p.AsString() ?? ""; } catch { } }

            string builtIn = "";
            Guid guid = Guid.Empty;
            try
            {
                if (p.Definition is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID)
                    builtIn = idef.BuiltInParameter.ToString();
                if (p.Definition is ExternalDefinition extDef)
                {
                    try { guid = extDef.GUID; } catch { }
                }
            }
            catch { }

            return new ParamInfo
            {
                ParamName = defName,
                Scope = scope,
                StorageType = p.StorageType.ToString(),
                Unit = GetUnitText(p),
                CurrentValue = current,
                NewValue = "",
                Apply = false,
                BuiltInIdRaw = builtIn,
                SharedGuid = guid
            };
        }

        private string GetUnitText(Parameter p)
        {
            try
            {
                var def = p.Definition as InternalDefinition;
                if (def == null || def.UnitType == UnitType.UT_Undefined) return "";
                return def.UnitType.ToString().Replace("UT_", "");
            }
            catch { return ""; }
        }

        // ============================================================
        // 参数筛选 / 表格
        // ============================================================
        private void ApplyParamFilter()
        {
            string kw = (_txtParamFilter.Text ?? "").Trim();
            _filteredParams = string.IsNullOrEmpty(kw)
                ? new List<ParamInfo>(_allParams)
                : _allParams.Where(p => p.ParamName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            RefreshGrid();
            UpdateStatus();
        }

        private void RefreshGrid()
        {
            _suppressCellEvent = true;
            _dgvParams.Rows.Clear();
            foreach (var p in _filteredParams)
            {
                int idx = _dgvParams.Rows.Add(
                    p.Apply,
                    p.ParamName,
                    p.Scope,
                    p.StorageType,
                    p.CurrentValue,
                    p.NewValue,
                    p.Unit,
                    p.AvailableCount + "/" + p.TotalCount
                );
                _dgvParams.Rows[idx].Tag = p;

                // 标记不适用的参数（行变灰）
                if (p.AvailableCount == 0)
                {
                    _dgvParams.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
                    _dgvParams.Rows[idx].DefaultCellStyle.ForeColor = Color.Gray;
                    _dgvParams.Rows[idx].Cells["cApply"].ReadOnly = true;
                    _dgvParams.Rows[idx].Cells["cNew"].ReadOnly = true;
                }
            }
            _suppressCellEvent = false;
        }

        private void DgvParams_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressCellEvent || e.RowIndex < 0) return;
            var row = _dgvParams.Rows[e.RowIndex];
            if (!(row.Tag is ParamInfo pi)) return;

            if (_dgvParams.Columns[e.ColumnIndex].Name == "cApply")
            {
                pi.Apply = (bool)(row.Cells["cApply"].Value ?? false);
            }
            else if (_dgvParams.Columns[e.ColumnIndex].Name == "cNew")
            {
                pi.NewValue = (row.Cells["cNew"].Value ?? "").ToString();
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int selTypes = _clbTypes.CheckedItems.Count;
            int applyCount = _allParams.Count(p => p.Apply && !string.IsNullOrWhiteSpace(p.NewValue));
            _lblStatus.Text = "已选 " + selTypes + " 个类型，" + applyCount + " 个参数待应用（共 " + _allParams.Count + " 个参数，已筛 " + _filteredParams.Count + "）";
            _btnApply.Enabled = applyCount > 0 && selTypes > 0;
            _btnApply.BackColor = _btnApply.Enabled ? Color.FromArgb(40, 90, 160) : Color.Gray;
        }

        // ============================================================
        // 应用
        // ============================================================
        private void BtnApply_Click(object sender, EventArgs e)
        {
            try
            {
                _dgvParams.EndEdit();

                var selTypes = _clbTypes.CheckedItems.Cast<TypeInfo>().ToList();
                var toApply = _allParams.Where(p => p.Apply && !string.IsNullOrWhiteSpace(p.NewValue)).ToList();

                if (selTypes.Count == 0) { MessageBox.Show("请先勾选至少一个族类型。"); return; }
                if (toApply.Count == 0) { MessageBox.Show("请勾选「应用」并填写「新值」。"); return; }

                // 确认
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("将对以下 " + selTypes.Count + " 个类型应用 " + toApply.Count + " 个参数：");
                sb.AppendLine();
                foreach (var t in selTypes.Take(8)) sb.AppendLine("  • " + t.FamilyName + " > " + t.TypeName + " (" + t.InstanceCount + " 实例)");
                if (selTypes.Count > 8) sb.AppendLine("  ... 另 " + (selTypes.Count - 8) + " 个");
                sb.AppendLine();
                sb.AppendLine("参数：");
                foreach (var p in toApply.Take(10)) sb.AppendLine("  • " + p.ParamName + " = " + p.NewValue + "  [" + p.Scope + " / " + p.StorageType + "]");
                if (toApply.Count > 10) sb.AppendLine("  ... 另 " + (toApply.Count - 10) + " 个");
                sb.AppendLine();
                sb.AppendLine("继续？");

                if (MessageBox.Show(sb.ToString(), "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                _diag.Clear();
                int typeDone = 0, instDone = 0, fail = 0, okCount = 0;
                var typeParams = toApply.Where(x => x.Scope == "类型").ToList();
                var instParams = toApply.Where(x => x.Scope == "实例").ToList();
                var processedTypes = new HashSet<ElementId>();

                using (var t = new Transaction(_doc, "批量填参数"))
                {
                    t.Start();
                    try
                    {
                        // ===== 类型参数：对每个 ElementType 写一次 =====
                        if (typeParams.Count > 0)
                        {
                            foreach (var row in selTypes)
                            {
                                Element firstElem = _doc.GetElement(row.FirstElementId);
                                if (firstElem == null) continue;
                                ElementType et = _doc.GetElement(firstElem.GetTypeId()) as ElementType;
                                if (et == null) continue;
                                if (!processedTypes.Add(et.Id)) continue;
                                foreach (var pr in typeParams)
                                {
                                    // 直接按名称查找（和 RoomCopyNameToId 一样）
                                    Parameter p = null;
                                    foreach (Parameter pp in et.Parameters)
                                    {
                                        if (pp.Definition != null && string.Equals(pp.Definition.Name, pr.ParamName, StringComparison.OrdinalIgnoreCase))
                                        { p = pp; break; }
                                    }
                                    if (p == null) { _diag.Add("TYPE 缺失: " + pr.ParamName + " (" + et.Name + ")"); fail++; continue; }
                                    if (p.IsReadOnly) { _diag.Add("TYPE 只读: " + pr.ParamName); fail++; continue; }
                                    try { p.Set(pr.NewValue); okCount++; typeDone++; }
                                    catch (Exception ex) { _diag.Add("TYPE 异常: " + pr.ParamName + " " + ex.Message); fail++; }
                                }
                            }
                        }

                        // ===== 实例参数：写所有选中类型的所有元素 =====
                        if (instParams.Count > 0)
                        {
                            int detailCount = 0;
                            foreach (var row in selTypes)
                            {
                                var elements = GetElementsForType(row);
                                var modelElems = new List<Element>();
                                foreach (var el in elements)
                                {
                                    Category c = el.Category;
                                    if (c == null) continue;
                                    if (c.CategoryType != CategoryType.Model) continue;
                                    modelElems.Add(el);
                                }

                                if (modelElems.Count == 0)
                                {
                                    _diag.Add("INST 无模型元素: " + row.Category + " > " + row.TypeName + " (原始 " + elements.Count + " 个)");
                                    fail += instParams.Count;
                                    continue;
                                }
                                foreach (var elem in modelElems)
                                {
                                    string catName = elem.Category?.Name ?? "?";
                                    foreach (var pr in instParams)
                                    {
                                        Parameter p = null;
                                        foreach (Parameter pp in elem.Parameters)
                                        {
                                            if (pp.Definition != null && string.Equals(pp.Definition.Name, pr.ParamName, StringComparison.OrdinalIgnoreCase))
                                            { p = pp; break; }
                                        }
                                        if (p == null) { _diag.Add("INST 缺失: " + pr.ParamName + " (" + catName + ", elem " + elem.Id.IntegerValue + ")"); fail++; continue; }
                                        if (p.IsReadOnly) { _diag.Add("INST 只读: " + pr.ParamName + " (" + catName + ", elem " + elem.Id.IntegerValue + ")"); fail++; continue; }

                                        string beforeVal = "";
                                        try { beforeVal = p.AsString() ?? ""; } catch { try { beforeVal = p.AsValueString() ?? ""; } catch { } }
                                        string defName = p.Definition?.Name ?? "";
                                        string storageType = p.StorageType.ToString();

                                        try
                                        {
                                            p.Set(pr.NewValue);
                                            // 事务内读回验证
                                            string afterVal = "";
                                            try { afterVal = p.AsString() ?? ""; } catch { try { afterVal = p.AsValueString() ?? ""; } catch { } }

                                            if (string.Equals(afterVal, pr.NewValue, StringComparison.OrdinalIgnoreCase) || afterVal == pr.NewValue)
                                            {
                                                okCount++; instDone++;
                                                if (detailCount < 8)
                                                {
                                                    _diag.Add("OK inst: " + catName + " / " + defName + " [" + storageType + "] '" + beforeVal + "' -> '" + afterVal + "' (elem " + elem.Id.IntegerValue + ")");
                                                    detailCount++;
                                                }
                                            }
                                            else
                                            {
                                                fail++;
                                                _diag.Add("VERIFY FAIL: " + catName + " / " + defName + " [" + storageType + "] 写入'" + pr.NewValue + "' 读回'" + afterVal + "'");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _diag.Add("INST 异常: " + catName + " / " + pr.ParamName + " [" + storageType + "] " + ex.Message);
                                            fail++;
                                        }
                                    }
                                }
                            }
                        }
                        var commitRes = t.Commit();
                        if (commitRes != TransactionStatus.Committed)
                        {
                            MessageBox.Show("事务未提交成功（" + commitRes + "），可能被 Revit 回滚。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        // ===== 提交后验证：重新打开元素读回值 =====
                        if (instDone > 0)
                        {
                            int verifyOk = 0, verifyFail = 0;
                            int vCount = 0;
                            foreach (var row in selTypes)
                            {
                                var elements = GetElementsForType(row);
                                var modelElems = new List<Element>();
                                foreach (var el in elements)
                                {
                                    Category c = el.Category;
                                    if (c == null) continue;
                                    if (c.CategoryType != CategoryType.Model) continue;
                                    modelElems.Add(el);
                                }
                                foreach (var elem in modelElems)
                                {
                                    foreach (var pr in instParams)
                                    {
                                        Parameter p = null;
                                        foreach (Parameter pp in elem.Parameters)
                                        {
                                            if (pp.Definition != null && string.Equals(pp.Definition.Name, pr.ParamName, StringComparison.OrdinalIgnoreCase))
                                            { p = pp; break; }
                                        }
                                        if (p == null) continue;
                                        string afterVal = "";
                                        try { afterVal = p.AsString() ?? ""; } catch { try { afterVal = p.AsValueString() ?? ""; } catch { } }
                                        if (string.Equals(afterVal, pr.NewValue, StringComparison.OrdinalIgnoreCase) || afterVal == pr.NewValue)
                                            verifyOk++;
                                        else
                                        {
                                            verifyFail++;
                                            if (vCount < 5)
                                            {
                                                _diag.Add("提交后验证失败: " + elem.Category?.Name + " / " + pr.ParamName + " 期望'" + pr.NewValue + "' 实际'" + afterVal + "' (elem " + elem.Id.IntegerValue + ")");
                                                vCount++;
                                            }
                                        }
                                    }
                                }
                            }
                            _diag.Add("--- 提交后验证 --- 成功 " + verifyOk + " / 失败 " + verifyFail);
                        }
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        MessageBox.Show("事务失败已回滚：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                // ===== 报告 =====
                var msg = new System.Text.StringBuilder();
                msg.AppendLine("✓ 应用完成");
                msg.AppendLine();
                msg.AppendLine("  类型参数：覆盖 " + typeDone + " 处");
                msg.AppendLine("  实例参数：覆盖 " + instDone + " 处");
                msg.AppendLine("  失败：    " + fail + " 处");
                if (_diag.Count > 0)
                {
                    msg.AppendLine();
                    msg.AppendLine("--- 失败明细（前 30 条）---");
                    foreach (var d in _diag.Take(30)) msg.AppendLine("  " + d);
                    if (_diag.Count > 30) msg.AppendLine("  ... 另 " + (_diag.Count - 30) + " 条");
                }
                MessageBox.Show(msg.ToString(), "完成", MessageBoxButtons.OK, fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("应用出错：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // 工具方法
        // ============================================================
        private Parameter FindParameterOnElement(Element owner, ParamInfo pr)
        {
            if (owner == null || pr == null) return null;

            // 1. BuiltInParameter
            if (!string.IsNullOrEmpty(pr.BuiltInIdRaw))
            {
                try
                {
                    var bip = (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), pr.BuiltInIdRaw);
                    var p = owner.get_Parameter(bip);
                    if (p != null) return p;
                }
                catch { }
            }

            // 2. Shared GUID
            if (pr.SharedGuid != Guid.Empty)
            {
                try
                {
                    var p = owner.get_Parameter(pr.SharedGuid);
                    if (p != null) return p;
                }
                catch { }
            }

            // 3. 按名称（放宽：不再要求 StorageType 严格匹配）
            foreach (Parameter pp in owner.Parameters)
            {
                if (pp == null) continue;
                if (string.Equals(pp.Definition?.Name, pr.ParamName, StringComparison.OrdinalIgnoreCase))
                    return pp;
            }

            // 4. LookupParameter（最后兜底）
            var lp = owner.LookupParameter(pr.ParamName);
            if (lp != null) return lp;

            return null;
        }

        private bool SetParamValue(Parameter p, string newVal, string expectedStorageType)
        {
            if (p == null || p.IsReadOnly) return false;

            // 模仿 RoomCopyNameToId：优先直接 p.Set(string)
            try
            {
                p.Set(newVal ?? "");
                return true;
            }
            catch
            {
                // Set(string) 失败，按实际 StorageType 转换
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Integer:
                            {
                                string t = (newVal ?? "").Trim();
                                int iv;
                                if (int.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out iv)) { p.Set(iv); return true; }
                                if (t == "是" || string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) { p.Set(1); return true; }
                                if (t == "否" || string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) { p.Set(0); return true; }
                                return false;
                            }
                        case StorageType.Double:
                            {
                                double dv;
                                if (double.TryParse(newVal, NumberStyles.Any, CultureInfo.InvariantCulture, out dv)) { p.Set(dv); return true; }
                                // 尝试 SetValueString（让 Revit 按单位解析），检查返回值
                                if (p.SetValueString(newVal)) return true;
                                return false;
                            }
                        case StorageType.ElementId:
                            {
                                int eid;
                                if (int.TryParse((newVal ?? "").Trim(), out eid)) { p.Set(new ElementId(eid)); return true; }
                                return false;
                            }
                        default:
                            return false;
                    }
                }
                catch { return false; }
            }
        }
    }

    // ============================================================
    // 数据模型
    // ============================================================
    internal class TypeInfo
    {
        public string Category { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public int InstanceCount { get; set; }
        public ElementId SymbolId { get; set; }
        public ElementId FamilyId { get; set; }
        public ElementId FirstElementId { get; set; }
        public bool IsFamilyInstance { get; set; }
        public ElementId TypeId { get; set; }
        public override string ToString() => Category + " | " + FamilyName + " > " + TypeName + " (" + InstanceCount + ")";
    }

    internal class ParamInfo
    {
        public string ParamName { get; set; }
        public string Scope { get; set; }       // "类型" or "实例"
        public string StorageType { get; set; } // String/Integer/Double/ElementId
        public string Unit { get; set; }
        public string CurrentValue { get; set; }
        public string NewValue { get; set; }
        public bool Apply { get; set; }
        public int AvailableCount { get; set; }
        public int TotalCount { get; set; }
        public string BuiltInIdRaw { get; set; }
        public Guid SharedGuid { get; set; }
    }
}
