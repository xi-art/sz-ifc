using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchEditElementParams
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

    internal class ParamEditInfo
    {
        public string Name { get; set; }
        public string GroupName { get; set; }
        public string Scope { get; set; }       // "实例" / "类型"
        public string StorageType { get; set; }
        public bool IsReadOnly { get; set; }
        public bool Apply { get; set; } = false;
        public string NewValue { get; set; } = "";

        public override string ToString()
        {
            string s = Scope == "类型" ? "[类]" : "[实]";
            return string.Format("{0} {1}  ({2})", s, Name, GroupName);
        }
    }

    internal class ElementRow
    {
        public bool Selected { get; set; } = true;
        public ElementId ElementId { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string LevelName { get; set; }
        public ElementId TypeId { get; set; }
    }

    // ============================================================
    // 主对话框
    // ============================================================

    internal class BatchEditElementParamsDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly List<ElementId> _selectedIds;

        private List<CatItem> _categories = new List<CatItem>();
        private BindingList<ParamEditInfo> _paramList = new BindingList<ParamEditInfo>();
        private List<ElementRow> _allRows = new List<ElementRow>();
        private BindingList<ElementRow> _viewRows;

        private WinComboBox _cmbCategory;
        private RadioButton _rbSelected;
        private RadioButton _rbAllModel;
        private DataGridView _dgvParams;     // 左侧：参数 + 新值输入
        private DataGridView _dgvElements;   // 右侧：元素列表
        private SplitContainer _split;
        private Label _lblStat;
        private Button _btnApply;
        private Button _btnCancel;
        private Button _btnSelAll;
        private Button _btnSelNone;
        private bool _suppress;

        public BatchEditElementParamsDialog(Document doc, UIDocument uiDoc, List<ElementId> selectedIds)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _selectedIds = selectedIds ?? new List<ElementId>();
            InitUI();
            Load += (s, e) => OnLoad();
        }

        public EditDialogResult ShowDialogEx(IWin32Window owner)
        {
            var dr = owner != null ? ShowDialog(owner) : ShowDialog();
            return BuildResult(dr == DialogResult.OK);
        }

        // ============================================================
        // UI 初始化
        // ============================================================
        private void InitUI()
        {
            Text = "批量修改构件属性 — 选类别→选参数→填值→一键应用";
            Size = new Size(1280, 760);
            MinimumSize = new Size(1100, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            // ===== 顶栏 =====
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.FromArgb(235, 240, 248) };

            _rbSelected = new RadioButton { Text = "仅处理选中构件", Left = 12, Top = 10, Checked = _selectedIds.Count > 0 };
            _rbAllModel = new RadioButton { Text = "处理全模型", Left = 140, Top = 10, Checked = _selectedIds.Count == 0 };
            _rbSelected.CheckedChanged += (s, e) => { if (!_suppress) ReloadData(); };
            _rbAllModel.CheckedChanged += (s, e) => { if (!_suppress) ReloadData(); };

            var lblCat = new Label { Text = "筛选类别：", AutoSize = true, Location = new Point(12, 43) };
            _cmbCategory = new WinComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 40),
                Width = 220
            };
            _cmbCategory.SelectedIndexChanged += (s, e) => ReloadData();

            var btnRefresh = new Button
            {
                Text = "刷新",
                Location = new Point(310, 39),
                Size = new Size(60, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.Click += (s, e) => ReloadData();

            var lblHint = new Label
            {
                Text = _selectedIds.Count > 0
                    ? string.Format("当前已选中 {0} 个构件", _selectedIds.Count)
                    : "未选中任何构件（可先选构件再打开此窗口）",
                AutoSize = true,
                Location = new Point(390, 43),
                ForeColor = Color.DarkSlateBlue
            };

            pnlTop.Controls.AddRange(new Control[] { _rbSelected, _rbAllModel, lblCat, _cmbCategory, btnRefresh, lblHint });

            // ===== 中部分隔：左参数 / 右元素 =====
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.FromArgb(220, 225, 230),
                Panel1MinSize = 380,
                Panel2MinSize = 400
            };
            // 延迟在 Shown 后设置 SplitterDistance（等实际 ClientSize 生效后再 clamp）
            this.Shown += (s, e) =>
            {
                try
                {
                    int w = _split.ClientSize.Width;
                    int sd = 480;
                    int min = _split.Panel1MinSize;
                    int max = w - _split.Panel2MinSize - _split.SplitterWidth;
                    if (max < min) max = min;
                    if (sd < min) sd = min;
                    if (sd > max) sd = max;
                    _split.SplitterDistance = sd;
                }
                catch { }
            };

            // ===== 左侧：参数编辑表 =====
            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var gbParams = new GroupBox
            {
                Text = "① 参数设置（勾选要修改的参数，在右侧填新值）",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            _dgvParams = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(220, 225, 230),
                Font = new Font("Microsoft YaHei UI", 9F),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(248, 250, 252) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(220, 230, 245),
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                EnableHeadersVisualStyles = false
            };

            // 参数表三列：勾选 / 参数信息 / 新值
            var colApply = new DataGridViewCheckBoxColumn
            {
                Name = "Apply",
                HeaderText = "应用",
                DataPropertyName = "Apply",
                Width = 50,
                ReadOnly = false,
                TrueValue = true,
                FalseValue = false
            };
            var colInfo = new DataGridViewTextBoxColumn
            {
                Name = "Param",
                HeaderText = "参数名称",
                DataPropertyName = "",
                Width = 280,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var colVal = new DataGridViewTextBoxColumn
            {
                Name = "NewValue",
                HeaderText = "新值",
                DataPropertyName = "NewValue",
                Width = 130,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            _dgvParams.Columns.AddRange(new DataGridViewColumn[] { colApply, colInfo, colVal });
            _dgvParams.CellValueChanged += DgvParams_CellValueChanged;
            _dgvParams.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvParams.IsCurrentCellDirty) _dgvParams.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _dgvParams.DataBindingComplete += (s, e) => UpdateStat();
            _dgvParams.ReadOnly = false;

            gbParams.Controls.Add(_dgvParams);
            pnlLeft.Controls.Add(gbParams);
            _split.Panel1.Controls.Add(pnlLeft);

            // ===== 右侧：元素列表 =====
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0) };
            var pnlRightTop = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(2) };
            var gbRight = new GroupBox
            {
                Text = "② 选择要修改的构件（勾选行）",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            _btnSelAll = new Button { Text = "全选", Left = 6, Top = 6, Size = new Size(70, 28), FlatStyle = FlatStyle.Flat };
            _btnSelNone = new Button { Text = "全不选", Left = 82, Top = 6, Size = new Size(70, 28), FlatStyle = FlatStyle.Flat };
            _btnSelAll.Click += (s, e) => SetAllRows(true);
            _btnSelNone.Click += (s, e) => SetAllRows(false);
            pnlRightTop.Controls.AddRange(new Control[] { _btnSelAll, _btnSelNone });

            _dgvElements = new DataGridView
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
                Font = new Font("Microsoft YaHei UI", 9F),
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
                Name = "Selected",
                HeaderText = "选择",
                DataPropertyName = "Selected",
                Width = 55,
                ReadOnly = false,
                TrueValue = true,
                FalseValue = false
            };
            var colName = new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "构件名称",
                DataPropertyName = "Name",
                Width = 200,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colType = new DataGridViewTextBoxColumn
            {
                Name = "TypeName",
                HeaderText = "类型",
                DataPropertyName = "TypeName",
                Width = 170,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colLevel = new DataGridViewTextBoxColumn
            {
                Name = "LevelName",
                HeaderText = "标高/族",
                DataPropertyName = "LevelName",
                Width = 160,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            var colId = new DataGridViewTextBoxColumn
            {
                Name = "ElementId",
                HeaderText = "ID",
                DataPropertyName = "ElementId",
                Width = 90,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            };

            _dgvElements.Columns.AddRange(new DataGridViewColumn[] { colSel, colName, colType, colLevel, colId });
            _dgvElements.CellValueChanged += (s, e) => UpdateStat();
            _dgvElements.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgvElements.IsCurrentCellDirty) _dgvElements.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _dgvElements.DataBindingComplete += (s, e) => UpdateStat();

            gbRight.Controls.Add(pnlRightTop);
            gbRight.Controls.Add(_dgvElements);
            // 修复：Dock 顺序，先加的最靠里
            gbRight.Controls.SetChildIndex(_dgvElements, 0);
            pnlRight.Controls.Add(gbRight);
            _split.Panel2.Controls.Add(pnlRight);

            // ===== 底栏 =====
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStat = new Label
            {
                AutoSize = false,
                Left = 12,
                Top = 18,
                Width = 760,
                Height = 22,
                ForeColor = Color.DarkSlateGray,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _btnCancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                FlatStyle = FlatStyle.Flat
            };
            _btnApply = new Button
            {
                Text = "应用修改",
                Size = new Size(110, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.FromArgb(70, 130, 200),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _btnCancel.Location = new Point(ClientSize.Width - 190, 12);
            _btnApply.Location = new Point(ClientSize.Width - 95, 12);
            _btnApply.Click += (s, e) =>
            {
                if (!ValidateBeforeApply()) return;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            pnlBottom.Controls.AddRange(new Control[] { _lblStat, _btnApply, _btnCancel });

            // 汇总
            Controls.Add(_split);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
        }

        // ============================================================
        // 加载
        // ============================================================
        private void OnLoad()
        {
            InitCategories();
            ReloadData();
        }

        private void InitCategories()
        {
            var cats = new List<CatItem>
            {
                new CatItem { Bic = BuiltInCategory.OST_Walls, Name = "墙" },
                new CatItem { Bic = BuiltInCategory.OST_Floors, Name = "楼板" },
                new CatItem { Bic = BuiltInCategory.OST_Roofs, Name = "屋顶" },
                new CatItem { Bic = BuiltInCategory.OST_Ceilings, Name = "天花板" },
                new CatItem { Bic = BuiltInCategory.OST_Columns, Name = "建筑柱" },
                new CatItem { Bic = BuiltInCategory.OST_StructuralColumns, Name = "结构柱" },
                new CatItem { Bic = BuiltInCategory.OST_StructuralFraming, Name = "结构框架(梁/支撑)" },
                new CatItem { Bic = BuiltInCategory.OST_Stairs, Name = "楼梯" },
                new CatItem { Bic = BuiltInCategory.OST_Doors, Name = "门" },
                new CatItem { Bic = BuiltInCategory.OST_Windows, Name = "窗" },
                new CatItem { Bic = BuiltInCategory.OST_Furniture, Name = "家具" },
                new CatItem { Bic = BuiltInCategory.OST_PlumbingFixtures, Name = "卫生器具" },
                new CatItem { Bic = BuiltInCategory.OST_MechanicalEquipment, Name = "机械设备" },
                new CatItem { Bic = BuiltInCategory.OST_ElectricalEquipment, Name = "电气设备" },
                new CatItem { Bic = BuiltInCategory.OST_LightingFixtures, Name = "照明设备" },
                new CatItem { Bic = BuiltInCategory.OST_DuctCurves, Name = "风管" },
                new CatItem { Bic = BuiltInCategory.OST_PipeCurves, Name = "管道" },
                new CatItem { Bic = BuiltInCategory.OST_DuctFitting, Name = "风管管件" },
                new CatItem { Bic = BuiltInCategory.OST_PipeFitting, Name = "管道管件" },
                new CatItem { Bic = BuiltInCategory.OST_DuctAccessory, Name = "风管附件" },
                new CatItem { Bic = BuiltInCategory.OST_PipeAccessory, Name = "管道附件" },
                new CatItem { Bic = BuiltInCategory.OST_Sprinklers, Name = "喷头" },
                new CatItem { Bic = BuiltInCategory.OST_Lines, Name = "线" },
                new CatItem { Bic = BuiltInCategory.OST_GenericModel, Name = "常规模型" },
            };

            // 检查项目中实际存在哪些类别（优化排序）
            var actual = new List<CatItem>();
            foreach (var c in cats)
            {
                try
                {
                    int cnt = new FilteredElementCollector(_doc).OfCategory(c.Bic)
                        .WhereElementIsNotElementType()
                        .Take(1).Count();
                    if (cnt > 0) actual.Add(c);
                }
                catch { }
            }
            // 没出现的也加上，放后面
            foreach (var c in cats)
                if (!actual.Any(x => x.Bic == c.Bic)) actual.Add(c);

            _categories = actual;
            _suppress = true;
            _cmbCategory.Items.Clear();
            foreach (var c in _categories) _cmbCategory.Items.Add(c);
            if (_cmbCategory.Items.Count > 0) _cmbCategory.SelectedIndex = 0;
            _suppress = false;
        }

        private void ReloadData()
        {
            var cat = _cmbCategory.SelectedItem as CatItem;
            if (cat == null) return;

            bool onlySelected = _rbSelected.Checked;
            HashSet<ElementId> selSet = null;
            if (onlySelected && _selectedIds.Count > 0)
                selSet = new HashSet<ElementId>(_selectedIds);

            // 收集元素
            List<Element> elements;
            try
            {
                var col = new FilteredElementCollector(_doc).OfCategory(cat.Bic).WhereElementIsNotElementType();
                if (onlySelected && selSet != null)
                    elements = col.Where(e => selSet.Contains(e.Id)).ToList();
                else
                    elements = col.ToList();
            }
            catch
            {
                elements = new List<Element>();
            }

            // 元素行
            _allRows.Clear();
            foreach (var e in elements)
            {
                var row = new ElementRow
                {
                    Selected = true,
                    ElementId = e.Id,
                    Name = e.Name ?? "",
                    TypeName = "",
                    LevelName = "",
                    TypeId = e.GetTypeId()
                };
                ElementType et = null;
                try { et = _doc.GetElement(e.GetTypeId()) as ElementType; } catch { }
                if (et != null) row.TypeName = et.Name ?? "";

                // 标高
                try
                {
                    Parameter lp = e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                    if (lp != null && lp.StorageType == StorageType.ElementId)
                    {
                        Element lid = _doc.GetElement(lp.AsElementId());
                        if (lid != null) row.LevelName = lid.Name ?? "";
                    }
                    else
                    {
                        Level lev = _doc.GetElement(e.LevelId) as Level;
                        if (lev != null) row.LevelName = lev.Name ?? "";
                    }
                }
                catch
                {
                    try
                    {
                        var fam = e as FamilyInstance;
                        if (fam != null && fam.Symbol != null && fam.Symbol.Family != null)
                            row.LevelName = fam.Symbol.Family.Name ?? "";
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(row.LevelName)) row.LevelName = "-";
                _allRows.Add(row);
            }
            _viewRows = new BindingList<ElementRow>(_allRows);
            _dgvElements.DataSource = null;
            _dgvElements.DataSource = _viewRows;
            if (_dgvElements.Columns.Contains("Selected"))
                _dgvElements.Columns["Selected"].ReadOnly = false;

            // 收集参数（取前几个样本，联合所有可写参数）
            CollectParams(elements, cat);

            UpdateStat();
        }

        private void CollectParams(List<Element> elements, CatItem cat)
        {
            var dict = new Dictionary<string, ParamEditInfo>(StringComparer.OrdinalIgnoreCase);
            int sampleCount = Math.Min(elements.Count, 8);
            for (int i = 0; i < sampleCount; i++)
            {
                var e = elements[i];
                // 实例参数
                foreach (Parameter p in e.Parameters)
                {
                    if (p.IsReadOnly) continue;
                    try
                    {
                        if (p.Definition == null) continue;
                        string key = "I|" + p.Definition.Name;
                        if (dict.ContainsKey(key)) continue;
                        dict[key] = new ParamEditInfo
                        {
                            Name = p.Definition.Name,
                            GroupName = GetGroupName(p),
                            Scope = "实例",
                            StorageType = p.StorageType.ToString(),
                            IsReadOnly = p.IsReadOnly,
                            Apply = false,
                            NewValue = ""
                        };
                    }
                    catch { }
                }
                // 类型参数
                try
                {
                    ElementType et = _doc.GetElement(e.GetTypeId()) as ElementType;
                    if (et != null)
                    {
                        foreach (Parameter p in et.Parameters)
                        {
                            if (p.IsReadOnly) continue;
                            try
                            {
                                if (p.Definition == null) continue;
                                string key = "T|" + p.Definition.Name;
                                if (dict.ContainsKey(key)) continue;
                                dict[key] = new ParamEditInfo
                                {
                                    Name = p.Definition.Name,
                                    GroupName = GetGroupName(p),
                                    Scope = "类型",
                                    StorageType = p.StorageType.ToString(),
                                    IsReadOnly = p.IsReadOnly,
                                    Apply = false,
                                    NewValue = ""
                                };
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            _paramList.Clear();
            var list = dict.Values.OrderBy(p => p.Scope == "类型" ? 1 : 0)
                .ThenBy(p => p.GroupName)
                .ThenBy(p => p.Name)
                .ToList();
            foreach (var p in list) _paramList.Add(p);

            _dgvParams.DataSource = null;
            _dgvParams.DataSource = _paramList;
            if (_dgvParams.Columns.Contains("Apply"))
                _dgvParams.Columns["Apply"].ReadOnly = false;
            // 填充 Param 列
            foreach (DataGridViewRow r in _dgvParams.Rows)
            {
                var p = r.DataBoundItem as ParamEditInfo;
                if (p != null) r.Cells["Param"].Value = p.ToString();
            }
        }

        private static string GetGroupName(Parameter p)
        {
            try
            {
                var def = p.Definition;
                if (def == null) return "-";
                try
                {
                    BuiltInParameterGroup pg = def.ParameterGroup;
                    string label = LabelUtils.GetLabelFor(pg);
                    if (!string.IsNullOrEmpty(label)) return label;
                    return pg.ToString();
                }
                catch { return "-"; }
            }
            catch { return "-"; }
        }

        // ============================================================
        // 事件
        // ============================================================
        private void DgvParams_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            UpdateStat();
            // 预览可在这里加
        }

        private void SetAllRows(bool sel)
        {
            foreach (var r in _allRows) r.Selected = sel;
            _dgvElements.DataSource = null;
            _viewRows = new BindingList<ElementRow>(_allRows);
            _dgvElements.DataSource = _viewRows;
            if (_dgvElements.Columns.Contains("Selected"))
                _dgvElements.Columns["Selected"].ReadOnly = false;
            UpdateStat();
        }

        private void UpdateStat()
        {
            int applyParams = _paramList.Count(p => p.Apply);
            int selElems = _allRows.Count(r => r.Selected);
            int totalChanges = applyParams * selElems;
            _lblStat.Text = string.Format(
                "共 {0} 个构件，已勾选 {1} 个；参数共 {2} 个，已勾选应用 {3} 个；合计将修改 {4} 条记录。",
                _allRows.Count, selElems, _paramList.Count, applyParams, totalChanges);
        }

        private bool ValidateBeforeApply()
        {
            int applyParams = _paramList.Count(p => p.Apply);
            int selElems = _allRows.Count(r => r.Selected);
            if (selElems == 0)
            {
                MessageBox.Show("请至少勾选一个要修改的构件。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (applyParams == 0)
            {
                MessageBox.Show("请至少勾选一个要修改的参数（左侧「应用」列）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            // 检查每个勾选的参数是否填了值（允许空字符串，但提示）
            var empty = _paramList.Where(p => p.Apply && string.IsNullOrEmpty(p.NewValue)).ToList();
            if (empty.Count > 0)
            {
                string names = string.Join("、", empty.Take(3).Select(p => p.Name));
                if (empty.Count > 3) names += " 等" + empty.Count + "个";
                var dr = MessageBox.Show(
                    string.Format("有 {0} 个已勾选参数的「新值」为空，是否以空字符串写入？\n\n涉及：{1}", empty.Count, names),
                    "空值确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return false;
            }
            return true;
        }

        // ============================================================
        // 结果
        // ============================================================
        private EditDialogResult BuildResult(bool confirmed)
        {
            var result = new EditDialogResult { Confirmed = confirmed };
            if (!confirmed) return result;

            var selectedRows = _allRows.Where(r => r.Selected).ToList();
            var selectedParams = _paramList.Where(p => p.Apply).ToList();

            foreach (var row in selectedRows)
            {
                foreach (var p in selectedParams)
                {
                    // 类型参数：写入 ElementType，且同类型去重（避免重复写入）
                    if (p.Scope == "类型")
                    {
                        result.Assignments.Add(new ParamEditAssignment
                        {
                            ElementId = row.TypeId,  // 用 TypeId 而不是 ElementId
                            ParamName = p.Name,
                            IsInstance = false,
                            NewValue = p.NewValue
                        });
                    }
                    else
                    {
                        result.Assignments.Add(new ParamEditAssignment
                        {
                            ElementId = row.ElementId,
                            ParamName = p.Name,
                            IsInstance = true,
                            NewValue = p.NewValue
                        });
                    }
                }
            }

            // 类型参数去重（同 TypeId + ParamName 只保留第一个）
            var dedup = new List<ParamEditAssignment>();
            var seen = new HashSet<string>();
            foreach (var a in result.Assignments)
            {
                string key = a.IsInstance
                    ? "I_" + a.ElementId.IntegerValue + "_" + a.ParamName
                    : "T_" + a.ElementId.IntegerValue + "_" + a.ParamName;
                if (seen.Contains(key)) continue;
                seen.Add(key);
                dedup.Add(a);
            }
            result.Assignments = dedup;
            return result;
        }
    }

    internal static class Exts
    {
        public static void Cancel_EventArgs(this CancelEventArgs e) { e.Cancel = true; }
    }
}
