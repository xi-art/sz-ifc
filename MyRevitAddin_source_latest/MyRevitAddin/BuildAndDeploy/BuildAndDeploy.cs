using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BuildAndDeploy
{
    public partial class MainForm : Form
    {
        private readonly string _projectRoot = @"F:\vs\code\MyRevitAddin";
        private readonly string _msbuildPath = @"F:\vs\p\MSBuild\Current\Bin\MSBuild.exe";
        private readonly string _projFile = @"F:\vs\code\MyRevitAddin\MyRevitAddin_2020.csproj";
        private readonly string _srcDll = @"F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.dll";
        private readonly string _srcPdb = @"F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.pdb";
        private readonly string _srcAddin = @"F:\vs\code\MyRevitAddin\deploy\2020\MyRevitAddin.addin";
        private readonly string _dstDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Autodesk\Revit\Addins\2020";

        private BackgroundWorker _worker;
        private TextBox _txtLog;
        private Label _lblStatus;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            Text = "Revit 插件一键编译部署";
            Size = new Size(600, 450);
            MinimumSize = new Size(500, 380);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            Panel pnlTop = new Panel();
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Height = 80;
            pnlTop.BackColor = Color.FromArgb(235, 240, 248);

            Label lblTitle = new Label();
            lblTitle.Text = "Revit 插件一键编译部署（2020版）";
            lblTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(40, 90, 160);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(12, 10);

            Button btnBuild = new Button();
            btnBuild.Text = "编译项目";
            btnBuild.Location = new Point(12, 40);
            btnBuild.Size = new Size(120, 32);
            btnBuild.BackColor = Color.FromArgb(40, 90, 160);
            btnBuild.ForeColor = Color.White;
            btnBuild.FlatStyle = FlatStyle.Flat;
            btnBuild.Font = new Font("Microsoft YaHei UI", 10F);
            btnBuild.Click += btnBuild_Click;

            Button btnDeploy = new Button();
            btnDeploy.Text = "部署插件";
            btnDeploy.Location = new Point(140, 40);
            btnDeploy.Size = new Size(120, 32);
            btnDeploy.BackColor = Color.FromArgb(60, 140, 70);
            btnDeploy.ForeColor = Color.White;
            btnDeploy.FlatStyle = FlatStyle.Flat;
            btnDeploy.Font = new Font("Microsoft YaHei UI", 10F);
            btnDeploy.Click += btnDeploy_Click;

            Button btnBuildDeploy = new Button();
            btnBuildDeploy.Text = "编译并部署";
            btnBuildDeploy.Location = new Point(268, 40);
            btnBuildDeploy.Size = new Size(140, 32);
            btnBuildDeploy.BackColor = Color.FromArgb(160, 90, 40);
            btnBuildDeploy.ForeColor = Color.White;
            btnBuildDeploy.FlatStyle = FlatStyle.Flat;
            btnBuildDeploy.Font = new Font("Microsoft YaHei UI", 10F);
            btnBuildDeploy.Click += btnBuildDeploy_Click;

            pnlTop.Controls.Add(lblTitle);
            pnlTop.Controls.Add(btnBuild);
            pnlTop.Controls.Add(btnDeploy);
            pnlTop.Controls.Add(btnBuildDeploy);

            Panel pnlMid = new Panel();
            pnlMid.Dock = DockStyle.Fill;
            pnlMid.Padding = new Padding(4);

            Label lblLog = new Label();
            lblLog.Text = "执行日志：";
            lblLog.AutoSize = true;
            lblLog.Location = new Point(4, 2);

            _txtLog = new TextBox();
            _txtLog.Location = new Point(4, 22);
            _txtLog.Size = new Size(576, 300);
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.Font = new Font("Consolas", 9F);
            _txtLog.BackColor = Color.White;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BorderStyle = BorderStyle.FixedSingle;

            pnlMid.Controls.Add(lblLog);
            pnlMid.Controls.Add(_txtLog);

            Panel pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 36;
            pnlBottom.BackColor = Color.FromArgb(235, 240, 248);

            _lblStatus = new Label();
            _lblStatus.Text = "就绪";
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(12, 8);
            _lblStatus.ForeColor = Color.DarkSlateGray;

            Button btnClear = new Button();
            btnClear.Text = "清空日志";
            btnClear.Size = new Size(80, 24);
            btnClear.Location = new Point(500, 5);
            btnClear.FlatStyle = FlatStyle.Flat;
            btnClear.Click += btnClear_Click;

            pnlBottom.Controls.Add(_lblStatus);
            pnlBottom.Controls.Add(btnClear);

            Controls.Add(pnlMid);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);

            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += worker_DoWork;
            _worker.ProgressChanged += worker_ProgressChanged;
            _worker.RunWorkerCompleted += worker_RunWorkerCompleted;

            btnBuild.Enabled = true;
            btnDeploy.Enabled = true;
            btnBuildDeploy.Enabled = true;

            AppendLog("工具已启动");
            AppendLog("项目目录: " + _projectRoot);
            AppendLog("MSBuild: " + _msbuildPath);
            AppendLog("目标目录: " + _dstDir);
            AppendLog("");

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        void btnClear_Click(object sender, EventArgs e)
        {
            _txtLog.Clear();
            AppendLog("日志已清空");
        }

        private void btnBuild_Click(object sender, EventArgs e)
        {
            StartWorker("build");
        }

        private void btnDeploy_Click(object sender, EventArgs e)
        {
            StartWorker("deploy");
        }

        private void btnBuildDeploy_Click(object sender, EventArgs e)
        {
            StartWorker("build_deploy");
        }

        private void StartWorker(string action)
        {
            if (_worker.IsBusy) return;
            _worker.RunWorkerAsync(action);
            foreach (Control c in Controls[1].Controls)
            {
                if (c is Button) c.Enabled = false;
            }
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            string action = e.Argument as string;

            if (action == "build" || action == "build_deploy")
            {
                BuildProject();
            }

            if (action == "deploy" || action == "build_deploy")
            {
                DeployPlugin();
            }
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            string msg = e.UserState as string;
            if (!string.IsNullOrEmpty(msg))
            {
                AppendLog(msg);
            }
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            foreach (Control c in Controls[1].Controls)
            {
                if (c is Button) c.Enabled = true;
            }
            if (e.Error != null)
            {
                _lblStatus.Text = "错误: " + e.Error.Message;
            }
            else
            {
                _lblStatus.Text = "就绪";
            }
        }

        private void AppendLog(string message)
        {
            _txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            _txtLog.ScrollToCaret();
        }

        private void BuildProject()
        {
            _worker.ReportProgress(0, "=== 开始编译项目 ===");

            if (!File.Exists(_msbuildPath))
            {
                _worker.ReportProgress(0, "错误: MSBuild 不存在 - " + _msbuildPath);
                return;
            }

            if (!File.Exists(_projFile))
            {
                _worker.ReportProgress(0, "错误: 项目文件不存在 - " + _projFile);
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = _msbuildPath;
            psi.Arguments = _projFile + " /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal /nologo";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = _projectRoot;

            using (Process proc = Process.Start(psi))
            {
                proc.OutputDataReceived += process_OutputDataReceived;
                proc.ErrorDataReceived += process_ErrorDataReceived;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                sw.Stop();

                if (proc.ExitCode == 0)
                {
                    if (File.Exists(_srcDll))
                    {
                        FileInfo info = new FileInfo(_srcDll);
                        _worker.ReportProgress(0, string.Format("编译成功！耗时 {0:F1}s", sw.Elapsed.TotalSeconds));
                        _worker.ReportProgress(0, string.Format("DLL: {0} | {1:N0} bytes", info.LastWriteTime.ToString("HH:mm:ss"), info.Length));
                    }
                    else
                    {
                        _worker.ReportProgress(0, "编译成功但 DLL 未找到");
                    }
                }
                else
                {
                    _worker.ReportProgress(0, string.Format("编译失败！退出码: {0}, 耗时 {1:F1}s", proc.ExitCode, sw.Elapsed.TotalSeconds));
                }
            }
        }

        void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _worker.ReportProgress(0, e.Data);
            }
        }

        void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _worker.ReportProgress(0, "[ERR] " + e.Data);
            }
        }

        private void DeployPlugin()
        {
            _worker.ReportProgress(0, "=== 开始部署插件 ===");

            if (!File.Exists(_srcDll))
            {
                _worker.ReportProgress(0, "错误: 源 DLL 不存在，请先编译项目");
                return;
            }

            _worker.ReportProgress(0, "源文件: " + _srcDll);
            _worker.ReportProgress(0, "目标目录: " + _dstDir);

            if (!Directory.Exists(_dstDir))
            {
                _worker.ReportProgress(0, "目标目录不存在，创建中...");
                Directory.CreateDirectory(_dstDir);
            }

            _worker.ReportProgress(0, "检查 Revit 是否运行...");
            if (IsProcessRunning("Revit"))
            {
                _worker.ReportProgress(0, "错误: 检测到 Revit 正在运行！");
                _worker.ReportProgress(0, "请先完全退出 Revit 再部署");
                return;
            }
            _worker.ReportProgress(0, "Revit 未运行，可以部署");

            try
            {
                _worker.ReportProgress(0, "复制 DLL...");
                File.Copy(_srcDll, Path.Combine(_dstDir, "MyRevitAddin.dll"), true);

                if (File.Exists(_srcPdb))
                {
                    _worker.ReportProgress(0, "复制 PDB...");
                    File.Copy(_srcPdb, Path.Combine(_dstDir, "MyRevitAddin.pdb"), true);
                }

                if (File.Exists(_srcAddin))
                {
                    _worker.ReportProgress(0, "复制 .addin 清单...");
                    File.Copy(_srcAddin, Path.Combine(_dstDir, "MyRevitAddin.addin"), true);
                }

                _worker.ReportProgress(0, "");
                _worker.ReportProgress(0, "=== 部署完成 ===");

                string[] deployedFiles = Directory.GetFiles(_dstDir, "MyRevitAddin*");
                foreach (string file in deployedFiles)
                {
                    FileInfo info = new FileInfo(file);
                    _worker.ReportProgress(0, string.Format("  {0} | {1:N0} bytes", Path.GetFileName(file), info.Length));
                }

                _worker.ReportProgress(0, "");
                _worker.ReportProgress(0, "启动 Revit 2020 即可使用新版插件");
            }
            catch (Exception ex)
            {
                _worker.ReportProgress(0, "部署失败: " + ex.Message);
            }
        }

        private bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName).Any();
            }
            catch
            {
                return false;
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}