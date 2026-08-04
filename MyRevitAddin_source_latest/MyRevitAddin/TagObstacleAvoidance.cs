using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyRevitAddin
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagObstacleAvoidance : IExternalCommand
    {
        // 红瓦洞口标记: 3.0mm仿宋字体
        // 用户反馈: 实际包围盒约4200mm×1000mm (13.8×3.3英尺)
        // 关键: 文字实际渲染范围常超出估算框，需足够padding才能正确检测重叠
        private const double CharWidth = 1.00;           // 单个字符宽度 (~305mm)
        private const double LineHeight = 1.00;          // 单行高度（约305mm）
        private const double PaddingX = 1.00;            // 水平内边距（模型坐标，英尺）
        private const double PaddingY = 1.00;            // 垂直内边距（模型坐标，英尺）

        // 避障参数
        private const double MinGap = 0.05;              // 最小间距 (~15mm)
        private const double MaxSearchRadius = 2.0;      // 最大搜索半径 (~600mm)
        private const double AngleStep = 15;             // 角度步进（更精细）
        private const double RadiusStep = 0.10;          // 半径步进 (~30mm)

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View activeView = doc.ActiveView;

            // 获取视图比例（如 1:100 则 scale = 100）
            double viewScale = activeView.Scale;
            if (viewScale <= 0) viewScale = 1.0;

            // 1. 选择关键标记
            IList<Reference> selectedRefs;
            try
            {
                selectedRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new TagSelectionFilter(),
                    "请框选需要避障的红瓦洞口标记");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (selectedRefs.Count < 2)
            {
                TaskDialog.Show("提示", "请至少选择 2 个关键标记。");
                return Result.Cancelled;
            }

            // 2. 收集所有标记信息
            List<TagInfo> tagInfos = new List<TagInfo>();
            foreach (Reference r in selectedRefs)
            {
                IndependentTag tag = doc.GetElement(r) as IndependentTag;
                if (tag != null)
                {
                    TagInfo info = GetTagInfo(tag, viewScale);
                    if (info != null)
                        tagInfos.Add(info);
                }
            }

            if (tagInfos.Count < 2)
            {
                TaskDialog.Show("提示", "未检测到足够的关键标记。");
                return Result.Cancelled;
            }

            // 调试：显示所有标记的检测信息和重叠情况
            string debugInfo = $"共选择 {tagInfos.Count} 个标记\n\n";
            for (int i = 0; i < Math.Min(tagInfos.Count, 3); i++)
            {
                var t = tagInfos[i];
                debugInfo += $"标记{i + 1}: {t.Text.Substring(0, Math.Min(15, t.Text.Length))}...\n";
                debugInfo += $"  位置: ({t.HeadPosition.X:F1}, {t.HeadPosition.Y:F1})\n";
                debugInfo += $"  包围盒: [{t.TextBox.Min.X:F1},{t.TextBox.Min.Y:F1}] ~ [{t.TextBox.Max.X:F1},{t.TextBox.Max.Y:F1}]\n\n";
            }

            // 检查重叠对 - 增强调试
            int overlapPairs = 0;
            string overlapDetails = "";
            for (int i = 0; i < tagInfos.Count; i++)
            {
                for (int j = i + 1; j < tagInfos.Count; j++)
                {
                    var a = tagInfos[i].TextBox;
                    var b = tagInfos[j].TextBox;
                    bool overlap = BoxesOverlap(a, b);
                    
                    // 计算间隙
                    double gapX = Math.Max(0, Math.Max(b.Min.X - a.Max.X, a.Min.X - b.Max.X));
                    double gapY = Math.Max(0, Math.Max(b.Min.Y - a.Max.Y, a.Min.Y - b.Max.Y));
                    
                    if (overlap) overlapPairs++;
                    
                    overlapDetails += $"\n标记{i+1}vs{j+1}: 重叠={overlap}, X间隙={gapX:F3}, Y间隙={gapY:F3}";
                }
            }
            debugInfo += $"检测到 {overlapPairs} 对重叠标记" + overlapDetails;

            TaskDialog.Show("调试信息", debugInfo);

            int movedCount = 0;

            using (Transaction trans = new Transaction(doc, "红瓦洞口标记避障"))
            {
                trans.Start();

                // 3. 逐个标记进行螺旋式避障
                for (int i = 0; i < tagInfos.Count; i++)
                {
                    TagInfo current = tagInfos[i];
                    List<TagInfo> others = tagInfos.Where((t, idx) => idx != i).ToList();

                    XYZ bestOffset = SpiralSearch(current, others);

                    if (bestOffset != null && (Math.Abs(bestOffset.X) > 0.001 || Math.Abs(bestOffset.Y) > 0.001))
                    {
                        XYZ originalPos = current.Tag.TagHeadPosition;
                        XYZ newPos = new XYZ(
                            originalPos.X + bestOffset.X,
                            originalPos.Y + bestOffset.Y,
                            originalPos.Z);

                        current.Tag.TagHeadPosition = newPos;
                        movedCount++;

                        tagInfos[i] = GetTagInfo(current.Tag, viewScale);
                    }
                }

                trans.Commit();
            }

            TaskDialog.Show("完成",
                $"共处理 {tagInfos.Count} 个红瓦洞口标记\n" +
                $"移动了 {movedCount} 个标记\n" +
                $"避障完成！");

            return Result.Succeeded;
        }

        private XYZ SpiralSearch(TagInfo current, List<TagInfo> others)
        {
            if (!HasOverlap(current, others))
                return new XYZ(0, 0, 0);

            double radius = RadiusStep;

            while (radius <= MaxSearchRadius)
            {
                for (double angle = 0; angle < 360; angle += AngleStep)
                {
                    double rad = angle * Math.PI / 180.0;
                    double offsetX = radius * Math.Cos(rad);
                    double offsetY = radius * Math.Sin(rad);

                    BoundingBoxXYZ testBox = OffsetBox(current.TextBox, offsetX, offsetY);

                    bool overlap = false;
                    foreach (var other in others)
                    {
                        if (BoxesOverlap(testBox, other.TextBox))
                        {
                            overlap = true;
                            break;
                        }
                    }

                    if (!overlap)
                        return new XYZ(offsetX, offsetY, 0);
                }

                radius += RadiusStep;
            }

            return new XYZ(MaxSearchRadius, 0, 0);
        }

        private bool HasOverlap(TagInfo current, List<TagInfo> others)
        {
            foreach (var other in others)
            {
                if (BoxesOverlap(current.TextBox, other.TextBox))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 基于3.0mm仿宋字体精确估算文字包围盒
        /// viewScale: 视图比例（如1:100则传入100）
        /// </summary>
        private TagInfo GetTagInfo(IndependentTag tag, double viewScale = 1.0)
        {
            try
            {
                string text = tag.TagText ?? "";
                XYZ headPos = tag.TagHeadPosition;

                if (string.IsNullOrEmpty(text)) return null;

                // 分割多行
                string[] lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) lines = new[] { text };

                // 计算最大行宽（字符数）
                int maxChars = lines.Max(l => l.Length);
                int lineCount = lines.Length;

                // 基于实际数据校准的文字尺寸（模型坐标，英尺）
                // 考虑视图比例：图纸上看到的尺寸 = 模型尺寸 × 视图比例
                double scaleFactor = viewScale > 0 ? viewScale : 1.0;
                double charWidth = CharWidth * scaleFactor;        // 单个字符宽度（按视图比例缩放）
                double lineHeight = LineHeight * scaleFactor;      // 单行高度（按视图比例缩放）
                double lineSpacing = lineHeight * 0.2;             // 行间距

                // 计算文字包围盒尺寸
                double textWidth = maxChars * charWidth;
                double textHeight = lineCount * lineHeight + (lineCount - 1) * lineSpacing;

                // 添加内边距 - 进一步增大padding以覆盖文字实际渲染范围
                double paddingX = PaddingX * scaleFactor;   // 水平padding（按视图比例缩放）
                double paddingY = PaddingY * scaleFactor;   // 垂直padding（按视图比例缩放）

                double boxWidth = textWidth + paddingX * 2;
                double boxHeight = textHeight + paddingY * 2;

                // 构建包围盒（以头部位置为中心参考点，文字在其上方和右侧）
                // 红瓦洞口标记：头部位置通常在文字左下角，文字向上和向右延伸
                BoundingBoxXYZ textBox = new BoundingBoxXYZ();
                textBox.Min = new XYZ(headPos.X, headPos.Y, headPos.Z);
                textBox.Max = new XYZ(headPos.X + boxWidth, headPos.Y + boxHeight, headPos.Z);

                return new TagInfo
                {
                    Tag = tag,
                    TextBox = textBox,
                    HeadPosition = headPos,
                    Text = text,
                    LineCount = lineCount,
                    MaxLineLength = maxChars,
                    ViewScale = viewScale
                };
            }
            catch
            {
                return null;
            }
        }

        private BoundingBoxXYZ OffsetBox(BoundingBoxXYZ box, double offsetX, double offsetY)
        {
            BoundingBoxXYZ result = new BoundingBoxXYZ();
            result.Min = new XYZ(box.Min.X + offsetX, box.Min.Y + offsetY, box.Min.Z);
            result.Max = new XYZ(box.Max.X + offsetX, box.Max.Y + offsetY, box.Max.Z);
            return result;
        }

        private bool BoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            // 检查两个包围盒是否在X或Y轴上分离（考虑最小间距MinGap）
            // 如果 a 在 b 的左边：a.Max.X + MinGap < b.Min.X
            // 如果 a 在 b 的右边：b.Max.X + MinGap < a.Min.X
            bool separatedX = (a.Max.X + MinGap) < b.Min.X || (b.Max.X + MinGap) < a.Min.X;
            bool separatedY = (a.Max.Y + MinGap) < b.Min.Y || (b.Max.Y + MinGap) < a.Min.Y;
            return !separatedX && !separatedY;
        }
    }

    public class TagSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is IndependentTag;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    public class TagInfo
    {
        public IndependentTag Tag { get; set; }
        public BoundingBoxXYZ TextBox { get; set; }
        public XYZ HeadPosition { get; set; }
        public string Text { get; set; }
        public int LineCount { get; set; }
        public int MaxLineLength { get; set; }
        public double ViewScale { get; set; }
    }
}
