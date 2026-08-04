using Autodesk.Revit.DB;
using System;

namespace MyRevitAddin
{
    /// <summary>
    /// 通用工具方法
    /// </summary>
    public static class Utils
    {
        private static readonly DisplayUnitType DUnit = DisplayUnitType.DUT_METERS;

        /// <summary>
        /// 把英尺值转成米字符串显示（保留3位小数）
        /// </summary>
        public static string FormatFeet(double feet)
        {
            try
            {
                double meters = UnitUtils.ConvertFromInternalUnits(feet, DUnit);
                return $"{meters:F3} m";
            }
            catch
            {
                return $"{feet:F3} ft";
            }
        }

        /// <summary>
        /// 获取元素的简短描述
        /// </summary>
        public static string GetElementDesc(Element e)
        {
            if (e == null) return "null";
            var cat = e.Category?.Name ?? "未知类别";
            return $"{cat} / {e.Name}";
        }
    }
}
