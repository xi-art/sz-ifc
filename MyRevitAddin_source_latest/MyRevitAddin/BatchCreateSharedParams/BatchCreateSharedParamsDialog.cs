using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchCreateSharedParams
{
    using WinComboBox = System.Windows.Forms.ComboBox;
    using WinTextBox = System.Windows.Forms.TextBox;

    internal class BatchCreateSharedParamsDialog : Form
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        private WinComboBox _cmbCategory;
        private WinComboBox _cmbParamType;
        private WinComboBox _cmbParamGroup;
        private CheckBox _chkUserModifiable;
        private WinTextBox _txtNames;
        private Label _lblCount;
        private Button _btnOK;
        private Button _btnCancel;

        private List<Category> _allCategories;

        public BatchCreateSharedParamsDialog(UIApplication uiApp, Document doc)
        {
            _uiApp = uiApp;
            _doc = doc;
            InitializeComponent();
            LoadCategories();
            LoadParamTypes();
            LoadParamGroups();
            UpdateCount();
        }

        private void InitializeComponent()
        {
            this.Text = "批量创建项目参数";
            this.Width = 720;
            this.Height = 560;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 9F);
            this.MinimumSize = new Size(640, 440);

            var lblCat = new Label { Text = "目标类别：", Left = 20, Top = 20, Width = 80, AutoSize = true };
            _cmbCategory = new WinComboBox { Left = 100, Top = 17, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };

            var grpSettings = new GroupBox { Text = "参数设置（所有参数共用，固定为实例参数）", Left = 20, Top = 55, Width = 660, Height = 110 };

            var lblType = new Label { Text = "参数类型：", Left = 15, Top = 30, Width = 70, AutoSize = true };
            _cmbParamType = new WinComboBox { Left = 90, Top = 27, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };

            var lblGroup = new Label { Text = "参数组：", Left = 250, Top = 30, Width = 60, AutoSize = true };
            _cmbParamGroup = new WinComboBox { Left = 315, Top = 27, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };

            _chkUserModifiable = new CheckBox { Text = "用户可修改", Left = 15, Top = 65, Width = 100, Checked = true };

            var lblHint = new Label
            {
                Text = "提示：批量创建实例参数，仅名称不同，其他设置一致",
                Left = 130,
                Top = 67,
                Width = 500,
                ForeColor = Color.Gray,
                AutoSize = true
            };

            grpSettings.Controls.Add(lblType);
            grpSettings.Controls.Add(_cmbParamType);
            grpSettings.Controls.Add(lblGroup);
            grpSettings.Controls.Add(_cmbParamGroup);
            grpSettings.Controls.Add(_chkUserModifiable);
            grpSettings.Controls.Add(lblHint);

            var lblNames = new Label { Text = "参数名称（每行一个）：", Left = 20, Top = 180, AutoSize = true };
            _lblCount = new Label { Text = "0 个", Left = 580, Top = 180, ForeColor = Color.Blue, AutoSize = true };

            _txtNames = new WinTextBox
            {
                Left = 20,
                Top = 205,
                Width = 660,
                Height = 250,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _txtNames.TextChanged += (s, e) => UpdateCount();

            int bottom = this.ClientSize.Height - 70;
            _btnOK = new Button
            {
                Text = "创建",
                Left = 460,
                Width = 100,
                Height = 32,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnOK.Top = bottom;
            _btnOK.Click += BtnOK_Click;

            _btnCancel = new Button
            {
                Text = "取消",
                Left = 580,
                Width = 100,
                Height = 32,
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnCancel.Top = bottom;

            this.Controls.Add(lblCat);
            this.Controls.Add(_cmbCategory);
            this.Controls.Add(grpSettings);
            this.Controls.Add(lblNames);
            this.Controls.Add(_lblCount);
            this.Controls.Add(_txtNames);
            this.Controls.Add(_btnOK);
            this.Controls.Add(_btnCancel);

            this.AcceptButton = _btnOK;
            this.CancelButton = _btnCancel;
        }

        private void LoadCategories()
        {
            _allCategories = new List<Category>();
            try
            {
                Categories cats = _doc.Settings.Categories;
                foreach (Category cat in cats)
                {
                    if (cat == null) continue;
                    if (!cat.AllowsBoundParameters) continue;
                    if (cat.CategoryType != CategoryType.Model && cat.CategoryType != CategoryType.Annotation) continue;
                    _allCategories.Add(cat);
                }
                _allCategories = _allCategories.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex) { MiniLog.Error("LoadCategories", ex); }

            foreach (var cat in _allCategories)
                _cmbCategory.Items.Add(cat.Name);

            int wallIdx = _allCategories.FindIndex(c => c.Name == "墙");
            if (wallIdx >= 0) _cmbCategory.SelectedIndex = wallIdx;
            else if (_cmbCategory.Items.Count > 0) _cmbCategory.SelectedIndex = 0;
        }

        private void LoadParamTypes()
        {
            var commonTypes = new List<Tuple<string, ParameterType>>
            {
                Tuple.Create("文字", ParameterType.Text),
                Tuple.Create("整数", ParameterType.Integer),
                Tuple.Create("数值", ParameterType.Number),
                Tuple.Create("长度", ParameterType.Length),
                Tuple.Create("面积", ParameterType.Area),
                Tuple.Create("体积", ParameterType.Volume),
                Tuple.Create("角度", ParameterType.Angle),
                Tuple.Create("是/否", ParameterType.YesNo),
                Tuple.Create("材质", ParameterType.Material),
                Tuple.Create("族类型", ParameterType.FamilyType),
                Tuple.Create("URL", ParameterType.URL),
                Tuple.Create("图像", ParameterType.Image),
            };

            _cmbParamType.DisplayMember = "Item1";
            _cmbParamType.ValueMember = "Item2";
            foreach (var t in commonTypes)
                _cmbParamType.Items.Add(t);

            _cmbParamType.SelectedIndex = 0;
        }

        private void LoadParamGroups()
        {
            var groups = new List<Tuple<string, BuiltInParameterGroup>>
            {
                Tuple.Create("标识数据", BuiltInParameterGroup.PG_IDENTITY_DATA),
                Tuple.Create("尺寸标注", BuiltInParameterGroup.PG_GEOMETRY),
                Tuple.Create("材质和装饰", BuiltInParameterGroup.PG_MATERIALS),
                Tuple.Create("分析属性", BuiltInParameterGroup.PG_ANALYSIS_RESULTS),
                Tuple.Create("结构", BuiltInParameterGroup.PG_STRUCTURAL),
                Tuple.Create("电气", BuiltInParameterGroup.PG_ELECTRICAL),
                Tuple.Create("管道", BuiltInParameterGroup.PG_MECHANICAL),
                Tuple.Create("文字", BuiltInParameterGroup.PG_TEXT),
                Tuple.Create("数据", BuiltInParameterGroup.PG_DATA),
                Tuple.Create("约束", BuiltInParameterGroup.PG_CONSTRAINTS),
            };

            _cmbParamGroup.DisplayMember = "Item1";
            _cmbParamGroup.ValueMember = "Item2";
            foreach (var g in groups)
                _cmbParamGroup.Items.Add(g);

            _cmbParamGroup.SelectedIndex = 0;
        }

        private void UpdateCount()
        {
            int n = GetParamNames().Count;
            _lblCount.Text = n + " 个";
            _btnOK.Enabled = n > 0;
        }

        private List<string> GetParamNames()
        {
            var names = new List<string>();
            var lines = (_txtNames.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                string t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (seen.Contains(t)) continue;
                seen.Add(t);
                names.Add(t);
            }
            return names;
        }

        private Category GetSelectedCategory()
        {
            int idx = _cmbCategory.SelectedIndex;
            if (idx < 0 || idx >= _allCategories.Count) return null;
            return _allCategories[idx];
        }

        private ParameterType GetSelectedParamType()
        {
            int idx = _cmbParamType.SelectedIndex;
            if (idx < 0 || idx >= _cmbParamType.Items.Count) return ParameterType.Text;
            var item = _cmbParamType.Items[idx] as Tuple<string, ParameterType>;
            if (item == null) return ParameterType.Text;
            return item.Item2;
        }

        private BuiltInParameterGroup GetSelectedParamGroup()
        {
            dynamic item = _cmbParamGroup.SelectedItem;
            if (item == null) return BuiltInParameterGroup.PG_DATA;
            return (BuiltInParameterGroup)item.Item2;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            var names = GetParamNames();
            if (names.Count == 0) { MessageBox.Show("请至少输入一个参数名称。"); return; }

            Category cat = GetSelectedCategory();
            if (cat == null) { MessageBox.Show("请选择目标类别。"); return; }

            ParameterType pType = GetSelectedParamType();
            BuiltInParameterGroup pGroup = GetSelectedParamGroup();
            bool userMod = _chkUserModifiable.Checked;

            // 确认
            string confirm = "将对「" + cat.Name + "」创建 " + names.Count + " 个实例参数：\n\n";
            foreach (var n in names.Take(8)) confirm += "  • " + n + "\n";
            if (names.Count > 8) confirm += "  ... 另 " + (names.Count - 8) + " 个\n";
            confirm += "\n参数类型：" + pType + "\n参数组：" + _cmbParamGroup.Text + "\n\n继续？";

            if (MessageBox.Show(confirm, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int ok = 0, skip = 0, fail = 0;
            var diag = new List<string>();

            using (var tx = new Transaction(_doc, "批量创建项目参数"))
            {
                tx.Start();
                try
                {
                    // 项目参数：通过共享参数文件创建定义，绑定到文档后显示为"项目参数"
                    DefinitionFile defFile = GetOrCreateSharedParamFile();
                    if (defFile == null)
                    {
                        MessageBox.Show("无法访问共享参数文件。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    string groupName = cat.Name;
                    DefinitionGroup defGroup = null;
                    try { defGroup = defFile.Groups.get_Item(groupName); }
                    catch { }
                    if (defGroup == null)
                        defGroup = defFile.Groups.Create(groupName);

                    BindingMap bm = _doc.ParameterBindings;

                    foreach (var name in names)
                    {
                        try
                        {
                            Definition def = null;
                            try { def = defGroup.Definitions.get_Item(name); }
                            catch { }

                            if (def == null)
                            {
                                // 新定义：使用用户选择的参数类型
                                var opt = new ExternalDefinitionCreationOptions(name, pType);
                                opt.UserModifiable = userMod;
                                opt.Visible = true;
                                def = defGroup.Definitions.Create(opt);
                            }
                            else
                            {
                                // 已有定义：无法修改类型，直接复用
                            }

                            // 检查是否已绑定
                            var existingBinding = bm.get_Item(def) as Autodesk.Revit.DB.ElementBinding;
                            if (existingBinding != null)
                            {
                                // 检查当前类别是否已在绑定中
                                bool catExists = false;
                                foreach (Category existingCat in existingBinding.Categories)
                                {
                                    if (existingCat.Id == cat.Id) { catExists = true; break; }
                                }

                                if (catExists)
                                {
                                    skip++;
                                    diag.Add("已存在(含此类别): " + name);
                                }
                                else
                                {
                                    // 同名参数已绑定到其他类别 → 追加当前类别
                                    var newCatSet = _doc.Application.Create.NewCategorySet();
                                    foreach (Category existingCat in existingBinding.Categories)
                                        newCatSet.Insert(existingCat);
                                    newCatSet.Insert(cat);

                                    var newBinding = _doc.Application.Create.NewInstanceBinding(newCatSet);
                                    bool reinserted = bm.Insert(def, newBinding, pGroup);
                                    if (reinserted) { ok++; diag.Add("已追加「" + cat.Name + "」类别: " + name); }
                                    else { fail++; diag.Add("追加类别失败: " + name); }
                                }
                            }
                            else
                            {
                                // 未绑定 → 新建绑定
                                var catSet = _doc.Application.Create.NewCategorySet();
                                catSet.Insert(cat);
                                var binding = _doc.Application.Create.NewInstanceBinding(catSet);
                                bool inserted = bm.Insert(def, binding, pGroup);
                                if (inserted) { ok++; }
                                else { diag.Add("绑定失败: " + name); fail++; }
                            }
                        }
                        catch (Exception ex)
                        {
                            fail++;
                            diag.Add(name + "  " + ex.Message);
                        }
                    }

                    var res = tx.Commit();
                    if (res != TransactionStatus.Committed)
                    {
                        MessageBox.Show("事务未提交成功（" + res + "）。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    MessageBox.Show("创建失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MiniLog.Error("BatchCreate", ex);
                    return;
                }
            }

            string msg = "创建完成\n\n";
            msg += "  成功：" + ok + " 个\n";
            msg += "  已存在：" + skip + " 个\n";
            msg += "  失败：" + fail + " 个";
            if (diag.Count > 0)
            {
                msg += "\n\n--- 明细（前20条）---\n";
                foreach (var d in diag.Take(20)) msg += "  " + d + "\n";
                if (diag.Count > 20) msg += "  ... 另 " + (diag.Count - 20) + " 条";
            }

            MessageBox.Show(msg, "完成", MessageBoxButtons.OK, fail == 0 && skip == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private DefinitionFile GetOrCreateSharedParamFile()
        {
            try
            {
                string sharedFile = "";
                try { sharedFile = _uiApp.Application.SharedParametersFilename; }
                catch { }

                if (!string.IsNullOrEmpty(sharedFile) && File.Exists(sharedFile))
                {
                    try { return _uiApp.Application.OpenSharedParameterFile(); }
                    catch { }
                }

                string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitSharedParams");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string tempFile = Path.Combine(tempDir, "SharedParameters.txt");
                if (!File.Exists(tempFile))
                    File.WriteAllText(tempFile, "# This is a Revit shared parameter file.\r\n# Do not edit manually.\r\n*META\tVERSION\tMINORVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");

                _uiApp.Application.SharedParametersFilename = tempFile;
                return _uiApp.Application.OpenSharedParameterFile();
            }
            catch (Exception ex)
            {
                MiniLog.Error("GetSharedParamFile", ex);
                return null;
            }
        }
    }
}
