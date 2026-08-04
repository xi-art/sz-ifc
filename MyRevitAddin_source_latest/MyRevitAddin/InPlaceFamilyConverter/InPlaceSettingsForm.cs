using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MyRevitAddin.InPlaceFamilyConverter
{
    public partial class InPlaceSettingsForm : Form
    {
        private readonly Document _document;
        private readonly FamilyInstance _inPlaceInstance;

        public string TargetFilePath { get; private set; }
        public bool DeleteOriginal { get; private set; }
        public bool ReplaceInProject { get; private set; }
        public string CustomTemplatePath { get; private set; }

        public InPlaceSettingsForm(Document document, FamilyInstance inPlaceInstance)
        {
            _document = document;
            _inPlaceInstance = inPlaceInstance;

            InitializeComponent();
            InitializeValues();
        }

        private void InitializeComponent()
        {
            this.Text = "内建族转可载入族 - 设置";
            this.Size = new System.Drawing.Size(550, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            Label lblInfo = new Label();
            lblInfo.Text = $"选中的内建族: {_inPlaceInstance.Name}";
            lblInfo.Location = new System.Drawing.Point(12, 15);
            lblInfo.Size = new System.Drawing.Size(500, 20);
            lblInfo.Font = new System.Drawing.Font(lblInfo.Font, System.Drawing.FontStyle.Bold);
            this.Controls.Add(lblInfo);

            string categoryName = _inPlaceInstance.Category?.Name ?? "未知";
            Label lblCategory = new Label();
            lblCategory.Text = $"类别: {categoryName}";
            lblCategory.Location = new System.Drawing.Point(12, 40);
            lblCategory.Size = new System.Drawing.Size(500, 20);
            this.Controls.Add(lblCategory);

            Label lblPath = new Label();
            lblPath.Text = "保存路径:";
            lblPath.Location = new System.Drawing.Point(12, 75);
            lblPath.Size = new System.Drawing.Size(70, 20);
            this.Controls.Add(lblPath);

            TextBox txtPath = new TextBox();
            txtPath.Name = "txtPath";
            txtPath.Location = new System.Drawing.Point(85, 72);
            txtPath.Size = new System.Drawing.Size(350, 20);
            txtPath.ReadOnly = true;
            this.Controls.Add(txtPath);

            Button btnBrowse = new Button();
            btnBrowse.Text = "浏览...";
            btnBrowse.Location = new System.Drawing.Point(445, 70);
            btnBrowse.Size = new System.Drawing.Size(75, 25);
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);

            Label lblTemplate = new Label();
            lblTemplate.Text = "族模板:";
            lblTemplate.Location = new System.Drawing.Point(12, 105);
            lblTemplate.Size = new System.Drawing.Size(70, 20);
            this.Controls.Add(lblTemplate);

            TextBox txtTemplate = new TextBox();
            txtTemplate.Name = "txtTemplate";
            txtTemplate.Location = new System.Drawing.Point(85, 102);
            txtTemplate.Size = new System.Drawing.Size(350, 20);
            txtTemplate.ReadOnly = true;
            this.Controls.Add(txtTemplate);

            Button btnTemplateBrowse = new Button();
            btnTemplateBrowse.Text = "浏览模板...";
            btnTemplateBrowse.Location = new System.Drawing.Point(445, 100);
            btnTemplateBrowse.Size = new System.Drawing.Size(85, 25);
            btnTemplateBrowse.Click += BtnTemplateBrowse_Click;
            this.Controls.Add(btnTemplateBrowse);

            CheckBox chkReplace = new CheckBox();
            chkReplace.Name = "chkReplace";
            chkReplace.Text = "在项目中用新族替换原内建族实例";
            chkReplace.Location = new System.Drawing.Point(15, 140);
            chkReplace.Size = new System.Drawing.Size(350, 20);
            chkReplace.Checked = true;
            this.Controls.Add(chkReplace);

            CheckBox chkDelete = new CheckBox();
            chkDelete.Name = "chkDelete";
            chkDelete.Text = "替换后删除原内建族实例";
            chkDelete.Location = new System.Drawing.Point(35, 165);
            chkDelete.Size = new System.Drawing.Size(300, 20);
            chkDelete.Checked = true;
            this.Controls.Add(chkDelete);

            Label lblNote = new Label();
            lblNote.Text = "说明:\n• 转换过程会保留内建族的几何形状和参数。\n" +
                          "• 新生成的可载入族可保存为 .rfa 供其他项目复用。\n" +
                          "• 如提示找不到模板，请点击\"浏览模板...\"选择任意 .rft（建议 Generic Model.rft）。";
            lblNote.Location = new System.Drawing.Point(12, 200);
            lblNote.Size = new System.Drawing.Size(510, 80);
            this.Controls.Add(lblNote);

            Button btnOK = new Button();
            btnOK.Text = "确定";
            btnOK.DialogResult = DialogResult.OK;
            btnOK.Location = new System.Drawing.Point(350, 315);
            btnOK.Size = new System.Drawing.Size(80, 28);
            this.Controls.Add(btnOK);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new System.Drawing.Point(440, 315);
            btnCancel.Size = new System.Drawing.Size(80, 28);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void InitializeValues()
        {
            string defaultName = SanitizeFileName(_inPlaceInstance.Name);
            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"{defaultName}.rfa");

            TextBox txtPath = this.Controls.Find("txtPath", true)[0] as TextBox;
            if (txtPath != null)
            {
                txtPath.Text = defaultPath;
                TargetFilePath = defaultPath;
            }

            string autoTemplate = FindTemplateAuto();
            TextBox txtTemplate = this.Controls.Find("txtTemplate", true)[0] as TextBox;
            if (txtTemplate != null && !string.IsNullOrEmpty(autoTemplate))
            {
                txtTemplate.Text = autoTemplate;
                CustomTemplatePath = autoTemplate;
            }
        }

        private string FindTemplateAuto()
        {
            try
            {
                List<string> candidateDrives = new List<string> { "F", "E", "D", "C" };
                foreach (string drv in candidateDrives)
                {
                    string root = $@"{drv}:\";
                    try
                    {
                        string[] found = Directory.GetFiles(root, "Generic Model.rft", SearchOption.AllDirectories);
                        if (found != null && found.Length > 0) return found[0];
                    }
                    catch { }
                }

                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string[] fallbacks = new[]
                {
                    Path.Combine(pf, "Autodesk", "FormIt Converter For Revit 2018", "FormItConversionAddon", "Resources", "2016", "Templates", "Metric", "Generic Model.rft"),
                    Path.Combine(pf, "Autodesk", "FormIt Converter For Revit 2020", "FormItConversionAddon", "Resources", "Templates", "Metric", "Generic Model.rft"),
                };
                foreach (string p in fallbacks)
                {
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        private void BtnTemplateBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Revit族模板 (*.rft)|*.rft|所有文件 (*.*)|*.*";
                dlg.Title = "选择族模板文件（推荐 Generic Model.rft）";
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string[] initials = new[]
                {
                    Path.Combine(pf, "Autodesk", "FormIt Converter For Revit 2018", "FormItConversionAddon", "Resources", "2016", "Templates", "Metric"),
                    Path.Combine(pf, "Autodesk"),
                };
                foreach (string init in initials)
                {
                    if (Directory.Exists(init)) { dlg.InitialDirectory = init; break; }
                }

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    TextBox txtTemplate = this.Controls.Find("txtTemplate", true)[0] as TextBox;
                    if (txtTemplate != null)
                    {
                        txtTemplate.Text = dlg.FileName;
                        CustomTemplatePath = dlg.FileName;
                    }
                }
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "Revit族文件 (*.rfa)|*.rfa";
                saveDialog.Title = "选择保存位置";
                saveDialog.FileName = Path.GetFileName(TargetFilePath);
                saveDialog.InitialDirectory = Path.GetDirectoryName(TargetFilePath);

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    TextBox txtPath = this.Controls.Find("txtPath", true)[0] as TextBox;
                    if (txtPath != null)
                    {
                        txtPath.Text = saveDialog.FileName;
                        TargetFilePath = saveDialog.FileName;
                    }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                TextBox txtPath = this.Controls.Find("txtPath", true)[0] as TextBox;
                if (txtPath != null)
                {
                    TargetFilePath = txtPath.Text;
                }

                TextBox txtTemplate = this.Controls.Find("txtTemplate", true)[0] as TextBox;
                if (txtTemplate != null && !string.IsNullOrWhiteSpace(txtTemplate.Text))
                {
                    CustomTemplatePath = txtTemplate.Text;
                }

                if (string.IsNullOrWhiteSpace(TargetFilePath))
                {
                    MessageBox.Show("请选择保存路径。", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }

                if (!TargetFilePath.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                {
                    TargetFilePath += ".rfa";
                }

                if (!string.IsNullOrWhiteSpace(CustomTemplatePath) && !File.Exists(CustomTemplatePath))
                {
                    MessageBox.Show("选择的族模板文件不存在，请检查路径或重新浏览。", "验证错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }

                CheckBox chkReplace = this.Controls.Find("chkReplace", true)[0] as CheckBox;
                CheckBox chkDelete = this.Controls.Find("chkDelete", true)[0] as CheckBox;

                ReplaceInProject = chkReplace?.Checked ?? false;
                DeleteOriginal = chkDelete?.Checked ?? false;

                string directory = Path.GetDirectoryName(TargetFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"无法创建目录: {ex.Message}", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        e.Cancel = true;
                    }
                }
            }

            base.OnFormClosing(e);
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "NewFamily";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }
    }
}
