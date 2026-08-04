using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MyRevitAddin.SplitByMepSystem
{
    internal class SplitByMepSystemDialog : Form
    {
        public string OutputDirectory { get; private set; }
        public string DuctFileName { get; private set; }
        public string TrayFileName { get; private set; }
        public string PipeFileName { get; private set; }

        private TextBox _txtDir;
        private TextBox _txtDuct;
        private TextBox _txtTray;
        private TextBox _txtPipe;
        private Label _lblTip;

        public SplitByMepSystemDialog(string defaultDir, string defaultBase)
        {
            OutputDirectory = defaultDir;
            DuctFileName = defaultBase + "_风管系统.rvt";
            TrayFileName = defaultBase + "_桥架系统.rvt";
            PipeFileName = defaultBase + "_水管系统.rvt";

            InitUI();
            UpdateSummary();
        }

        private void InitUI()
        {
            Text = "按系统分离模型 → 风管 / 桥架 / 水管 三份独立文件";
            Size = new Size(680, 430);
            MinimumSize = new Size(640, 400);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18), BackColor = Color.FromArgb(245, 247, 250) };

            // 说明
            var lblHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 58,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 64, 120),
                Text = "把当前模型复制 3 份，每份删除不属于该系统的构件。\n标高、轴网、视图、图纸、标注、建筑结构构件等共享内容三份都保留。"
            };

            var pnlDir = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(0, 10, 0, 0) };
            var lblDir = new Label { Text = "输出目录：", AutoSize = true, Left = 0, Top = 16 };
            _txtDir = new TextBox { Left = 80, Top = 12, Width = 500, Text = OutputDirectory };
            var btnDir = new Button { Text = "浏览…", Left = 586, Top = 11, Size = new Size(60, 26), FlatStyle = FlatStyle.Flat };
            btnDir.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog())
                {
                    d.Description = "选择输出目录";
                    d.SelectedPath = _txtDir.Text;
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        _txtDir.Text = d.SelectedPath;
                        UpdateSummary();
                    }
                }
            };
            pnlDir.Controls.AddRange(new Control[] { lblDir, _txtDir, btnDir });

            // 三行文件名
            var pnlFiles = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(0, 8, 0, 0) };
            pnlFiles.Controls.Add(MakeFileRow(0, "① 风管文件：", Color.FromArgb(60, 130, 190), _txtDuct = new TextBox { Text = DuctFileName }));
            pnlFiles.Controls.Add(MakeFileRow(40, "② 桥架文件：", Color.FromArgb(190, 120, 50), _txtTray = new TextBox { Text = TrayFileName }));
            pnlFiles.Controls.Add(MakeFileRow(80, "③ 水管文件：", Color.FromArgb(60, 160, 90), _txtPipe = new TextBox { Text = PipeFileName }));

            foreach (TextBox t in new[] { _txtDuct, _txtTray, _txtPipe })
                t.TextChanged += (s, e) => UpdateSummary();
            _txtDir.TextChanged += (s, e) => UpdateSummary();

            // 分隔
            var line = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(210, 215, 225) };

            // 预览汇总
            var pnlInfo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 8) };
            _lblTip = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.DarkSlateGray,
                Text = "",
                TextAlign = ContentAlignment.TopLeft
            };
            pnlInfo.Controls.Add(_lblTip);

            // 按钮
            var btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat
            };
            var btnOk = new Button
            {
                Text = "开始分离（复制+清理）",
                Size = new Size(170, 30),
                DialogResult = DialogResult.None,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 130, 200),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            btnOk.Click += (s, e) =>
            {
                if (!ValidateInput()) return;
                OutputDirectory = _txtDir.Text.Trim();
                DuctFileName = AppendRvt(_txtDuct.Text.Trim());
                TrayFileName = AppendRvt(_txtTray.Text.Trim());
                PipeFileName = AppendRvt(_txtPipe.Text.Trim());
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            var pnlBtns = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.FromArgb(235, 240, 248) };
            btnCancel.Location = new Point(pnlBtns.ClientSize.Width - 178, 8);
            btnOk.Location = new Point(pnlBtns.ClientSize.Width - 352, 8);
            pnlBtns.Resize += (s, e) =>
            {
                btnCancel.Location = new Point(pnlBtns.ClientSize.Width - 98, 8);
                btnOk.Location = new Point(pnlBtns.ClientSize.Width - 98 - 8 - 170, 8);
            };
            pnlBtns.Controls.Add(btnOk);
            pnlBtns.Controls.Add(btnCancel);

            // 装配：Dock 顺序（先加的最靠里，反向）
            pad.Controls.Add(pnlInfo);
            pad.Controls.Add(line);
            pad.Controls.Add(pnlFiles);
            pad.Controls.Add(pnlDir);
            pad.Controls.Add(lblHeader);

            Controls.Add(pad);
            Controls.Add(pnlBtns);
        }

        private Control MakeFileRow(int top, string label, Color color, TextBox textBox)
        {
            var row = new Panel { Left = 0, Top = top, Width = 640, Height = 34 };
            var lbl = new Label
            {
                Text = label,
                Left = 0,
                Top = 8,
                Width = 96,
                ForeColor = color,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            textBox.Left = 100;
            textBox.Top = 6;
            textBox.Width = 480;
            var suf = new Label { Text = ".rvt", Left = 586, Top = 8, Width = 36, ForeColor = Color.Gray };
            row.Controls.AddRange(new Control[] { lbl, textBox, suf });
            return row;
        }

        private static string AppendRvt(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "未命名.rvt";
            if (!name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) name += ".rvt";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "_");
            return name;
        }

        private bool ValidateInput()
        {
            string dir = _txtDir.Text?.Trim();
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show("请选择输出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    MessageBox.Show("输出目录无法创建：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            string d = AppendRvt(_txtDuct.Text.Trim());
            string t = AppendRvt(_txtTray.Text.Trim());
            string p = AppendRvt(_txtPipe.Text.Trim());
            if (d == t || t == p || d == p)
            {
                MessageBox.Show("三份文件名不能相同，请修改后重试。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void UpdateSummary()
        {
            string dir = _txtDir.Text ?? "";
            string d = AppendRvt(_txtDuct != null ? _txtDuct.Text : "");
            string t = AppendRvt(_txtTray != null ? _txtTray.Text : "");
            string p = AppendRvt(_txtPipe != null ? _txtPipe.Text : "");

            string checkD = File.Exists(Path.Combine(dir, d)) ? "【已存在，将覆盖】" : "";
            string checkT = File.Exists(Path.Combine(dir, t)) ? "【已存在，将覆盖】" : "";
            string checkP = File.Exists(Path.Combine(dir, p)) ? "【已存在，将覆盖】" : "";

            _lblTip.Text =
                "预览：\n\n" +
                string.Format("  ① {0}{1}\n     → {2}\n\n", d, checkD, Path.Combine(dir, d)) +
                string.Format("  ② {0}{1}\n     → {2}\n\n", t, checkT, Path.Combine(dir, t)) +
                string.Format("  ③ {0}{1}\n     → {2}\n\n", p, checkP, Path.Combine(dir, p)) +
                "说明：不会修改当前打开的原文件。三份副本生成后分别在内部删除不属于该系统的元素后保存。";
        }
    }
}
