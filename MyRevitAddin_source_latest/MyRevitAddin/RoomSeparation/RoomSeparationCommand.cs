using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace MyRevitAddin.RoomSeparation
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoomSeparationCommand : IExternalCommand
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
                using (RoomSeparationDialog dlg = new RoomSeparationDialog(commandData.Application, doc))
                {
                    IWin32Window owner = GetRevitOwner(commandData.Application);
                    if (dlg.ShowDialog(owner) != DialogResult.OK)
                        return Result.Cancelled;

                    if (dlg.SelectedLevelId == ElementId.InvalidElementId)
                    {
                        TaskDialog.Show("房间分割线", "请选择一个楼层。");
                        return Result.Cancelled;
                    }

                    using (Transaction tx = new Transaction(doc, "自动生成房间分割线"))
                {
                    tx.Start();
                    int created = CreateSeparationLines(doc, dlg.SelectedLevelId, dlg.SpacingMM, dlg.OffsetMM, dlg.LineStyleId);
                    var res = tx.Commit();
                    if (res == TransactionStatus.Committed)
                        TaskDialog.Show("房间分割线", $"已生成 {created} 条房间分割线。");
                    else
                        TaskDialog.Show("房间分割线", "事务未成功提交。");
                }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "生成房间分割线失败：" + ex.Message;
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

        private static int CreateSeparationLines(Document doc, ElementId levelId, double spacingMM, double offsetMM, ElementId lineStyleId)
        {
            Level level = doc.GetElement(levelId) as Level;
            if (level == null) return 0;

            ViewPlan viewPlan = FindViewPlanForLevel(doc, levelId);
            if (viewPlan == null) return 0;

            double spacing = spacingMM / 304.8;
            double offset = offsetMM / 304.8;

            BoundingBoxUV projectBounds = GetProjectBounds(doc);
            if (projectBounds == null) return 0;

            UV min = projectBounds.Min;
            UV max = projectBounds.Max;

            double minX = min.U + offset;
            double minY = min.V + offset;
            double maxX = max.U - offset;
            double maxY = max.V - offset;

            if (maxX <= minX || maxY <= minY) return 0;

            double elevation = level.Elevation;
            XYZ p0 = new XYZ(minX, minY, elevation);
            XYZ p1 = new XYZ(maxX, minY, elevation);
            XYZ p2 = new XYZ(maxX, maxY, elevation);
            XYZ p3 = new XYZ(minX, maxY, elevation);

            CurveArray curves = new CurveArray();

            // 矩形边界
            AddCurve(curves, p0, p1);
            AddCurve(curves, p1, p2);
            AddCurve(curves, p2, p3);
            AddCurve(curves, p3, p0);

            // 水平网格线
            for (double y = minY + spacing; y < maxY - 0.001; y += spacing)
            {
                AddCurve(curves, new XYZ(minX, y, elevation), new XYZ(maxX, y, elevation));
            }

            // 垂直网格线
            for (double x = minX + spacing; x < maxX - 0.001; x += spacing)
            {
                AddCurve(curves, new XYZ(x, minY, elevation), new XYZ(x, maxY, elevation));
            }

            if (curves.Size == 0) return 0;

            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation));
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            ModelCurveArray result = doc.Create.NewRoomBoundaryLines(sketchPlane, curves, viewPlan);
            if (result == null) return 0;

            int count = 0;
            foreach (ModelCurve mc in result)
            {
                if (mc == null) continue;
                count++;
                if (lineStyleId != ElementId.InvalidElementId)
                {
                    try { mc.LineStyle = doc.GetElement(lineStyleId) as GraphicsStyle; }
                    catch { }
                }
            }
            return count;
        }

        private static void AddCurve(CurveArray arr, XYZ a, XYZ b)
        {
            try { arr.Append(Line.CreateBound(a, b)); } catch { }
        }

        private static ViewPlan FindViewPlanForLevel(Document doc, ElementId levelId)
        {
            try
            {
                var coll = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(vp => vp.GenLevel != null && vp.GenLevel.Id.Equals(levelId));
                return coll.FirstOrDefault();
            }
            catch { return null; }
        }

        private static BoundingBoxUV GetProjectBounds(Document doc)
        {
            try
            {
                ProjectLocation loc = doc.ActiveProjectLocation;
                BoundingBoxXYZ bb = loc.get_BoundingBox(null);
                if (bb != null && bb.Max != null && bb.Min != null)
                {
                    return new BoundingBoxUV(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
                }
            }
            catch { }

            // 兜底：根据所有墙、楼板等模型元素范围计算
            BoundingBoxXYZ total = null;
            FilteredElementCollector coll = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall));
            foreach (Element e in coll)
            {
                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                if (total == null)
                {
                    total = bb;
                }
                else
                {
                    XYZ min = new XYZ(
                        Math.Min(total.Min.X, bb.Min.X),
                        Math.Min(total.Min.Y, bb.Min.Y),
                        Math.Min(total.Min.Z, bb.Min.Z));
                    XYZ max = new XYZ(
                        Math.Max(total.Max.X, bb.Max.X),
                        Math.Max(total.Max.Y, bb.Max.Y),
                        Math.Max(total.Max.Z, bb.Max.Z));
                    total = new BoundingBoxXYZ { Min = min, Max = max };
                }
            }

            if (total != null)
            {
                return new BoundingBoxUV(total.Min.X, total.Min.Y, total.Max.X, total.Max.Y);
            }
            return null;
        }
    }
}
