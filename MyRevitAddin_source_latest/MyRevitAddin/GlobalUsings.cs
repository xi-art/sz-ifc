// ============================================================
// 命名空间冲突消歧全局别名
// 放在项目最前面，避免 RevitAPI 与 WinForms/Drawing 的类型冲突
// ============================================================
global using Forms = System.Windows.Forms;
global using Drawing = System.Drawing;
global using Color = System.Drawing.Color;
global using Point = System.Drawing.Point;
global using Size = System.Drawing.Size;
global using Font = System.Drawing.Font;
global using Panel = System.Windows.Forms.Panel;
global using Control = System.Windows.Forms.Control;
global using Form = System.Windows.Forms.Form;
global using View = Autodesk.Revit.DB.View;
global using PointRvt = Autodesk.Revit.DB.Point;
global using XYZ = Autodesk.Revit.DB.XYZ;
global using UV = Autodesk.Revit.DB.UV;
