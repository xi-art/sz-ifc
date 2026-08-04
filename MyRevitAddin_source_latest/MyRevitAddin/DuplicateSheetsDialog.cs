using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MyRevitAddin
{
    /// <summary>
    /// 批量复制图纸与视图对话框
    /// 参照 PowerSheets / SmartViews Duplicate Sheets with Views
    /// </summary>
    public class DuplicateSheetsDialog : Form
    {
        private readonly Document _doc;
        private readonly List<SheetViewRow> _rows;

        // 控件
        private System.Windows.Forms.TextBox TxtSheetPrefix;
        private System.Windows.Forms.TextBox TxtViewPrefix;
        private System.Windows.Forms.TextBox TxtCopyCount;
        private System.Windows.Forms.ComboBox CmbNameRule;
        private System.Windows.Forms.DataGridView DgSheets;
        private System.Windows.Forms.Label LblInfo;
        private System.Windows.Forms.Button BtnOk;
        private System.Windows.Forms.Button BtnCancel;
        private System.Windows.Forms.Button BtnSelectAll;

        // 公开属性
        public string SheetPrefix => TxtSheetPrefix.Text.Trim();
        public string ViewPrefix => TxtViewPrefix.Text.Trim();
        public int CopyCount
        {
            get => int.TryParse(TxtCopyCount.Text.Trim(), out int v) && v > 0 ? v : 1;
        }
        public bool UsePrefixForNumbering => CmbNameRule.SelectedIndex == 0;
        public Dictionary<(ElementId, ElementId), ElementId> ViewReplacements { get; private set; }

        public DuplicateSheetsDialog(Document doc, List<ElementId> selectedSheetIds)
        {
            _doc = doc;
            _rows = new List<SheetViewRow>();
            ViewReplacements = new Dictionary<(ElementId, ElementId), ElementId>();
            InitUI();
            BuildRows(selectedSheetIds);
        }

        private void InitUI()
        {
            this.Text = "批量复制图纸与视图";
            this.Size = new Size(900, 650);
            this.MinimumSize = new Size(800, 550);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.BackColor = System.Drawing.Color.White;

            var font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            var boldFont = new System.Drawing.Font("Microsoft YaHei UI", 9F,
                System.Drawing.FontStyle.Bold);

            // ===== 顶部配置区 =====
            var topPanel = new System.Windows.Forms.FlowLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Top,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                Padding = new System.Windows.Forms.Padding(12, 12, 12, 8),
                Height = 80,
                BackColor = System.Drawing.Color.FromArgb(245, 245, 245),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                AutoSize = false
            };

            // Sheet 前缀
            var sheetPrefixPanel = new System.Windows.Forms.Panel { Width = 150, Height = 62 };
            sheetPrefixPanel.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "Sheet 前缀", Font = boldFont,
                Location = new System.Drawing.Point(0, 0), AutoSize = true
            });
            TxtSheetPrefix = new System.Windows.Forms.TextBox
            {
                Width = 140, Location = new System.Drawing.Point(0, 22), Font = font
            };
            sheetPrefixPanel.Controls.Add(TxtSheetPrefix);

            // View 前缀
            var viewPrefixPanel = new System.Windows.Forms.Panel { Width = 150, Height = 62 };
            viewPrefixPanel.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "视图前缀", Font = boldFont,
                Location = new System.Drawing.Point(0, 0), AutoSize = true
            });
            TxtViewPrefix = new System.Windows.Forms.TextBox
            {
                Width = 140, Location = new System.Drawing.Point(0, 22), Font = font
            };
            viewPrefixPanel.Controls.Add(TxtViewPrefix);

            // 复制数量
            var countPanel = new System.Windows.Forms.Panel { Width = 100, Height = 62 };
            countPanel.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "复制数量", Font = boldFont,
                Location = new System.Drawing.Point(0, 0), AutoSize = true
            });
            TxtCopyCount = new System.Windows.Forms.TextBox
            {
                Width = 60, Location = new System.Drawing.Point(0, 22),
                Font = font, Text = "1",
                TextAlign = System.Windows.Forms.HorizontalAlignment.Center
            };
            countPanel.Controls.Add(TxtCopyCount);

            // 编号规则
            var rulePanel = new System.Windows.Forms.Panel { Width = 160, Height = 62 };
            rulePanel.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "编号规则", Font = boldFont,
                Location = new System.Drawing.Point(0, 0), AutoSize = true
            });
            CmbNameRule = new System.Windows.Forms.ComboBox
            {
                Width = 150, Location = new System.Drawing.Point(0, 22),
                Font = font, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
            };
            CmbNameRule.Items.AddRange(new[] { "前缀 + 原编号", "原编号 + Copy N" });
            CmbNameRule.SelectedIndex = 0;
            rulePanel.Controls.Add(CmbNameRule);

            // 提示
            var tipPanel = new System.Windows.Forms.Panel { Width = 220, Height = 62 };
            tipPanel.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "💡 提示", Font = boldFont,
                ForeColor = System.Drawing.Color.Gray,
                Location = new System.Drawing.Point(0, 0), AutoSize = true
            });
            tipPanel.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "Ctrl+点击 可多选图纸\n替换视图列：选择后该视图将被替换",
                Font = new System.Drawing.Font("Microsoft YaHei UI", 8F),
                ForeColor = System.Drawing.Color.Gray,
                Location = new System.Drawing.Point(0, 20), AutoSize = true
            });

            topPanel.Controls.AddRange(new System.Windows.Forms.Control[]
                { sheetPrefixPanel, viewPrefixPanel, countPanel, rulePanel, tipPanel });

            // ===== 信息栏 =====
            LblInfo = new System.Windows.Forms.Label
            {
                Dock = System.Windows.Forms.DockStyle.Top,
                Text = "正在加载视图列表...",
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F),
                ForeColor = System.Drawing.Color.Gray,
                Padding = new System.Windows.Forms.Padding(12, 6, 12, 4),
                Height = 28
            };

            // ===== 主表格 =====
            DgSheets = new System.Windows.Forms.DataGridView
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Font = font,
                BackgroundColor = System.Drawing.Color.White,
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                MultiSelect = true,
                SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = false,
                ColumnHeadersHeightSizeMode =
                    System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 26 }
            };
            DgSheets.AlternatingRowsDefaultCellStyle.BackColor =
                System.Drawing.Color.FromArgb(250, 250, 250);
            DgSheets.ColumnHeadersHeight = 30;

            // ===== 底部按钮 =====
            var bottomPanel = new System.Windows.Forms.Panel
            {
                Dock = System.Windows.Forms.DockStyle.Bottom,
                Height = 48,
                Padding = new System.Windows.Forms.Padding(12, 8, 12, 8),
                BackColor = System.Drawing.Color.FromArgb(245, 245, 245)
            };

            BtnSelectAll = new System.Windows.Forms.Button
            {
                Text = "全选",
                Width = 80,
                Location = new System.Drawing.Point(12, 8),
                Font = font
            };
            BtnSelectAll.Click += (s, e) =>
            {
                if (DgSheets.RowCount > 0) DgSheets.SelectAll();
            };

            BtnOk = new System.Windows.Forms.Button
            {
                Text = "确定",
                Width = 90,
                Font = font,
                BackColor = System.Drawing.Color.FromArgb(0, 120, 212),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = System.Windows.Forms.FlatStyle.Flat
            };
            BtnOk.Click += BtnOk_Click;

            BtnCancel = new System.Windows.Forms.Button
            {
                Text = "取消",
                Width = 90,
                Font = font,
                DialogResult = System.Windows.Forms.DialogResult.Cancel
            };
            BtnCancel.Click += (s, e) =>
            {
                this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                this.Close();
            };

            bottomPanel.Controls.Add(BtnSelectAll);
            bottomPanel.Controls.Add(BtnOk);
            bottomPanel.Controls.Add(BtnCancel);

            this.Controls.Add(DgSheets);
            this.Controls.Add(LblInfo);
            this.Controls.Add(topPanel);
            this.Controls.Add(bottomPanel);
        }

        private void BuildRows(List<ElementId> selectedSheetIds)
        {
            _rows.Clear();

            // 收集所有可用视图
            var allViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Internal)
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .ToList();

            int totalViews = 0;
            foreach (var sheetId in selectedSheetIds)
            {
                ViewSheet sheet = _doc.GetElement(sheetId) as ViewSheet;
                if (sheet == null) continue;

                var vpIds = sheet.GetAllViewports();
                var vps = vpIds
                    .Select(vpId => _doc.GetElement(vpId) as Viewport)
                    .Where(vp => vp != null)
                    .ToList();

                totalViews += vps.Count;

                if (vps.Count == 0)
                {
                    _rows.Add(new SheetViewRow
                    {
                        SheetId = sheetId,
                        ViewId = ElementId.InvalidElementId,
                        SheetNumber = sheet.SheetNumber,
                        SheetName = sheet.Name,
                        ViewName = "(空图纸)",
                        ViewType = "-",
                        IsIncluded = true,
                        AvailableViews = BuildViewComboItems(allViews, ElementId.InvalidElementId)
                    });
                }
                else
                {
                    foreach (var vp in vps)
                    {
                        var view = _doc.GetElement(vp.ViewId) as Autodesk.Revit.DB.View;
                        _rows.Add(new SheetViewRow
                        {
                            SheetId = sheetId,
                            ViewId = vp.ViewId,
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            ViewName = view?.Name ?? "(未知)",
                            ViewType = view?.ViewType.ToString() ?? "-",
                            IsIncluded = true,
                            AvailableViews = BuildViewComboItems(allViews, vp.ViewId)
                        });
                    }
                }
            }

            LblInfo.Text = $"已选 {selectedSheetIds.Count} 张图纸，共 {totalViews} 个视图  |  " +
                           "视图列可选择替换为其他视图（空白=原样复制）";
            BindGrid();
        }

        private List<ViewComboItem> BuildViewComboItems(
            List<Autodesk.Revit.DB.View> allViews, ElementId currentId)
        {
            var items = new List<ViewComboItem>
            {
                new ViewComboItem { ViewId = ElementId.InvalidElementId, ViewName = "— 原样复制 —" }
            };
            items.AddRange(allViews.Select(v =>
                new ViewComboItem
                {
                    ViewId = v.Id,
                    ViewName = $"[{v.ViewType}] {v.Name}"
                }));
            return items;
        }

        private void BindGrid()
        {
            DgSheets.DataSource = null;
            DgSheets.DataSource = _rows;
            DgSheets.DataMember = "";
            DgSheets.Columns.Clear();

            DgSheets.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            {
                HeaderText = "Sheet编号", DataPropertyName = "SheetNumber",
                ReadOnly = true, Width = 100, FillWeight = 80
            });
            DgSheets.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            {
                HeaderText = "Sheet名称", DataPropertyName = "SheetName",
                ReadOnly = true, Width = 160, FillWeight = 120
            });
            DgSheets.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            {
                HeaderText = "视图名称", DataPropertyName = "ViewName",
                ReadOnly = true, Width = 150, FillWeight = 110
            });
            DgSheets.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            {
                HeaderText = "视图类型", DataPropertyName = "ViewType",
                ReadOnly = true, Width = 90, FillWeight = 65,
                DefaultCellStyle = new System.Windows.Forms.DataGridViewCellStyle
                {
                    ForeColor = System.Drawing.Color.Gray
                }
            });
            DgSheets.Columns.Add(new System.Windows.Forms.DataGridViewCheckBoxColumn
            {
                HeaderText = "包含", DataPropertyName = "IsIncluded",
                Width = 50, FillWeight = 30, TrueValue = true, FalseValue = false
            });

            // 替换视图下拉列
            var replaceCol = new System.Windows.Forms.DataGridViewComboBoxColumn
            {
                HeaderText = "替换视图（留空=原样复制）",
                DataPropertyName = "ReplacementViewId",
                Width = 220, FillWeight = 160,
                DisplayMember = "ViewName", ValueMember = "ViewId",
                DataSource = null,
                FlatStyle = System.Windows.Forms.FlatStyle.Flat
            };
            DgSheets.Columns.Add(replaceCol);

            // 行样式
            DgSheets.CellFormatting += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _rows.Count)
                {
                    var row = _rows[e.RowIndex];
                    if (!row.IsIncluded)
                    {
                        DgSheets.Rows[e.RowIndex].DefaultCellStyle.BackColor =
                            System.Drawing.Color.FromArgb(240, 240, 240);
                    }
                }
            };

            // 数据源更新后刷新下拉列
            DgSheets.DataSourceChanged += (s, e) =>
            {
                foreach (System.Windows.Forms.DataGridViewRow gridRow in DgSheets.Rows)
                {
                    if (gridRow.DataBoundItem is SheetViewRow row &&
                        row.AvailableViews != null && row.AvailableViews.Count > 0)
                    {
                        var comboCell = gridRow.Cells[replaceCol.Index] as
                            System.Windows.Forms.DataGridViewComboBoxCell;
                        if (comboCell != null)
                        {
                            comboCell.DataSource = row.AvailableViews;
                            comboCell.DisplayMember = "ViewName";
                            comboCell.ValueMember = "ViewId";
                            // 选中默认项
                            if (row.ReplacementViewId != ElementId.InvalidElementId)
                                comboCell.Value = row.ReplacementViewId;
                        }
                    }
                }
            };

            DgSheets.DataSource = _rows;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            ViewReplacements.Clear();
            foreach (var row in _rows)
            {
                if (row.IsIncluded && row.ViewId != ElementId.InvalidElementId)
                {
                    if (row.ReplacementViewId != ElementId.InvalidElementId &&
                        row.ReplacementViewId != row.ViewId)
                    {
                        ViewReplacements[(row.SheetId, row.ViewId)] = row.ReplacementViewId;
                    }
                }
            }
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }
    }

    /// <summary>
    /// 表格行数据模型
    /// </summary>
    public class SheetViewRow : INotifyPropertyChanged
    {
        public ElementId SheetId { get; set; }
        public ElementId ViewId { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string ViewName { get; set; }
        public string ViewType { get; set; }

        private bool _isIncluded = true;
        public bool IsIncluded
        {
            get => _isIncluded;
            set { _isIncluded = value; OnPropertyChanged(nameof(IsIncluded)); }
        }

        private List<ViewComboItem> _availableViews = new List<ViewComboItem>();
        public List<ViewComboItem> AvailableViews
        {
            get => _availableViews;
            set { _availableViews = value; OnPropertyChanged(nameof(AvailableViews)); }
        }

        private ElementId _replacementViewId = ElementId.InvalidElementId;
        public ElementId ReplacementViewId
        {
            get => _replacementViewId;
            set { _replacementViewId = value; OnPropertyChanged(nameof(ReplacementViewId)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ViewComboItem
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public override string ToString() => ViewName;
    }
}
