using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace MyRevitAddin.RoomOverlapChecker
{
    internal class OverlapPair
    {
        public ElementId Room1Id { get; set; }
        public ElementId Room2Id { get; set; }
        public string Room1Name { get; set; }
        public string Room2Name { get; set; }
        public string LevelName { get; set; }
        public double OverlapArea { get; set; }  // m²
        public override string ToString() => $"{Room1Name} ↔ {Room2Name}";
    }

    internal class EmptyRoomItem
    {
        public ElementId RoomId { get; set; }
        public string RoomName { get; set; }
        public string LevelName { get; set; }
        public double Area { get; set; }  // m²
    }

    internal class RoomOverlapCheckerDialog : Form
    {
        private readonly Document _doc;
        private readonly UIDocument _uidoc;

        private List<OverlapPair> _overlapPairs = new List<OverlapPair>();
        private BindingList<OverlapPair> _viewPairs;
        private List<EmptyRoomItem> _emptyRooms = new List<EmptyRoomItem>();
        private BindingList<EmptyRoomItem> _viewEmptyRooms;
        private DataGridView _dgv;
        private TabControl _tabControl;
        private Label _lblStat;
        private Button _btnCheck;
        private Button _btnClose;

        public RoomOverlapCheckerDialog(Document doc, UIDocument uidoc)
        {
            _doc = doc;
            _uidoc = uidoc;
            InitUI();
            Load += (s, e) => OnLoad();
        }

        private void InitUI()
        {
            Text = "房间重叠检测 — 完全重叠房间 + 空白房间清理";
            Size = new Size(1050, 650);
            MinimumSize = new Size(850, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;

            // ===== 顶栏 =====
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(235, 240, 248) };

            _btnCheck = new Button
            {
                Text = "开始检测",
                Size = new Size(100, 34),
                Location = new Point(12, 12),
                BackColor = Color.FromArgb(40, 90, 160),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnCheck.Click += BtnCheck_Click;

            var lblHint = new Label
            {
                Text = "提示：点击「选中」按钮可单独选中房间，窗口可保留在界面上",
                AutoSize = true,
                Location = new Point(120, 20),
                ForeColor = Color.DarkSlateBlue
            };

            pnlTop.Controls.AddRange(new Control[] { _btnCheck, lblHint });

            // ===== 选项卡区域 =====
            var pnlMid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };

            _tabControl = new TabControl { Dock = DockStyle.Fill };

            // 标签页1: 完全重叠房间
            var tabOverlap = new TabPage("完全重叠房间");
            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Microsoft YaHei UI", 9F), Padding = new Padding(2) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(235, 240, 248),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };
            _dgv.CellContentClick += Dgv_CellContentClick;
            tabOverlap.Controls.Add(_dgv);
            _tabControl.TabPages.Add(tabOverlap);

            // 标签页2: 空白房间
            var tabEmpty = new TabPage("空白房间（待删除）");
            var dgvEmpty = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Microsoft YaHei UI", 9F), Padding = new Padding(2) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(235, 240, 248),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };
            dgvEmpty.CellContentClick += DgvEmpty_CellContentClick;
            tabEmpty.Controls.Add(dgvEmpty);
            _tabControl.TabPages.Add(tabEmpty);

            pnlMid.Controls.Add(_tabControl);

            // ===== 底栏 =====
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(235, 240, 248) };
            _lblStat = new Label { AutoSize = true, Location = new Point(12, 14), ForeColor = Color.FromArgb(60, 60, 60), Text = "准备就绪，点击「开始检测」查找完全重叠房间和空白房间" };
            _btnClose = new Button { Text = "关闭", Size = new Size(90, 30), FlatStyle = FlatStyle.Flat };
            _btnClose.Click += (s, e) => { Close(); };
            pnlBottom.Controls.Add(_lblStat);
            pnlBottom.Controls.Add(_btnClose);
            pnlBottom.Resize += (s, e) => { _btnClose.Location = new Point(pnlBottom.Width - 100, 8); };

            Controls.Add(pnlMid);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
        }

        // ============================================================
        // 加载
        // ============================================================
        private void OnLoad()
        {
            InitOverlapGrid();
            InitEmptyGrid();
        }

        private void InitOverlapGrid()
        {
            _dgv.Columns.Clear();
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标高", DataPropertyName = "LevelName", Width = 80 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "房间1", DataPropertyName = "Room1Name", Width = 180 });
            _dgv.Columns.Add(new DataGridViewButtonColumn { HeaderText = "选中1", Text = "选中", UseColumnTextForButtonValue = true, Width = 60 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "房间2", DataPropertyName = "Room2Name", Width = 180 });
            _dgv.Columns.Add(new DataGridViewButtonColumn { HeaderText = "选中2", Text = "选中", UseColumnTextForButtonValue = true, Width = 60 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "面积(m²)", DataPropertyName = "OverlapArea", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight, Format = "0.00" } });
        }

        private void InitEmptyGrid()
        {
            var dgvEmpty = (DataGridView)_tabControl.TabPages[1].Controls[0];
            dgvEmpty.Columns.Clear();
            dgvEmpty.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标高", DataPropertyName = "LevelName", Width = 100 });
            dgvEmpty.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "房间名称", DataPropertyName = "RoomName", Width = 200 });
            dgvEmpty.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "面积(m²)", DataPropertyName = "Area", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight, Format = "0.00" } });
            dgvEmpty.Columns.Add(new DataGridViewButtonColumn { HeaderText = "选中", Text = "选中", UseColumnTextForButtonValue = true, Width = 60 });
        }

        // ============================================================
        // 检测重叠
        // ============================================================
        private void BtnCheck_Click(object sender, EventArgs e)
        {
            _btnCheck.Enabled = false;
            _lblStat.Text = "正在检测，请稍候...";
            Application.DoEvents();

            try
            {
                _overlapPairs = DetectFullOverlaps();
                _emptyRooms = DetectEmptyRooms();

                _viewPairs = new BindingList<OverlapPair>(_overlapPairs);
                _viewEmptyRooms = new BindingList<EmptyRoomItem>(_emptyRooms);

                InitOverlapGrid();
                InitEmptyGrid();

                _dgv.DataSource = _viewPairs;
                ((DataGridView)_tabControl.TabPages[1].Controls[0]).DataSource = _viewEmptyRooms;

                _lblStat.Text = $"检测完成：完全重叠房间 {_overlapPairs.Count} 组，空白房间 {_emptyRooms.Count} 个";

                if (_overlapPairs.Count == 0 && _emptyRooms.Count == 0)
                {
                    MessageBox.Show("未发现完全重叠房间和空白房间，项目中的房间布局正常。", "检测结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("检测失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStat.Text = "检测失败";
            }
            finally
            {
                _btnCheck.Enabled = true;
            }
        }

        private List<OverlapPair> DetectFullOverlaps()
        {
            var result = new List<OverlapPair>();
            var visited = new HashSet<long>();

            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<SpatialElement>()
                .ToList();

            if (rooms.Count < 2) return result;

            var roomsByLevel = rooms.GroupBy(r =>
            {
                Level level = _doc.GetElement(r.LevelId) as Level;
                return level?.Name ?? "未指定标高";
            }).ToList();

            foreach (var levelGroup in roomsByLevel)
            {
                var levelRooms = levelGroup.ToList();
                int n = levelRooms.Count;

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        SpatialElement r1 = levelRooms[i];
                        SpatialElement r2 = levelRooms[j];

                        if (RoomsFullyOverlap(r1, r2))
                        {
                            long key1 = ((long)r1.Id.IntegerValue << 32) | (uint)r2.Id.IntegerValue;
                            long key2 = ((long)r2.Id.IntegerValue << 32) | (uint)r1.Id.IntegerValue;
                            if (visited.Contains(key1) || visited.Contains(key2)) continue;
                            visited.Add(key1);

                            result.Add(new OverlapPair
                            {
                                Room1Id = r1.Id,
                                Room2Id = r2.Id,
                                Room1Name = r1.Name ?? $"房间 #{r1.Id.IntegerValue}",
                                Room2Name = r2.Name ?? $"房间 #{r2.Id.IntegerValue}",
                                LevelName = levelGroup.Key,
                                OverlapArea = GetRoomArea(r1)
                            });
                        }
                    }
                }
            }

            return result.OrderBy(r => r.LevelName).ThenByDescending(r => r.OverlapArea).ToList();
        }

        // 检测两个房间是否完全重叠（边界基本重合）
        private bool RoomsFullyOverlap(SpatialElement r1, SpatialElement r2)
        {
            try
            {
                // 先比较面积，如果面积差异超过5%则不可能完全重叠
                double area1 = GetRoomArea(r1);
                double area2 = GetRoomArea(r2);
                if (area1 > 0 && area2 > 0)
                {
                    double diff = Math.Abs(area1 - area2) / Math.Max(area1, area2);
                    if (diff > 0.05) return false;
                }

                // 比较BoundingBox，完全重叠的房间BoundingBox应该基本一致
                BoundingBoxXYZ bb1 = r1.get_BoundingBox(null);
                BoundingBoxXYZ bb2 = r2.get_BoundingBox(null);
                if (bb1 == null || bb2 == null) return false;

                // BoundingBox容差比较（50mm以内差异）
                double tol = 50 / 304.8; // 50mm 转英尺
                if (Math.Abs(bb1.Min.X - bb2.Min.X) > tol ||
                    Math.Abs(bb1.Min.Y - bb2.Min.Y) > tol ||
                    Math.Abs(bb1.Max.X - bb2.Max.X) > tol ||
                    Math.Abs(bb1.Max.Y - bb2.Max.Y) > tol)
                {
                    return false;
                }

                // 检查边界曲线是否基本重合
                var options = new SpatialElementBoundaryOptions();
                var boundary1List = r1.GetBoundarySegments(options).FirstOrDefault();
                var boundary2List = r2.GetBoundarySegments(options).FirstOrDefault();

                if (boundary1List == null || boundary2List == null)
                {
                    return true;
                }

                CurveLoop boundary1 = null;
                CurveLoop boundary2 = null;

                if (boundary1List != null && boundary1List.Count > 0)
                {
                    boundary1 = new CurveLoop();
                    foreach (var seg in boundary1List)
                    {
                        Curve c = seg.GetCurve();
                        if (c != null) boundary1.Append(c);
                    }
                }

                if (boundary2List != null && boundary2List.Count > 0)
                {
                    boundary2 = new CurveLoop();
                    foreach (var seg in boundary2List)
                    {
                        Curve c = seg.GetCurve();
                        if (c != null) boundary2.Append(c);
                    }
                }

                if (boundary1 == null || boundary2 == null)
                {
                    return true;
                }

                // 检查两个房间的边界曲线数量是否相近
                if (Math.Abs(boundary1.Count() - boundary2.Count()) > 2)
                {
                    return false;
                }

                // 检查第一个房间的所有边界点是否都在第二个房间边界附近
                foreach (Curve c1 in boundary1)
                {
                    XYZ p1 = c1.GetEndPoint(0);
                    XYZ p2 = c1.GetEndPoint(1);

                    if (!IsPointNearBoundary(p1, boundary2, tol) ||
                        !IsPointNearBoundary(p2, boundary2, tol))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch { }
            return false;
        }

        // 检查点是否在边界附近
        private bool IsPointNearBoundary(XYZ point, CurveLoop boundary, double tol)
        {
            foreach (Curve c in boundary)
            {
                try
                {
                    XYZ ptOnCurve = c.Project(point).XYZPoint;
                    if (ptOnCurve.DistanceTo(point) < tol)
                        return true;
                }
                catch { }
            }
            return false;
        }

        // 获取房间面积（m²）
        private double GetRoomArea(SpatialElement room)
        {
            try
            {
                Parameter areaParam = room.get_Parameter(BuiltInParameter.ROOM_AREA);
                if (areaParam != null)
                {
                    double areaSqFt = areaParam.AsDouble();
                    return areaSqFt * 0.09290304; // 平方英尺转平方米
                }
            }
            catch { }
            return 0;
        }

        // 检测空白房间
        private List<EmptyRoomItem> DetectEmptyRooms()
        {
            var result = new List<EmptyRoomItem>();

            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<SpatialElement>()
                .ToList();

            foreach (var room in rooms)
            {
                string name = room.Name ?? "";
                name = name.Trim();

                // 判断是否为空白房间：名称为空或默认值
                bool isEmpty = string.IsNullOrEmpty(name) ||
                               name.StartsWith("房间") ||
                               name.StartsWith("Room") ||
                               name.Contains("未命名");

                if (isEmpty)
                {
                    Level level = _doc.GetElement(room.LevelId) as Level;
                    result.Add(new EmptyRoomItem
                    {
                        RoomId = room.Id,
                        RoomName = name.Length > 0 ? name : "(空)",
                        LevelName = level?.Name ?? "未指定标高",
                        Area = GetRoomArea(room)
                    });
                }
            }

            return result.OrderBy(r => r.LevelName).ThenBy(r => r.RoomName).ToList();
        }

        // BoundingBox快速检测
        private bool BoundingBoxIntersect(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
        {
            XYZ min1 = bb1.Min;
            XYZ max1 = bb1.Max;
            XYZ min2 = bb2.Min;
            XYZ max2 = bb2.Max;

            return min1.X <= max2.X && max1.X >= min2.X &&
                   min1.Y <= max2.Y && max1.Y >= min2.Y;
        }

        // 精确检测房间是否重叠（保留用于兼容，实际使用RoomsFullyOverlap）
        private bool RoomsOverlap(SpatialElement r1, SpatialElement r2)
        {
            try
            {
                var options = new SpatialElementBoundaryOptions();
                var boundary1List = r1.GetBoundarySegments(options).FirstOrDefault();
                var boundary2List = r2.GetBoundarySegments(options).FirstOrDefault();

                CurveLoop boundary1 = null;
                CurveLoop boundary2 = null;

                if (boundary1List != null && boundary1List.Count > 0)
                {
                    boundary1 = new CurveLoop();
                    foreach (var seg in boundary1List)
                    {
                        Curve c = seg.GetCurve();
                        if (c != null) boundary1.Append(c);
                    }
                }

                if (boundary2List != null && boundary2List.Count > 0)
                {
                    boundary2 = new CurveLoop();
                    foreach (var seg in boundary2List)
                    {
                        Curve c = seg.GetCurve();
                        if (c != null) boundary2.Append(c);
                    }
                }

                if (boundary1 == null || boundary2 == null)
                {
                    // 回退到BoundingBox检测
                    BoundingBoxXYZ bb1 = r1.get_BoundingBox(null);
                    BoundingBoxXYZ bb2 = r2.get_BoundingBox(null);
                    return bb1 != null && bb2 != null && BoundingBoxIntersect(bb1, bb2);
                }

                // 检查边界框的中心是否在对方内部
                XYZ center1 = GetCentroid(boundary1);
                XYZ center2 = GetCentroid(boundary2);

                if (IsPointInBoundary(center1, boundary2)) return true;
                if (IsPointInBoundary(center2, boundary1)) return true;

                // 检查边界曲线是否相交
                foreach (Curve c1 in boundary1)
                {
                    foreach (Curve c2 in boundary2)
                    {
                        try
                        {
                            IntersectionResultArray results;
                            SetComparisonResult scr = c1.Intersect(c2, out results);
                            if (scr == SetComparisonResult.Overlap) return true;
                            if (scr == SetComparisonResult.Disjoint) continue;
                            if (results != null && results.Size > 0) return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return false;
        }

        // 获取边界中心
        private XYZ GetCentroid(CurveLoop loop)
        {
            double sumX = 0, sumY = 0;
            int count = 0;
            foreach (Curve c in loop)
            {
                try
                {
                    sumX += (c.GetEndPoint(0).X + c.GetEndPoint(1).X) / 2;
                    sumY += (c.GetEndPoint(0).Y + c.GetEndPoint(1).Y) / 2;
                    count++;
                }
                catch { }
            }
            return count > 0 ? new XYZ(sumX / count, sumY / count, 0) : XYZ.Zero;
        }

        // 检查点是否在边界内
        private bool IsPointInBoundary(XYZ point, CurveLoop boundary)
        {
            try
            {
                // 使用射线法：从点向右作水平线，统计与边界交点数
                // 奇数=内部，偶数=外部
                int crossings = 0;
                XYZ rayStart = point;
                XYZ rayEnd = new XYZ(point.X + 10000, point.Y, point.Z);

                foreach (Curve c in boundary)
                {
                    try
                    {
                        IntersectionResultArray results;
                        SetComparisonResult scr = c.Intersect(Line.CreateBound(rayStart, rayEnd), out results);
                        if (scr == SetComparisonResult.Overlap) return true;
                        if (results != null && results.Size > 0) crossings++;
                    }
                    catch { }
                }

                return crossings % 2 == 1;
            }
            catch { }
            return false;
        }

        // 估算重叠面积
        private double EstimateOverlapArea(SpatialElement r1, SpatialElement r2)
        {
            try
            {
                // 简化：取两个BoundingBox交集的面积估算
                BoundingBoxXYZ bb1 = r1.get_BoundingBox(null);
                BoundingBoxXYZ bb2 = r2.get_BoundingBox(null);
                if (bb1 == null || bb2 == null) return 0;

                double minX = Math.Max(bb1.Min.X, bb2.Min.X);
                double maxX = Math.Min(bb1.Max.X, bb2.Max.X);
                double minY = Math.Max(bb1.Min.Y, bb2.Min.Y);
                double maxY = Math.Min(bb1.Max.Y, bb2.Max.Y);

                if (maxX <= minX || maxY <= minY) return 0;

                // 内部单位（平方英尺）转平方米
                double areaSqFt = (maxX - minX) * (maxY - minY);
                return areaSqFt * 0.09290304;
            }
            catch { }
            return 0;
        }

        // ============================================================
        // 选中房间
        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = _dgv.Rows[e.RowIndex].DataBoundItem as OverlapPair;
            if (row == null) return;

            if (e.ColumnIndex == 2)
            {
                SelectRoom(row.Room1Id, row.Room1Name);
            }
            else if (e.ColumnIndex == 4)
            {
                SelectRoom(row.Room2Id, row.Room2Name);
            }
        }

        private void SelectRoom(ElementId roomId, string roomName)
        {
            try
            {
                _uidoc.Selection.SetElementIds(new List<ElementId> { roomId });
                _uidoc.ShowElements(roomId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("选中失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DgvEmpty_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var dgv = sender as DataGridView;
            var row = dgv?.Rows[e.RowIndex].DataBoundItem as EmptyRoomItem;
            if (row == null) return;

            if (e.ColumnIndex == 3)
            {
                SelectRoom(row.RoomId, row.RoomName);
            }
        }
    }
}
