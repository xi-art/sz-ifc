using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MyRevitAddin.RoomSeparation
{
    public partial class RoomSeparationDialog : Form
    {
        private readonly Autodesk.Revit.UI.UIApplication _uiApp;
        private readonly Document _doc;

        public ElementId SelectedLevelId { get; private set; } = ElementId.InvalidElementId;
        public double SpacingMM { get; private set; } = 3000;
        public double OffsetMM { get; private set; } = 0;
        public ElementId LineStyleId { get; private set; } = ElementId.InvalidElementId;

        private ComboBox _cmbLevel;
        private NumericUpDown _numSpacing;
        private NumericUpDown _numOffset;
        private ComboBox _cmbLineStyle;

        public RoomSeparationDialog(Autodesk.Revit.UI.UIApplication uiApp, Document doc)
        {
            _uiApp = uiApp;
            _doc = doc;
            InitializeComponent();
            LoadLevels();
            LoadLineStyles();
        }

        private void InitializeComponent()
        {
            this.Text = "自动生成房间分割线";
            this.Size = new Size(480, 320);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Microsoft YaHei UI", 9f);

            TableLayoutPanel tlp = new TableLayoutPanel();
            tlp.Dock = DockStyle.Fill;
            tlp.ColumnCount = 2;
            tlp.RowCount = 5;
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tlp.Padding = new Padding(12);

            Label lblLevel = new Label() { Text = "目标楼层：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            _cmbLevel = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            tlp.Controls.Add(lblLevel, 0, 0);
            tlp.Controls.Add(_cmbLevel, 1, 0);

            Label lblSpacing = new Label() { Text = "网格间距(mm)：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            _numSpacing = new NumericUpDown()
            {
                Minimum = 500,
                Maximum = 50000,
                Value = 3000,
                Increment = 100,
                DecimalPlaces = 0,
                Dock = DockStyle.Fill
            };
            tlp.Controls.Add(lblSpacing, 0, 1);
            tlp.Controls.Add(_numSpacing, 1, 1);

            Label lblOffset = new Label() { Text = "边界偏移(mm)：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            _numOffset = new NumericUpDown()
            {
                Minimum = 0,
                Maximum = 10000,
                Value = 0,
                Increment = 100,
                DecimalPlaces = 0,
                Dock = DockStyle.Fill
            };
            tlp.Controls.Add(lblOffset, 0, 2);
            tlp.Controls.Add(_numOffset, 1, 2);

            Label lblStyle = new Label() { Text = "线样式：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            _cmbLineStyle = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            tlp.Controls.Add(lblStyle, 0, 3);
            tlp.Controls.Add(_cmbLineStyle, 1, 3);

            FlowLayoutPanel pButtons = new FlowLayoutPanel();
            pButtons.Dock = DockStyle.Fill;
            pButtons.FlowDirection = FlowDirection.RightToLeft;
            Button btnCancel = new Button() { Text = "取消", DialogResult = DialogResult.Cancel };
            Button btnOk = new Button() { Text = "生成", DialogResult = DialogResult.OK };
            pButtons.Controls.Add(btnCancel);
            pButtons.Controls.Add(btnOk);
            tlp.Controls.Add(pButtons, 1, 4);

            this.Controls.Add(tlp);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void LoadLevels()
        {
            _cmbLevel.Items.Clear();
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            foreach (Level lvl in levels)
            {
                _cmbLevel.Items.Add(new LevelItem { Id = lvl.Id, Name = lvl.Name });
            }
            if (_cmbLevel.Items.Count > 0)
                _cmbLevel.SelectedIndex = 0;
        }

        private void LoadLineStyles()
        {
            _cmbLineStyle.Items.Clear();
            _cmbLineStyle.Items.Add(new LineStyleItem { Id = ElementId.InvalidElementId, Name = "默认" });
            FilteredElementCollector coll = new FilteredElementCollector(_doc)
                .OfClass(typeof(GraphicsStyle));
            foreach (GraphicsStyle gs in coll.Cast<GraphicsStyle>())
            {
                if (gs.GraphicsStyleCategory == null) continue;
                if (gs.GraphicsStyleCategory.Parent == null) continue;
                if (gs.GraphicsStyleCategory.Parent.Id.IntegerValue != (int)BuiltInCategory.OST_RoomSeparationLines)
                    continue;
                _cmbLineStyle.Items.Add(new LineStyleItem { Id = gs.Id, Name = gs.Name });
            }
            _cmbLineStyle.SelectedIndex = 0;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                if (_cmbLevel.SelectedItem is LevelItem li)
                    SelectedLevelId = li.Id;
                else
                    SelectedLevelId = ElementId.InvalidElementId;

                SpacingMM = (double)_numSpacing.Value;
                OffsetMM = (double)_numOffset.Value;

                if (_cmbLineStyle.SelectedItem is LineStyleItem si)
                    LineStyleId = si.Id;
            }
            base.OnClosing(e);
        }

        private class LevelItem
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }

        private class LineStyleItem
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }
    }
}
