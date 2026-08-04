using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;

namespace MyRevitAddin.SelectBySystem
{
    using WinComboBox = System.Windows.Forms.ComboBox;
    using WinTextBox = System.Windows.Forms.TextBox;

    // ============================================================
    // 数据结构
    // ============================================================

    internal class SystemTypeItem
    {
        public ElementId SystemTypeId { get; set; }
        public string Name { get; set; }
        public int ElementCount { get; set; }
        public override string ToString() => $"{Name} ({ElementCount} 个)";
    }

    internal class ElementRow
    {
        public int Index { get; set; }
        public string Category { get; set; }
        public string SystemType { get; set; }
        public string Name { get; set; }
        public string Mark { get; set; }
        public ElementId ElementId { get; set; }
    }

    internal class ParamItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string GroupName { get; set; }
        public StorageType StorageType { get; set; }
        public bool IsReadOnly { get; set; }
        public override string ToString()
        {
            string st = StorageType.ToString();
            return $"[{st}] {DisplayName}  ({GroupName})";
        }
    }

    // ============================================================
    // 主对话框
    // ============================================================

    internal class SelectBySystemDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;

        private WinComboBox _cmbCategory;
        private CheckedListBox _clbSystems;
        private DataGridView _dgv;
        private WinComboBox _cmbParam;
        private WinTextBox _txtParamValue;
        private Label _lblParamInfo;
        private Label _lblElementCount;
        private Button _btnRefresh;
        private Button _btnSelectAll;
        private Button _btnSelectNone;
        private Button _btnSelectInRevit;
        private Button _btnApplyParam;
        private Button _btnClose;

        private List<SystemTypeItem> _systemTypes = new List<SystemTypeItem>();
        private List<ElementRow> _allRows = new List<ElementRow>();
        private List<ParamItem> _paramList = new List<ParamItem>();
        private List<Element> _selectedElements = new List<Element>();

        // 类别定义
        private class MepCategory
        {
            public string Name { get; set; }
            public BuiltInCategory[] Categories { get; set; }
            public BuiltInParameter SystemTypeParam { get; set; }
        }

        private List<MepCategory> _mepCategories;

        public SelectBySystemDialog(Document doc, UIDocument uiDoc)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            InitializeComponent();
            InitCategories();
            LoadCategoryDropdown(); // 设置 SelectedIndex=0 会触发 OnCategoryChanged
        }

        private void InitCategories()
        {
            _mepCategories = new List<MepCategory>
            {
                new MepCategory
                {
                    Name = "管道系统",
                    Categories = new[]
                    {
                        BuiltInCategory.OST_PipeCurves,
                        BuiltInCategory.OST_PipeFitting,
                        BuiltInCategory.OST_PipeAccessory
                    },
                    SystemTypeParam = BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM
                },
                new MepCategory
                {
                    Name = "风管系统",
                    Categories = new[]
                    {
                        BuiltInCategory.OST_DuctCurves,
                        BuiltInCategory.OST_DuctFitting,
                        BuiltInCategory.OST_DuctAccessory
                    },
                    SystemTypeParam = BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM
                }
            };
        }

        private void InitializeComponent()
        {
            this.Text = "按系统类型选中构件";
            this.Width = 1000;
            this.Height = 680;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 9F);
            this.MinimumSize = new Size(800, 500);

            // === 左侧面板 ===
            var leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 300,
                Padding = new Padding(10)
            };

            var lblCat = new Label { Text = "机电类别：", Dock = DockStyle.Top, Height = 25 };
            _cmbCategory = new WinComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbCategory.SelectedIndexChanged += (s, e) => OnCategoryChanged();

            var lblSys = new Label { Text = "系统类型（勾选要选中的系统）：", Dock = DockStyle.Top, Height = 25, Padding = new Padding(0, 10, 0, 0) };

            _clbSystems = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                Font = new Font("微软雅黑", 9F)
            };
            _clbSystems.ItemCheck += (s, e) =>
            {
                // 延迟刷新，等勾选状态更新完
                BeginInvoke((Action)(() => LoadElements()));
            };

            var leftBtnPanel = new Panel { Dock = DockStyle.Bottom, Height = 80 };

            _btnSelectAll = new Button { Text = "全选", Left = 10, Top = 5, Width = 80, Height = 28 };
            _btnSelectAll.Click += (s, e) =>
            {
                for (int i = 0; i < _clbSystems.Items.Count; i++)
                    _clbSystems.SetItemChecked(i, true);
            };

            _btnSelectNone = new Button { Text = "全不选", Left = 100, Top = 5, Width = 80, Height = 28 };
            _btnSelectNone.Click += (s, e) =>
            {
                for (int i = 0; i < _clbSystems.Items.Count; i++)
                    _clbSystems.SetItemChecked(i, false);
            };

            _btnRefresh = new Button { Text = "刷新", Left = 190, Top = 5, Width = 80, Height = 28 };
            _btnRefresh.Click += (s, e) => OnCategoryChanged();

            var leftBtnPanel2 = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            _btnSelectInRevit = new Button
            {
                Text = "在Revit中选中",
                Dock = DockStyle.Fill,
                Height = 32,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSelectInRevit.Click += BtnSelectInRevit_Click;
            leftBtnPanel2.Controls.Add(_btnSelectInRevit);

            leftBtnPanel.Controls.Add(_btnSelectAll);
            leftBtnPanel.Controls.Add(_btnSelectNone);
            leftBtnPanel.Controls.Add(_btnRefresh);

            leftPanel.Controls.Add(_clbSystems);
            leftPanel.Controls.Add(lblSys);
            leftPanel.Controls.Add(_cmbCategory);
            leftPanel.Controls.Add(lblCat);
            leftPanel.Controls.Add(leftBtnPanel2);
            leftPanel.Controls.Add(leftBtnPanel);

            // === 右侧面板 ===
            var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 10) };

            var lblElements = new Label { Text = "构件列表：", Dock = DockStyle.Top, Height = 25 };
            _lblElementCount = new Label { Text = "共 0 个构件", Dock = DockStyle.Top, Height = 20, ForeColor = Color.Blue };

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("微软雅黑", 9F) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font("微软雅黑", 9F, FontStyle.Bold) }
            };

            // 参数设置区域
            var paramPanel = new Panel { Dock = DockStyle.Bottom, Height = 100, Padding = new Padding(0, 5, 0, 0) };

            var lblParam = new Label { Text = "参数：", Left = 0, Top = 5, Width = 45, AutoSize = true };
            _cmbParam = new WinComboBox
            {
                Left = 50, Top = 2, Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbParam.SelectedIndexChanged += (s, e) => UpdateParamInfo();

            _lblParamInfo = new Label { Left = 310, Top = 5, Width = 200, ForeColor = Color.Gray, AutoSize = true };

            var lblVal = new Label { Text = "值：", Left = 0, Top = 40, Width = 45, AutoSize = true };
            _txtParamValue = new WinTextBox { Left = 50, Top = 37, Width = 250 };

            _btnApplyParam = new Button
            {
                Text = "应用参数",
                Left = 310, Top = 35, Width = 100, Height = 32,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnApplyParam.Click += BtnApplyParam_Click;

            _btnClose = new Button { Text = "关闭", Left = 430, Top = 35, Width = 80, Height = 32 };
            _btnClose.Click += (s, e) => this.Close();

            paramPanel.Controls.Add(lblParam);
            paramPanel.Controls.Add(_cmbParam);
            paramPanel.Controls.Add(_lblParamInfo);
            paramPanel.Controls.Add(lblVal);
            paramPanel.Controls.Add(_txtParamValue);
            paramPanel.Controls.Add(_btnApplyParam);
            paramPanel.Controls.Add(_btnClose);

            rightPanel.Controls.Add(_dgv);
            rightPanel.Controls.Add(_lblElementCount);
            rightPanel.Controls.Add(lblElements);
            rightPanel.Controls.Add(paramPanel);

            this.Controls.Add(rightPanel);
            this.Controls.Add(leftPanel);
        }

        // ============================================================
        // 数据加载
        // ============================================================

        private MepCategory GetCurrentCategory()
        {
            int idx = _cmbCategory.SelectedIndex;
            if (idx < 0 || idx >= _mepCategories.Count) return null;
            return _mepCategories[idx];
        }

        private void LoadCategoryDropdown()
        {
            _cmbCategory.Items.Clear();
            foreach (var cat in _mepCategories)
                _cmbCategory.Items.Add(cat.Name);

            if (_cmbCategory.Items.Count > 0)
                _cmbCategory.SelectedIndex = 0;
        }

        private void OnCategoryChanged()
        {
            var mepCat = GetCurrentCategory();
            if (mepCat == null) return;

            _clbSystems.Items.Clear();
            _systemTypes.Clear();

            // 收集所有元素
            var allElements = new List<Element>();
            foreach (var bic in mepCat.Categories)
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();
                allElements.AddRange(elements);
            }

            // 获取系统类型名称映射（从元素上读出来的 SystemTypeId 直接 GetElement 取名称，避免枚举类型）
            var systemTypeNameMap = new Dictionary<ElementId, string>();
            // 先扫描所有元素，收集 SystemTypeId → 名称解析
            foreach (var elem in allElements)
            {
                ElementId sysTypeId = GetSystemTypeId(elem, mepCat.SystemTypeParam);
                if (sysTypeId == null || sysTypeId == ElementId.InvalidElementId) continue;
                if (systemTypeNameMap.ContainsKey(sysTypeId)) continue;
                try
                {
                    Element sysType = _doc.GetElement(sysTypeId);
                    systemTypeNameMap[sysTypeId] = sysType != null ? (sysType.Name ?? "") : ("#" + sysTypeId.IntegerValue);
                }
                catch
                {
                    systemTypeNameMap[sysTypeId] = "#" + sysTypeId.IntegerValue;
                }
            }

            // 按系统类型分组
            var grouped = new Dictionary<ElementId, int>();
            foreach (var elem in allElements)
            {
                ElementId sysTypeId = GetSystemTypeId(elem, mepCat.SystemTypeParam);
                if (sysTypeId == null || sysTypeId == ElementId.InvalidElementId) continue;

                if (!grouped.ContainsKey(sysTypeId))
                    grouped[sysTypeId] = 0;
                grouped[sysTypeId]++;
            }

            // 构建系统类型列表
            foreach (var kvp in grouped.OrderBy(k =>
            {
                string name;
                systemTypeNameMap.TryGetValue(k.Key, out name);
                return name ?? "ZZZ";
            }))
            {
                string name;
                systemTypeNameMap.TryGetValue(kvp.Key, out name);
                name = name ?? "未知系统";

                var item = new SystemTypeItem
                {
                    SystemTypeId = kvp.Key,
                    Name = name,
                    ElementCount = kvp.Value
                };
                _systemTypes.Add(item);
                _clbSystems.Items.Add(item, false);
            }

            LoadElements();
        }

        private ElementId GetSystemTypeId(Element elem, BuiltInParameter sysTypeParam)
        {
            try
            {
                var param = elem.get_Parameter(sysTypeParam);
                if (param != null && param.StorageType == StorageType.ElementId)
                    return param.AsElementId();

                // 尝试按名称查找
                param = elem.LookupParameter("系统类型");
                if (param != null && param.StorageType == StorageType.ElementId)
                    return param.AsElementId();
            }
            catch { }
            return null;
        }

        private void LoadElements()
        {
            _allRows.Clear();
            _selectedElements.Clear();

            var mepCat = GetCurrentCategory();
            if (mepCat == null)
            {
                UpdateGrid();
                return;
            }

            // 获取勾选的系统类型
            var selectedSystemIds = new HashSet<ElementId>();
            for (int i = 0; i < _clbSystems.Items.Count; i++)
            {
                if (_clbSystems.GetItemChecked(i))
                {
                    var item = _clbSystems.Items[i] as SystemTypeItem;
                    if (item != null) selectedSystemIds.Add(item.SystemTypeId);
                }
            }

            if (selectedSystemIds.Count == 0)
            {
                UpdateGrid();
                return;
            }

            // 获取系统类型名称映射（复用已有 allElements 扫描，避免额外过滤）
            var systemTypeNameMap2 = new Dictionary<ElementId, string>();
            // 先从 selectedElements 反查一次所有可能的 sysTypeIds
            var needIds = new HashSet<ElementId>();
            foreach (var bic in mepCat.Categories)
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();
                foreach (var elem in elements)
                {
                    ElementId sysTypeId = GetSystemTypeId(elem, mepCat.SystemTypeParam);
                    if (sysTypeId != null && sysTypeId != ElementId.InvalidElementId)
                        needIds.Add(sysTypeId);
                }
            }
            foreach (var sid in needIds)
            {
                try
                {
                    Element sysType = _doc.GetElement(sid);
                    systemTypeNameMap2[sid] = sysType != null ? (sysType.Name ?? "") : ("#" + sid.IntegerValue);
                }
                catch
                {
                    systemTypeNameMap2[sid] = "#" + sid.IntegerValue;
                }
            }

            // 收集选中系统的元素
            foreach (var bic in mepCat.Categories)
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var elem in elements)
                {
                    ElementId sysTypeId = GetSystemTypeId(elem, mepCat.SystemTypeParam);
                    if (sysTypeId == null || !selectedSystemIds.Contains(sysTypeId)) continue;

                    _selectedElements.Add(elem);

                    string sysName;
                    systemTypeNameMap2.TryGetValue(sysTypeId, out sysName);
                    sysName = sysName ?? "未知";

                    string mark = "";
                    try
                    {
                        var markParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        if (markParam != null) mark = markParam.AsString() ?? "";
                    }
                    catch { }

                    _allRows.Add(new ElementRow
                    {
                        Index = _allRows.Count + 1,
                        Category = elem.Category?.Name ?? "",
                        SystemType = sysName,
                        Name = elem.Name ?? "",
                        Mark = mark,
                        ElementId = elem.Id
                    });
                }
            }

            UpdateGrid();
            LoadParameters();
        }

        private void UpdateGrid()
        {
            var bindingList = new BindingList<ElementRow>(_allRows);
            _dgv.DataSource = bindingList;
            _lblElementCount.Text = $"共 {_allRows.Count} 个构件";
        }

        // ============================================================
        // 参数加载与设置
        // ============================================================

        private void LoadParameters()
        {
            _cmbParam.Items.Clear();
            _paramList.Clear();

            if (_selectedElements.Count == 0) return;

            // 取第一个元素的可写参数作为参考
            var firstElem = _selectedElements[0];
            var seenNames = new HashSet<string>();

            foreach (Parameter param in firstElem.Parameters)
            {
                if (param == null) continue;
                if (param.IsReadOnly) continue;
                if (param.StorageType == StorageType.None) continue;

                string name = param.Definition?.Name ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (seenNames.Contains(name)) continue;
                seenNames.Add(name);

                _paramList.Add(new ParamItem
                {
                    Name = name,
                    DisplayName = param.Definition?.Name ?? name,
                    GroupName = param.Definition?.ParameterGroup.ToString() ?? "其他",
                    StorageType = param.StorageType,
                    IsReadOnly = param.IsReadOnly
                });
            }

            _paramList = _paramList.OrderBy(p => p.GroupName).ThenBy(p => p.DisplayName).ToList();

            foreach (var p in _paramList)
                _cmbParam.Items.Add(p);

            if (_cmbParam.Items.Count > 0)
                _cmbParam.SelectedIndex = 0;
        }

        private void UpdateParamInfo()
        {
            if (_cmbParam.SelectedItem is ParamItem pi)
            {
                _lblParamInfo.Text = $"类型: {pi.StorageType}";
            }
            else
            {
                _lblParamInfo.Text = "";
            }
        }

        // ============================================================
        // 按钮事件
        // ============================================================

        private void BtnSelectInRevit_Click(object sender, EventArgs e)
        {
            if (_selectedElements.Count == 0)
            {
                MessageBox.Show("没有可选中的构件，请先勾选系统类型。", "提示");
                return;
            }

            try
            {
                var ids = _selectedElements.Select(e => e.Id).ToList();
                _uiDoc.Selection.SetElementIds(ids);
                MessageBox.Show($"已在 Revit 中选中 {ids.Count} 个构件。", "完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show("选中失败：" + ex.Message, "错误");
            }
        }

        private void BtnApplyParam_Click(object sender, EventArgs e)
        {
            if (_selectedElements.Count == 0)
            {
                MessageBox.Show("没有可操作的构件。", "提示");
                return;
            }

            var paramItem = _cmbParam.SelectedItem as ParamItem;
            if (paramItem == null)
            {
                MessageBox.Show("请选择要设置的参数。", "提示");
                return;
            }

            string value = _txtParamValue.Text.Trim();
            if (string.IsNullOrEmpty(value))
            {
                MessageBox.Show("请输入参数值。", "提示");
                return;
            }

            // 确认
            string confirm = $"将对 {_selectedElements.Count} 个构件设置参数：\n\n";
            confirm += $"  参数：{paramItem.DisplayName}\n";
            confirm += $"  值：{value}\n\n继续？";

            if (MessageBox.Show(confirm, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int ok = 0, fail = 0;

            using (var tx = new Transaction(_doc, "批量设置参数"))
            {
                tx.Start();
                try
                {
                    foreach (var elem in _selectedElements)
                    {
                        try
                        {
                            if (elem == null || !elem.IsValidObject) continue;

                            var param = elem.LookupParameter(paramItem.Name);
                            if (param == null || param.IsReadOnly)
                            {
                                fail++;
                                continue;
                            }

                            if (SetParamValue(param, value))
                                ok++;
                            else
                                fail++;
                        }
                        catch
                        {
                            fail++;
                        }
                    }

                    var res = tx.Commit();
                    if (res != TransactionStatus.Committed)
                    {
                        MessageBox.Show("事务未提交成功（" + res + "）。", "警告");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    MessageBox.Show("设置失败：" + ex.Message, "错误");
                    return;
                }
            }

            MessageBox.Show($"设置完成\n\n  成功：{ok} 个\n  失败：{fail} 个",
                "完成", MessageBoxButtons.OK,
                fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private bool SetParamValue(Parameter param, string value)
        {
            try
            {
                // 优先使用 SetValueString（Revit 自动处理单位转换）
                if (param.SetValueString(value))
                    return true;
            }
            catch { }

            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.Set(value);

                    case StorageType.Double:
                        if (double.TryParse(value, out double dval))
                            return param.Set(dval);
                        return false;

                    case StorageType.Integer:
                        if (int.TryParse(value, out int ival))
                            return param.Set(ival);
                        // 是/否 类型
                        if (value == "是" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                            return param.Set(1);
                        if (value == "否" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                            return param.Set(0);
                        return false;

                    case StorageType.ElementId:
                        // 尝试按名称查找元素
                        var matched = new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType()
                            .FirstOrDefault(e => e.Name == value);
                        if (matched != null)
                            return param.Set(matched.Id);
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

    }
}
