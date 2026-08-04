using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace MyRevitAddin.RoomCopyNameToId
{
    // ============================================================
    // 参数定义
    // ============================================================
    public class IdentityParamInfo
    {
        public string Name { get; set; }            // 内部名（Definition.Name）
        public string DisplayName { get; set; }     // 本地化显示名
        public string GroupName { get; set; }       // 参数组名
        public string Scope { get; set; }           // 实例 / 类型
        public string StorageType { get; set; }     // String / Integer / ...
        public bool IsReadOnly { get; set; }
        public bool Selected { get; set; } = true;
        public override string ToString()
        {
            return string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;
        }
    }

    // ============================================================
    // 房间行
    // ============================================================
    public class RoomRow
    {
        public ElementId RoomId { get; set; }
        public string LevelName { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public double Area { get; set; }
        public bool Selected { get; set; } = true;
        public string Preview { get; set; } = "";
        public string Status { get; set; } = "";   // 空名 / 不变 / 跳过
    }

    // ============================================================
    // 对话框
    // ============================================================
    public partial class RoomCopyNameToIdDialog : Form
    {
        private readonly Document _doc;
        private List<RoomRow> _allRooms = new List<RoomRow>();
        private BindingList<RoomRow> _viewRooms = new BindingList<RoomRow>();
        private List<IdentityParamInfo> _allParams = new List<IdentityParamInfo>();
        private bool _suppressEvent;

        // UI
        private ComboBox _cmbLevel;
        private CheckBox _chkSkipEmpty;
        private CheckBox _chkSkipUnchanged;

        private CheckedListBox _clbParams;
        private Button _btnSelectAllParams;
        private Button _btnSelectNoneParams;

        private DataGridView _dgv;
        private Button _btnSelectAllRooms;
        private Button _btnSelectNoneRooms;

        private Label _lblStat;
        private Button _btnApply;
        private Button _btnCancel;

        public RoomCopyNameToIdDialog(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            Load += (s, e) => OnLoadData();
        }

        /// <summary>
        /// 显式 ShowDialog，外部 using 包裹
        /// </summary>
        public BatchDialogResult ShowDialogEx(IWin32Window owner)
        {
            var dr = owner != null ? ShowDialog(owner) : ShowDialog();
            return new BatchDialogResult
            {
                Confirmed = dr == DialogResult.OK,
                Assignments = _pendingAssignments
            };
        }

        private List<RoomParamAssignment> _pendingAssignments = new List<RoomParamAssignment>();

        // ============================================================
        // UI 初始化
        // ============================================================
        private void InitializeComponent()
        {
            Text = "房间名称 → 属性参数 批量赋值";
            Size = new Size(1080, 680);
            MinimumSize = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            // 顶部
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
                Text = "跳过空名",
                Checked = true,
                AutoSize = true,
                Location = new Point(340, 14)
            };
            _chkSkipEmpty.CheckedChanged += (s, e) => ApplyLevelFilter();
            _chkSkipUnchanged = new CheckBox
            {
                Text = "跳过未变化（值与原值相同）",
                Checked = false,
                AutoSize = true,
                Location = new Point(420, 14)
            };
            _chkSkipUnchanged.CheckedChanged += (s, e) => UpdateStats();
            pnlTop.Controls.AddRange(new Control[] { lblLevel, _cmbLevel, _chkSkipEmpty, _chkSkipUnchanged });

            // 中部分隔
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 380,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.FromArgb(220, 225, 230)
            };

            // ===== 左侧：参数选择 =====
            var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            int y = 8;
            var gbParams = new GroupBox
            {
                Text = "目标参数（自动识别所有可写参数）",
                Left = 8,
                Top = y,
                Width = 360,
                Height = 460
            };
            y += 468;

            // 顶部说明
            var lblTip = new Label
            {
                Text = "勾选要写入的参数；值 = 对应房间的「名称」",
                Left = 12,
                Top = 18,
                Width = 340,
                Height = 18,
                ForeColor = Color.DarkSlateBlue
            };
            gbParams.Controls.Add(lblTip);

            _clbParams = new CheckedListBox
            {
                Left = 12,
                Top = 40,
                Width = 336,
                Height = 380,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _clbParams.ItemCheck += (s, e) =>
            {
                if (_suppressEvent) return;
                var it = _clbParams.Items[e.Index] as IdentityParamInfo;
                if (it != null)
                {
                    it.Selected = e.NewValue == CheckState.Checked;
                    UpdateStats();
                    UpdatePreview();
                }
            };
            gbParams.Controls.Add(_clbParams);

            // 全选/全不选 按钮
            _btnSelectAllParams = new Button { Text = "全选参数", Location = new Point(12, 425), Size = new Size(80, 26) };
            _btnSelectAllParams.Click += (s, e) => SetAllParams(true);
            _btnSelectNoneParams = new Button { Text = "全不选", Location = new Point(98, 425), Size = new Size(80, 26) };
            _btnSelectNoneParams.Click += (s, e) => SetAllParams(false);
            gbParams.Controls.AddRange(new Control[] { _btnSelectAllParams, _btnSelectNoneParams });

            // 检测结果
            var lblInfo = new Label
            {
                Text = "",
                Left = 188,
                Top = 432,
                Width = 168,
                Height = 18,
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleRight
            };
            lblInfo.Name = "lblInfo";
            gbParams.Controls.Add(lblInfo);

            pnlLeft.Controls.Add(gbParams);

            // 提示：手动填写参数
            var gbCustom = new GroupBox
            {
                Text = "自定义参数名（可选）",
                Left = 8,
                Top = y,
                Width = 360,
                Height = 80
            };
            var lblCustom = new Label
            {
                Text = "参数名（手动添加共享参数）：",
                Left = 12,
                Top = 18,
                Width = 340,
                AutoSize = true
            };
            var txtCustom = new TextBox { Left = 12, Top = 42, Width = 200, Name = "txtCustom" };
            var btnAddCustom = new Button
            {
                Text = "添加",
                Left = 220,
                Top = 40,
                Size = new Size(60, 26)
            };
            btnAddCustom.Click += (s, e) =>
            {
                var t = gbCustom.Controls["txtCustom"] as TextBox;
                if (t == null || string.IsNullOrWhiteSpace(t.Text)) return;
                var name = t.Text.Trim();
                if (_allParams.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("参数已存在。", "提示");
                    return;
                }
                var info = new IdentityParamInfo
                {
                    Name = name,
                    DisplayName = name + " （自定义）",
                    GroupName = "自定义",
                    Scope = "实例",
                    StorageType = "String",
                    IsReadOnly = false,
                    Selected = true
                };
                _allParams.Add(info);
                _suppressEvent = true;
                int idx = _clbParams.Items.Add(info);
                _clbParams.SetItemChecked(idx, true);
                _suppressEvent = false;
                t.Text = "";
                UpdateStats();
                UpdatePreview();
            };
            gbCustom.Controls.AddRange(new Control[] { lblCustom, txtCustom, btnAddCustom });
            pnlLeft.Controls.Add(gbCustom);

            split.Panel1.Controls.Add(pnlLeft);

            // ===== 右侧：房间预览 =====
            var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var pnlToolbar = new Panel { Dock = DockStyle.Top, Height = 36 };
            _btnSelectAllRooms = new Button { Text = "全选", Location = new Point(0, 4), Size = new Size(60, 28) };
            _btnSelectAllRooms.Click += (s, e) => SetAllRooms(true);
            _btnSelectNoneRooms = new Button { Text = "全不选", Location = new Point(66, 4), Size = new Size(70, 28) };
            _btnSelectNoneRooms.Click += (s, e) => SetAllRooms(false);
            var btnRefresh = new Button { Text = "刷新预览", Location = new Point(142, 4), Size = new Size(90, 28) };
            btnRefresh.Click += (s, e) => UpdatePreview();
            pnlToolbar.Controls.AddRange(new Control[] { _btnSelectAllRooms, _btnSelectNoneRooms, btnRefresh });

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

            // 底部
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

            Controls.Add(split);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
            AcceptButton = _btnApply;
            CancelButton = _btnCancel;
        }

        // ============================================================
        // 数据加载
        // ============================================================
        private void OnLoadData()
        {
            try
            {
                LoadParams();
                LoadLevels();
                LoadRooms();
                ApplyLevelFilter();
                BindGrid();
                UpdatePreview();
                UpdateStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadParams()
        {
            _allParams.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Take(50)
                .ToList();

            foreach (var r in rooms)
            {
                CollectParams(r.Parameters, r, "实例", seen);
                ElementType et = _doc.GetElement(r.GetTypeId()) as ElementType;
                if (et != null) CollectParams(et.Parameters, et, "类型", seen);
            }

            _suppressEvent = true;
            _clbParams.Items.Clear();
            foreach (var p in _allParams)
            {
                int idx = _clbParams.Items.Add(p);
                if (p.Selected) _clbParams.SetItemChecked(idx, true);
            }
            _suppressEvent = false;

            // 更新左侧检测结果
            if (Controls.Find("split", true).FirstOrDefault() is SplitContainer sp)
            {
                // placeholder
            }
            // 直接找 lblInfo
            foreach (Control c in GetAll(this, "gbParams"))
            {
                if (c.Name == "lblInfo")
                {
                    c.Text = "共 " + _allParams.Count + " 个参数（实例 " +
                             _allParams.Count(x => x.Scope == "实例") + " / 类型 " +
                             _allParams.Count(x => x.Scope == "类型") + "）";
                    break;
                }
            }
        }

        private static IEnumerable<Control> GetAll(Control root, string nameHint)
        {
            // 遍历整个树找 nameHint 命名的容器
            var stack = new Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if (c.Name == nameHint) yield return c;
                foreach (Control ch in c.Controls) stack.Push(ch);
            }
        }

        private void CollectParams(ParameterSet ps, Element owner, string scope, HashSet<string> seen)
        {
            if (ps == null) return;
            foreach (Parameter p in ps)
            {
                if (p?.Definition == null) continue;
                // 不再过滤参数组，收集所有可写参数
                string key = p.Definition.Name;
                if (seen.Contains(key)) continue;
                seen.Add(key);

                string display = key;
                try
                {
                    if (p.Id?.IntegerValue >= 0 && Enum.IsDefined(typeof(BuiltInParameter), p.Id.IntegerValue))
                    {
                        var bip = (BuiltInParameter)p.Id.IntegerValue;
                        string localized = LabelUtils.GetLabelFor(bip);
                        if (!string.IsNullOrEmpty(localized)) display = localized;
                    }
                }
                catch { }

                // 获取参数组的本地化名称
                string groupName = "其他";
                try
                {
                    if (p.Definition.ParameterGroup != BuiltInParameterGroup.INVALID)
                        groupName = LabelUtils.GetLabelFor(p.Definition.ParameterGroup);
                }
                catch { }

                _allParams.Add(new IdentityParamInfo
                {
                    Name = key,
                    DisplayName = display,
                    GroupName = groupName,
                    Scope = scope,
                    StorageType = p.StorageType.ToString(),
                    IsReadOnly = p.IsReadOnly,
                    Selected = !p.IsReadOnly
                });
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
            _allRooms.Clear();
            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();
            foreach (var r in rooms)
            {
                string name = "";
                try { name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""; } catch { }
                string num = "";
                try { num = r.Number ?? ""; } catch { }
                string lvlName = "";
                try { lvlName = (r.Level as Level)?.Name ?? ""; } catch { }
                double area = 0;
                try { area = r.Area; } catch { }

                _allRooms.Add(new RoomRow
                {
                    RoomId = r.Id,
                    LevelName = lvlName,
                    Number = num,
                    Name = name,
                    Area = area
                });
            }
            _allRooms = _allRooms.OrderBy(x => x.LevelName).ThenBy(x => x.Number).ToList();
        }

        private void ApplyLevelFilter()
        {
            if (_cmbLevel.SelectedItem is LevelItem li)
            {
                if (li.Id == ElementId.InvalidElementId)
                    _viewRooms = new BindingList<RoomRow>(_allRooms.ToList());
                else
                {
                    var lvlName = li.Name;
                    _viewRooms = new BindingList<RoomRow>(
                        _allRooms.Where(x => string.Equals(x.LevelName, lvlName, StringComparison.OrdinalIgnoreCase)).ToList());
                }
            }
            else
            {
                _viewRooms = new BindingList<RoomRow>(_allRooms.ToList());
            }
            if (_chkSkipEmpty.Checked)
            {
                var filtered = _viewRooms.Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Area > 0).ToList();
                _viewRooms = new BindingList<RoomRow>(filtered);
            }
            BindGrid();
            UpdatePreview();
            UpdateStats();
        }

        private void SetAllParams(bool sel)
        {
            _suppressEvent = true;
            for (int i = 0; i < _clbParams.Items.Count; i++)
            {
                _clbParams.SetItemChecked(i, sel);
                if (_clbParams.Items[i] is IdentityParamInfo x) x.Selected = sel;
            }
            _suppressEvent = false;
            UpdateStats();
            UpdatePreview();
        }

        private void SetAllRooms(bool sel)
        {
            foreach (var r in _viewRooms) r.Selected = sel;
            _dgv.Invalidate();
            UpdateStats();
        }

        // ============================================================
        // 表格绑定
        // ============================================================
        private void BindGrid()
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
                _dgv.Columns.Add(MakeTextCol("LevelName", "楼层", 110, true));
                _dgv.Columns.Add(MakeTextCol("Number", "编号", 80, true));
                _dgv.Columns.Add(MakeTextCol("Name", "房间名称（源）", 220, true));
                _dgv.Columns.Add(MakeTextCol("Preview", "将赋值到（预览）", 280, true));
                _dgv.Columns.Add(MakeTextCol("Status", "状态", 100, true));

                _dgv.DataSource = _viewRooms;
                _dgv.CellValueChanged += (s, e) =>
                {
                    if (_suppressEvent || e.RowIndex < 0) return;
                    if (_dgv.Columns[e.ColumnIndex].DataPropertyName == "Selected")
                        UpdateStats();
                };
                _dgv.CurrentCellDirtyStateChanged += (s, e) =>
                {
                    if (_dgv.IsCurrentCellDirty) _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
            }
            finally { _suppressEvent = false; }
        }

        private DataGridViewTextBoxColumn MakeTextCol(string prop, string header, int width, bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = prop,
                ReadOnly = readOnly,
                Width = width
            };
        }

        // ============================================================
        // 预览 & 统计
        // ============================================================
        private void UpdatePreview()
        {
            if (_viewRooms == null) return;

            var selectedParams = _allParams.Where(p => p.Selected && !p.IsReadOnly).ToList();
            string preview = selectedParams.Count == 0
                ? "（未选参数）"
                : string.Join("、", selectedParams.Select(p => p.DisplayName)) + " ← 房间名称";

            foreach (var r in _viewRooms)
            {
                r.Preview = preview;
                if (string.IsNullOrWhiteSpace(r.Name)) r.Status = "空名";
                else if (selectedParams.Count == 0) r.Status = "无目标参数";
                else r.Status = "将赋值";
            }
            _dgv?.Invalidate();
        }

        private void UpdateStats()
        {
            if (_viewRooms == null) return;
            int totalRooms = _viewRooms.Count;
            int selRooms = _viewRooms.Count(x => x.Selected);
            int selParams = _allParams.Count(p => p.Selected && !p.IsReadOnly);
            int willSet = selRooms * selParams;
            _lblStat.Text = $"共 {totalRooms} 个房间，已选 {selRooms}；目标参数 {selParams}；将写入 {willSet} 个参数值。";
        }

        // ============================================================
        // 应用
        // ============================================================
        private void BtnApply_Click(object sender, EventArgs e)
        {
            var selectedParams = _allParams.Where(p => p.Selected && !p.IsReadOnly).ToList();
            if (selectedParams.Count == 0)
            {
                MessageBox.Show("请至少勾选一个目标参数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            var rooms = _viewRooms.Where(x => x.Selected && !string.IsNullOrWhiteSpace(x.Name)).ToList();
            if (rooms.Count == 0)
            {
                MessageBox.Show("请至少勾选一个有效房间（名称非空）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            // 组装 assignments
            var list = new List<RoomParamAssignment>();
            foreach (var r in rooms)
            {
                foreach (var p in selectedParams)
                {
                    list.Add(new RoomParamAssignment
                    {
                        RoomId = r.RoomId,
                        ParamKey = p.Name,
                        IsInstance = p.Scope == "实例",
                        NewValue = r.Name
                    });
                }
            }

            // 二次确认
            string msg = $"即将对 {rooms.Count} 个房间 × {selectedParams.Count} 个参数 = {list.Count} 个赋值。\n\n是否继续？";
            if (MessageBox.Show(msg, "确认应用", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                DialogResult = DialogResult.None;
                return;
            }

            _pendingAssignments = list;
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
