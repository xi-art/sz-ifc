using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace MyRevitAddin.RoomGapDetector
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomGapDetectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                message = "请先打开一个 Revit 项目。";
                return Result.Failed;
            }

            try
            {
                using (RoomGapDetectorDialog dlg = new RoomGapDetectorDialog(doc))
                {
                    IWin32Window owner = GetRevitOwner(commandData.Application);
                    if (dlg.ShowDialog(owner) != DialogResult.OK)
                        return Result.Cancelled;

                    using (Transaction tx = new Transaction(doc, "房间缺口检测标记"))
                    {
                        tx.Start();
                        int gapCount = DetectAndMarkGaps(doc, dlg.SelectedLevelId, dlg.ToleranceMM, dlg.CreateMarkers);
                        tx.Commit();
                        TaskDialog.Show("房间缺口检测", $"检测完成，发现 {gapCount} 处房间边界缺口。");
                    }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "房间缺口检测失败：" + ex.Message;
                return Result.Failed;
            }
        }

        private static IWin32Window GetRevitOwner(UIApplication uiApp)
        {
            try
            {
                IntPtr h = Process.GetCurrentProcess().MainWindowHandle;
                if (h != IntPtr.Zero) return new Win32Window(h);
            }
            catch { }
            try
            {
                foreach (Process p in Process.GetProcessesByName("Revit"))
                {
                    IntPtr h2 = p.MainWindowHandle;
                    if (h2 != IntPtr.Zero) return new Win32Window(h2);
                }
            }
            catch { }
            return null;
        }

        private class Win32Window : IWin32Window
        {
            private readonly IntPtr _h;
            public Win32Window(IntPtr h) { _h = h; }
            public IntPtr Handle { get { return _h; } }
        }

        private static int DetectAndMarkGaps(Document doc, ElementId levelId, double toleranceMM, bool createMarkers)
        {
            double tolerance = toleranceMM / 304.8;
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0 && r.Location != null)
                .ToList();

            if (levelId != ElementId.InvalidElementId)
                rooms = rooms.Where(r => r.LevelId != null && r.LevelId.Equals(levelId)).ToList();

            int totalGaps = 0;
            ElementId redStyleId = FindRedGraphicsStyle(doc);

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            options.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;

            foreach (Room room in rooms)
            {
                try
                {
                    IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);
                    if (boundaries == null || boundaries.Count == 0) continue;

                    foreach (IList<BoundarySegment> loop in boundaries)
                    {
                        if (loop == null || loop.Count < 2) continue;

                        for (int i = 0; i < loop.Count; i++)
                        {
                            BoundarySegment current = loop[i];
                            BoundarySegment next = loop[(i + 1) % loop.Count];
                            if (current == null || next == null) continue;

                            Curve c1 = current.GetCurve();
                            Curve c2 = next.GetCurve();
                            if (c1 == null || c2 == null) continue;

                            XYZ end1 = c1.GetEndPoint(1);
                            XYZ start2 = c2.GetEndPoint(0);
                            double dist = end1.DistanceTo(start2);

                            if (dist > tolerance)
                            {
                                totalGaps++;
                                if (createMarkers)
                                    CreateGapMarker(doc, end1, dist, redStyleId);
                            }
                        }
                    }
                }
                catch { }
            }

            return totalGaps;
        }

        private static ElementId FindRedGraphicsStyle(Document doc)
        {
            try
            {
                var gs = new FilteredElementCollector(doc)
                    .OfClass(typeof(GraphicsStyle))
                    .Cast<GraphicsStyle>()
                    .FirstOrDefault(g => g.Name.IndexOf("红", StringComparison.OrdinalIgnoreCase) >= 0
                                      || g.Name.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0);
                return gs?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private static void CreateGapMarker(Document doc, XYZ point, double gapSize, ElementId styleId)
        {
            try
            {
                View activeView = doc.ActiveView;
                if (activeView == null || !(activeView is ViewPlan)) return;

                double radius = Math.Max(gapSize * 0.5, 100.0 / 304.8);
                XYZ center = point;
                XYZ right = center + new XYZ(radius, 0, 0);
                XYZ top = center + new XYZ(0, radius, 0);
                XYZ left = center + new XYZ(-radius, 0, 0);
                XYZ bottom = center + new XYZ(0, -radius, 0);

                DetailCurve dc1 = doc.Create.NewDetailCurve(activeView, Line.CreateBound(left, right));
                DetailCurve dc2 = doc.Create.NewDetailCurve(activeView, Line.CreateBound(top, bottom));

                if (styleId != ElementId.InvalidElementId)
                {
                    try
                    {
                        dc1.LineStyle = doc.GetElement(styleId) as GraphicsStyle;
                        dc2.LineStyle = doc.GetElement(styleId) as GraphicsStyle;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
