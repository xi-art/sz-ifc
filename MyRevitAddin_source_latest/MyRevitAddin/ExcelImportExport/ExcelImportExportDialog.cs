using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.ExcelImportExport
{
    using WinComboBox = System.Windows.Forms.ComboBox;
    using WinTextBox = System.Windows.Forms.TextBox;

    internal class CatItem
    {
        public BuiltInCategory Bic { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    internal class ParamCheckItem
    {
        public string Name { get; set; }
        public StorageType StorageType { get; set; }
        public override string ToString() => Name;
    }

    internal class ImportRow
    {
        public int TypeId { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        public bool Found { get; set; }
        public string Status { get; set; } = "";
    }

    internal class ExcelImportExportDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly List<ElementId> _selectedIds;

        private TabControl _tabControl;
        // 导出页控件
        private WinComboBox _cmbCategory;
        private RadioButton _rbSelected;
        private RadioButton _rbAll;
        private CheckedListBox _clbParams;
        private Button _btnExport;
        private Button _btnSelectAllParams;
        private Button _btnDeselectAllParams;
        private Label _lblExportInfo;
        // 导入页控件
        private WinTextBox _txtFilePath;
        private Button _btnBrowse;
        private DataGridView _dgvImport;
        private Button _btnImport;
        private Label _lblImportInfo;

        private List<CatItem> _categories;
        private List<ParamCheckItem> _exportParams = new List<ParamCheckItem>();
        private List<string> _importHeaders = new List<string>();
        private List<ImportRow> _importRows = new List<ImportRow>();

        public ExcelImportExportDialog(Document doc, UIDocument uiDoc, List<ElementId> selectedIds)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _selectedIds = selectedIds ?? new List<ElementId>();
            InitializeComponent();
            LoadCategories();
        }

        private void InitializeComponent()
        {
            this.Text = "表格导入回导工具";
            this.Width = 900;
            this.Height = 620;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 9F);
            this.MinimumSize = new Size(700, 500);

            _tabControl = new TabControl { Dock = DockStyle.Fill };

            // === 导出页 ===
            var tabExport = new TabPage("导出表格");
            var exportPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var lblCat = new Label { Text = "构件类别：", Left = 0, Top = 5, AutoSize = true };
            _cmbCategory = new WinComboBox
            {
                Left = 80, Top = 2, Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbCategory.SelectedIndexChanged += (s, e) => LoadParams();

            _rbSelected = new RadioButton { Text = "仅选中构件", Left = 300, Top = 5, AutoSize = true };
            _rbAll = new RadioButton { Text = "全部构件", Left = 400, Top = 5, AutoSize = true, Checked = true };

            var lblParams = new Label { Text = "选择要导出的参数：", Left = 0, Top = 40, AutoSize = true };

            _clbParams = new CheckedListBox
            {
                Left = 0, Top = 65, Width = 350, Height = 350,
                CheckOnClick = true
            };

            _btnSelectAllParams = new Button { Text = "全选", Left = 360, Top = 65, Width = 70, Height = 28 };
            _btnSelectAllParams.Click += (s, e) =>
            {
                for (int i = 0; i < _clbParams.Items.Count; i++)
                    _clbParams.SetItemChecked(i, true);
            };

            _btnDeselectAllParams = new Button { Text = "全不选", Left = 360, Top = 98, Width = 70, Height = 28 };
            _btnDeselectAllParams.Click += (s, e) =>
            {
                for (int i = 0; i < _clbParams.Items.Count; i++)
                    _clbParams.SetItemChecked(i, false);
            };

            _lblExportInfo = new Label
            {
                Left = 360, Top = 140, Width = 300, Height = 100,
                ForeColor = Color.Gray
            };
            _lblExportInfo.Text = "使用说明：\n1. 选择类别和类型参数\n2. 点击「导出表格」保存CSV\n3. 用Excel打开CSV填写数据\n4. 切换到「导入表格」回导\n\n注：每行代表一个族类型\n修改类型参数=修改该类型下所有实例";

            _btnExport = new Button
            {
                Text = "导出表格",
                Left = 0, Top = 430, Width = 120, Height = 36,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnExport.Click += BtnExport_Click;

            exportPanel.Controls.Add(lblCat);
            exportPanel.Controls.Add(_cmbCategory);
            exportPanel.Controls.Add(_rbSelected);
            exportPanel.Controls.Add(_rbAll);
            exportPanel.Controls.Add(lblParams);
            exportPanel.Controls.Add(_clbParams);
            exportPanel.Controls.Add(_btnSelectAllParams);
            exportPanel.Controls.Add(_btnDeselectAllParams);
            exportPanel.Controls.Add(_lblExportInfo);
            exportPanel.Controls.Add(_btnExport);
            tabExport.Controls.Add(exportPanel);

            // === 导入页 ===
            var tabImport = new TabPage("导入表格");
            var importPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

            var lblFile = new Label { Text = "文件：", Left = 0, Top = 5, AutoSize = true };
            _txtFilePath = new WinTextBox { Left = 50, Top = 2, Width = 500, ReadOnly = true };
            _btnBrowse = new Button { Text = "浏览...", Left = 560, Top = 0, Width = 80, Height = 28 };
            _btnBrowse.Click += BtnBrowse_Click;

            _dgvImport = new DataGridView
            {
                Left = 0, Top = 40, Width = 820, Height = 380,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("微软雅黑", 9F) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font("微软雅黑", 9F, FontStyle.Bold) }
            };

            _lblImportInfo = new Label { Left = 0, Top = 430, Width = 400, Height = 25, ForeColor = Color.Blue };

            _btnImport = new Button
            {
                Text = "导入更新",
                Left = 700, Top = 425, Width = 120, Height = 36,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnImport.Click += BtnImport_Click;

            importPanel.Controls.Add(lblFile);
            importPanel.Controls.Add(_txtFilePath);
            importPanel.Controls.Add(_btnBrowse);
            importPanel.Controls.Add(_dgvImport);
            importPanel.Controls.Add(_lblImportInfo);
            importPanel.Controls.Add(_btnImport);
            tabImport.Controls.Add(importPanel);

            _tabControl.TabPages.Add(tabExport);
            _tabControl.TabPages.Add(tabImport);
            this.Controls.Add(_tabControl);
        }

        // ============================================================
        // 类别与参数加载
        // ============================================================

        private void LoadCategories()
        {
            _categories = new List<CatItem>
            {
                new CatItem { Bic = BuiltInCategory.OST_MechanicalEquipment, Name = "机械设备" },
                new CatItem { Bic = BuiltInCategory.OST_ElectricalEquipment, Name = "电气设备" },
                new CatItem { Bic = BuiltInCategory.OST_ElectricalFixtures, Name = "电气装置" },
                new CatItem { Bic = BuiltInCategory.OST_LightingFixtures, Name = "照明设备" },
                new CatItem { Bic = BuiltInCategory.OST_PlumbingFixtures, Name = "卫浴装置" },
                new CatItem { Bic = BuiltInCategory.OST_PipeAccessory, Name = "管道附件" },
                new CatItem { Bic = BuiltInCategory.OST_DuctAccessory, Name = "风管附件" },
                new CatItem { Bic = BuiltInCategory.OST_CommunicationDevices, Name = "通讯设备" },
                new CatItem { Bic = BuiltInCategory.OST_FireAlarmDevices, Name = "火灾报警设备" },
                new CatItem { Bic = BuiltInCategory.OST_SpecialityEquipment, Name = "专用设备" },
                new CatItem { Bic = BuiltInCategory.OST_PipeCurves, Name = "管道" },
                new CatItem { Bic = BuiltInCategory.OST_DuctCurves, Name = "风管" },
                new CatItem { Bic = BuiltInCategory.OST_Windows, Name = "窗" },
                new CatItem { Bic = BuiltInCategory.OST_Doors, Name = "门" }
            };

            _cmbCategory.Items.Clear();
            foreach (var c in _categories)
                _cmbCategory.Items.Add(c);

            if (_cmbCategory.Items.Count > 0)
                _cmbCategory.SelectedIndex = 0;
        }

        private void LoadParams()
        {
            _clbParams.Items.Clear();
            _exportParams.Clear();

            var catItem = _cmbCategory.SelectedItem as CatItem;
            if (catItem == null) return;

            // 取该类别的第一个ElementType的类型参数
            var firstType = new FilteredElementCollector(_doc)
                .OfCategory(catItem.Bic)
                .WhereElementIsElementType()
                .FirstOrDefault() as ElementType;

            if (firstType == null) return;

            var seen = new HashSet<string>();
            foreach (Parameter p in firstType.Parameters)
            {
                if (p == null || p.IsReadOnly) continue;
                if (p.StorageType == StorageType.None) continue;
                string name = p.Definition?.Name ?? "";
                if (string.IsNullOrEmpty(name) || seen.Contains(name)) continue;
                seen.Add(name);

                var item = new ParamCheckItem { Name = name, StorageType = p.StorageType };
                _exportParams.Add(item);
                _clbParams.Items.Add(item, false);
            }
        }

        // ============================================================
        // 导出
        // ============================================================

        private void BtnExport_Click(object sender, EventArgs e)
        {
            var catItem = _cmbCategory.SelectedItem as CatItem;
            if (catItem == null) { MessageBox.Show("请选择构件类别。"); return; }

            // 收集选中的参数
            var paramNames = new List<string>();
            for (int i = 0; i < _clbParams.Items.Count; i++)
            {
                if (_clbParams.GetItemChecked(i))
                {
                    var item = _clbParams.Items[i] as ParamCheckItem;
                    if (item != null) paramNames.Add(item.Name);
                }
            }

            if (paramNames.Count == 0)
            {
                MessageBox.Show("请至少选择一个要导出的参数。");
                return;
            }

            // 收集ElementType（类型）
            List<ElementType> typeElements;
            if (_rbSelected.Checked && _selectedIds.Count > 0)
            {
                // 从选中实例提取类型，去重
                var typeIds = new HashSet<ElementId>();
                foreach (var id in _selectedIds)
                {
                    var elem = _doc.GetElement(id);
                    if (elem == null || elem is ElementType) continue;
                    if (elem.Category == null || elem.Category.Id.IntegerValue != (int)catItem.Bic) continue;
                    typeIds.Add(elem.GetTypeId());
                }
                typeElements = typeIds
                    .Select(tid => _doc.GetElement(tid) as ElementType)
                    .Where(t => t != null)
                    .ToList();
            }
            else
            {
                typeElements = new FilteredElementCollector(_doc)
                    .OfCategory(catItem.Bic)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();
            }

            if (typeElements.Count == 0)
            {
                MessageBox.Show("未找到符合条件的类型。");
                return;
            }

            // 保存文件
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV 文件 (*.csv)|*.csv";
                sfd.FileName = $"{catItem.Name}_类型导出_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                if (sfd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    ExportToCsv(sfd.FileName, typeElements, paramNames);
                    MessageBox.Show($"导出成功！\n\n文件：{sfd.FileName}\n类型数：{typeElements.Count}\n参数数：{paramNames.Count}\n\n请用 Excel 打开此文件，填写数据后保存。\n注意：保存时请选择 CSV 格式。",
                        "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败：" + ex.Message, "错误");
                }
            }
        }

        private void ExportToCsv(string path, List<ElementType> typeElements, List<string> paramNames)
        {
            // UTF-8 with BOM（确保 Excel 正确识别中文）
            var encoding = new UTF8Encoding(true);

            using (var writer = new StreamWriter(path, false, encoding))
            {
                // 表头
                var headers = new List<string> { "TypeId", "类别", "族名称", "类型名称" };
                headers.AddRange(paramNames);
                writer.WriteLine(string.Join(",", headers.Select(EscapeCsv)));

                // 数据行
                foreach (var typ in typeElements)
                {
                    var row = new List<string>();
                    row.Add(typ.Id.IntegerValue.ToString());
                    row.Add(typ.Category?.Name ?? "");
                    row.Add(typ.FamilyName ?? "");
                    row.Add(typ.Name ?? "");

                    foreach (var paramName in paramNames)
                    {
                        string val = ReadParamValue(typ, paramName);
                        row.Add(val);
                    }

                    writer.WriteLine(string.Join(",", row.Select(EscapeCsv)));
                }
            }
        }

        private string ReadParamValue(ElementType typ, string paramName)
        {
            try
            {
                var param = typ.LookupParameter(paramName);
                if (param == null) return "";

                // 优先使用 AsValueString 获取显示值
                string val = param.AsValueString();
                if (!string.IsNullOrEmpty(val)) return val;

                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.AsString() ?? "";
                    case StorageType.Double:
                        return param.AsDouble().ToString("F3");
                    case StorageType.Integer:
                        return param.AsInteger().ToString();
                    case StorageType.ElementId:
                        var id = param.AsElementId();
                        if (id == null || id == ElementId.InvalidElementId) return "";
                        var refElem = _doc.GetElement(id);
                        return refElem?.Name ?? "";
                    default:
                        return "";
                }
            }
            catch { return ""; }
        }

        // ============================================================
        // 导入
        // ============================================================

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
                if (ofd.ShowDialog() != DialogResult.OK) return;

                _txtFilePath.Text = ofd.FileName;
                LoadImportFile(ofd.FileName);
            }
        }

        private void LoadImportFile(string path)
        {
            _importHeaders.Clear();
            _importRows.Clear();

            try
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 0) { MessageBox.Show("文件为空。"); return; }

                // 解析表头
                _importHeaders = ParseCsvLine(lines[0]);

                // 验证必须有 TypeId 列
                if (!_importHeaders.Any(h => h == "TypeId"))
                {
                    MessageBox.Show("文件格式错误：缺少 TypeId 列。请确保使用本插件导出的CSV文件。");
                    return;
                }

                // 解析数据行
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Count == 0) continue;

                    var row = new ImportRow();
                    int idIdx = _importHeaders.IndexOf("TypeId");
                    if (idIdx >= 0 && idIdx < fields.Count)
                    {
                        int tid;
                        if (int.TryParse(fields[idIdx], out tid))
                            row.TypeId = tid;
                    }

                    for (int j = 0; j < _importHeaders.Count && j < fields.Count; j++)
                    {
                        row.Values[_importHeaders[j]] = fields[j];
                    }

                    // 检查类型是否存在
                    var elem = _doc.GetElement(new ElementId(row.TypeId));
                    row.Found = elem != null && elem.IsValidObject && elem is ElementType;
                    row.Status = row.Found ? "可更新" : "类型不存在";

                    _importRows.Add(row);
                }

                // 显示预览
                ShowImportPreview();

                int foundCount = _importRows.Count(r => r.Found);
                _lblImportInfo.Text = $"共 {_importRows.Count} 行数据，其中 {foundCount} 行可更新";
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取文件失败：" + ex.Message, "错误");
            }
        }

        private void ShowImportPreview()
        {
            var dt = new System.Data.DataTable();

            foreach (var header in _importHeaders)
                dt.Columns.Add(header);

            // 添加状态列
            dt.Columns.Add("状态");

            foreach (var row in _importRows)
            {
                var dr = dt.NewRow();
                foreach (var header in _importHeaders)
                {
                    dr[header] = row.Values.ContainsKey(header) ? row.Values[header] : "";
                }
                dr["状态"] = row.Status;
                dt.Rows.Add(dr);
            }

            _dgvImport.DataSource = dt;

            // 标记不可更新的行
            foreach (DataGridViewRow dgvRow in _dgvImport.Rows)
            {
                var row = _importRows[dgvRow.Index];
                if (!row.Found)
                {
                    dgvRow.DefaultCellStyle.BackColor = Color.MistyRose;
                }
            }
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            if (_importRows.Count == 0)
            {
                MessageBox.Show("请先选择要导入的CSV文件。");
                return;
            }

            var updatableRows = _importRows.Where(r => r.Found).ToList();
            if (updatableRows.Count == 0)
            {
                MessageBox.Show("没有可更新的数据。");
                return;
            }

            // 获取参数列（排除非参数列）
            var paramColumns = _importHeaders
                .Where(h => h != "TypeId" && h != "类别" && h != "族名称" && h != "类型名称")
                .ToList();

            if (paramColumns.Count == 0)
            {
                MessageBox.Show("文件中没有可导入的参数列。");
                return;
            }

            // 确认
            string confirm = $"将更新 {updatableRows.Count} 个类型的 {paramColumns.Count} 个类型参数：\n\n";
            foreach (var p in paramColumns.Take(8)) confirm += "  • " + p + "\n";
            if (paramColumns.Count > 8) confirm += "  ... 另 " + (paramColumns.Count - 8) + " 个\n";
            confirm += "\n继续？";

            if (MessageBox.Show(confirm, "确认导入", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int ok = 0, fail = 0, skip = 0;
            var errors = new List<string>();

            using (var tx = new Transaction(_doc, "导入表格数据"))
            {
                tx.Start();
                try
                {
                    foreach (var row in updatableRows)
                    {
                        try
                        {
                            var elem = _doc.GetElement(new ElementId(row.TypeId)) as ElementType;
                            if (elem == null || !elem.IsValidObject) { skip++; continue; }

                            bool anyChanged = false;
                            foreach (var paramName in paramColumns)
                            {
                                if (!row.Values.ContainsKey(paramName)) continue;
                                string val = row.Values[paramName];
                                if (string.IsNullOrEmpty(val)) continue;

                                var param = elem.LookupParameter(paramName);
                                if (param == null || param.IsReadOnly) continue;

                                if (SetParamValue(param, val))
                                    anyChanged = true;
                            }

                            if (anyChanged) ok++; else skip++;
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            if (errors.Count < 10) errors.Add($"TypeID={row.TypeId}: {ex.Message}");
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
                    MessageBox.Show("导入失败：" + ex.Message, "错误");
                    return;
                }
            }

            string msg = $"导入完成\n\n  成功更新类型：{ok} 个\n  跳过（无变化）：{skip} 个\n  失败：{fail} 个";
            if (errors.Count > 0)
            {
                msg += "\n\n--- 错误明细 ---\n";
                foreach (var err in errors) msg += "  " + err + "\n";
            }

            MessageBox.Show(msg, "完成", MessageBoxButtons.OK,
                fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            // 刷新预览状态
            foreach (var row in _importRows)
            {
                if (row.Found) row.Status = "已更新";
            }
            ShowImportPreview();
        }

        // ============================================================
        // 参数写入
        // ============================================================

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
                        if (value == "是" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                            return param.Set(1);
                        if (value == "否" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                            return param.Set(0);
                        return false;

                    case StorageType.ElementId:
                        var matched = new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType()
                            .FirstOrDefault(e2 => e2.Name == value);
                        if (matched != null)
                            return param.Set(matched.Id);
                        return false;

                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        // ============================================================
        // CSV 工具方法
        // ============================================================

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                            inQuotes = false;
                    }
                    else
                        current.Append(c);
                }
                else
                {
                    if (c == ',')
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    else if (c == '"')
                        inQuotes = true;
                    else
                        current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }
    }
}
