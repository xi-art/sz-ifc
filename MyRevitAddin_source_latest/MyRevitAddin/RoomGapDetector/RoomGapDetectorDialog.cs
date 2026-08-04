using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MyRevitAddin.RoomGapDetector
{
    public partial class RoomGapDetectorDialog : Form
    {
        private readonly Document _doc;

        public ElementId SelectedLevelId { get; private set; } = ElementId.InvalidElementId;
        public double ToleranceMM { get; private set; } = 50;
        public bool CreateMarkers { get; private set; } = true;

        private ComboBox _cmbLevel;
        private NumericUpDown _numTolerance;
        private CheckBox _chkMarker;

        public RoomGapDetectorDialog(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            LoadLevels();
        }

        private void InitializeComponent()
        {
            this.Text = "房间缺口检测";
            this.Size = new Size(420, 260);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Microsoft YaHei UI", 9f);

            TableLayoutPanel tlp = new TableLayoutPanel();
            tlp.Dock = DockStyle.Fill;
            tlp.ColumnCount = 2;
            tlp.RowCount = 4;
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tlp.Padding = new Padding(12);

            Label lblLevel = new Label() { Text = "目标楼层：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            _cmbLevel = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            tlp.Controls.Add(lblLevel, 0, 0);
            tlp.Controls.Add(_cmbLevel, 1, 0);

            Label lblTol = new Label() { Text = "容差(mm)：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            _numTolerance = new NumericUpDown()
            {
                Minimum = 1,
                Maximum = 500,
                Value = 50,
                Increment = 5,
                DecimalPlaces = 0,
                Dock = DockStyle.Fill
            };
            tlp.Controls.Add(lblTol, 0, 1);
            tlp.Controls.Add(_numTolerance, 1, 1);

            _chkMarker = new CheckBox()
            {
                Text = "在当前平面视图中用红色十字标记缺口位置",
                Checked = true,
                Dock = DockStyle.Fill
            };
            tlp.SetColumnSpan(_chkMarker, 2);
            tlp.Controls.Add(_chkMarker, 0, 2);

            FlowLayoutPanel pButtons = new FlowLayoutPanel();
            pButtons.Dock = DockStyle.Fill;
            pButtons.FlowDirection = FlowDirection.RightToLeft;
            Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel };
            Button btnOk = new Button() { Text = "开始检测", DialogResult = DialogResult.OK };
            pButtons.Controls.Add(btnCancel);
            pButtons.Controls.Add(btnOk);
            tlp.Controls.Add(pButtons, 1, 3);

            this.Controls.Add(tlp);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
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
            foreach (Level lvl in levels)
                _cmbLevel.Items.Add(new LevelItem { Id = lvl.Id, Name = lvl.Name });
            _cmbLevel.SelectedIndex = 0;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                if (_cmbLevel.SelectedItem is LevelItem li)
                    SelectedLevelId = li.Id;
                ToleranceMM = (double)_numTolerance.Value;
                CreateMarkers = _chkMarker.Checked;
            }
            base.OnClosing(e);
        }

        private class LevelItem
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }
    }
}
