using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MyRevitAddin
{
    /// <summary>
    /// 选择两个标高的对话框
    /// </summary>
    public class ReassignLevelDialog : Form
    {
        private ComboBox _cmbLower;
        private ComboBox _cmbUpper;
        private List<Level> _levels;

        public Level SelectedLowerLevel => _cmbLower.SelectedItem as Level;
        public Level SelectedUpperLevel => _cmbUpper.SelectedItem as Level;

        public ReassignLevelDialog(List<Level> levels)
        {
            _levels = levels;
            InitializeComponent();
            LoadLevels();
        }

        private void InitializeComponent()
        {
            this.Text = "重新归属构件标高";
            this.Width = 420;
            this.Height = 200;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            var lblTitle = new Label
            {
                Text = "请选择标高范围：构件底部标高在此范围内，将被重新归属到下部标高。",
                Location = new System.Drawing.Point(16, 16),
                Size = new System.Drawing.Size(380, 36),
                ForeColor = System.Drawing.Color.FromArgb(64, 64, 64)
            };

            var lblLower = new Label
            {
                Text = "下部标高（目标归属）：",
                Location = new System.Drawing.Point(16, 62),
                Size = new System.Drawing.Size(160, 20)
            };

            _cmbLower = new ComboBox
            {
                Location = new System.Drawing.Point(180, 60),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Name"
            };

            var lblUpper = new Label
            {
                Text = "上部标高（不含）：",
                Location = new System.Drawing.Point(16, 96),
                Size = new System.Drawing.Size(160, 20)
            };

            _cmbUpper = new ComboBox
            {
                Location = new System.Drawing.Point(180, 94),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Name"
            };

            var btnOk = new Button
            {
                Text = "确定",
                Location = new System.Drawing.Point(208, 130),
                Width = 80,
                Height = 28,
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "取消",
                Location = new System.Drawing.Point(296, 130),
                Width = 80,
                Height = 28,
                DialogResult = DialogResult.Cancel
            };

            btnOk.Click += (s, e) =>
            {
                if (_cmbLower.SelectedIndex < 0 || _cmbUpper.SelectedIndex < 0)
                {
                    MessageBox.Show("请先选择两个标高。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }
                if (_cmbLower.SelectedIndex >= _cmbUpper.SelectedIndex)
                {
                    MessageBox.Show("上部标高必须高于下部标高。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.AddRange(new Control[] {
                lblTitle, lblLower, _cmbLower,
                lblUpper, _cmbUpper,
                btnOk, btnCancel
            });
        }

        private void LoadLevels()
        {
            foreach (var level in _levels)
            {
                string display = $"{level.Name}  (标高: {Utils.FormatFeet(level.Elevation)})";
                _cmbLower.Items.Add(level);
                _cmbUpper.Items.Add(level);
            }

            if (_levels.Count >= 2)
            {
                // 默认：最低 → 次低
                _cmbLower.SelectedIndex = 0;
                _cmbUpper.SelectedIndex = 1;
            }
        }
    }
}
