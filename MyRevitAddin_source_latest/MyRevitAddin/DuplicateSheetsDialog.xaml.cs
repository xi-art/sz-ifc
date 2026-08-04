using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MyRevitAddin
{
    public partial class DuplicateSheetsDialog : Window
    {
        private readonly Document _doc;
        private readonly IReadOnlyList<ElementId> _selectedSheetIds;

        public string SheetPrefix => TxtSheetPrefix.Text.Trim();
        public string ViewPrefix => TxtViewPrefix.Text.Trim();
        public int CopyCount
        {
            get
            {
                int.TryParse(TxtCopyCount.Text.Trim(), out int count);
                return count < 1 ? 1 : count;
            }
        }
        public bool UsePrefixForNumbering => CmbNameRule.SelectedIndex == 0;

        /// <summary>
        /// 返回用户配置的替换关系
        /// Key: (源SheetId, 源ViewId), Value: 替换目标ViewId（ElementId.InvalidElementId = 原样复制）
        /// </summary>
        public Dictionary<(ElementId, ElementId), ElementId> ViewReplacements { get; private set; }

        public DuplicateSheetsDialog(Document doc, IReadOnlyList<ElementId> selectedSheetIds)
        {
            _doc = doc;
            _selectedSheetIds = selectedSheetIds;
            InitializeComponent();
            BuildDataGrid();
        }

        private void BuildDataGrid()
        {
            var rows = new ObservableCollection<SheetViewRow>();

            // 收集项目中所有可用视图（用于替换下拉）
            var allViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Internal)
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .ToList();

            TxtSelectedInfo.Text = $"已选 {allViews.Count} 张图纸/视图";

            foreach (var sheetId in _selectedSheetIds)
            {
                ViewSheet sheet = _doc.GetElement(sheetId) as ViewSheet;
                if (sheet == null) continue;

                var viewportIds = sheet.GetAllViewports();
                var viewports = viewportIds
                    .Select(vpId => _doc.GetElement(vpId) as Viewport)
                    .Where(vp => vp != null)
                    .ToList();

                if (viewports.Count == 0)
                {
                    // 空图纸也加入一行
                    rows.Add(new SheetViewRow
                    {
                        SheetId = sheetId,
                        SheetNumber = sheet.SheetNumber,
                        SheetName = sheet.Name,
                        ViewName = "(空图纸)",
                        ViewType = "-",
                        IsIncluded = true,
                        AvailableViews = GetViewComboItems(allViews, ElementId.InvalidElementId),
                        ReplacementViewId = ElementId.InvalidElementId
                    });
                }
                else
                {
                    foreach (var vp in viewports)
                    {
                        ElementId viewId = vp.ViewId;
                        View view = _doc.GetElement(viewId) as View;
                        string viewName = view?.Name ?? "(未知)";
                        string viewType = view?.ViewType.ToString() ?? "-";

                        rows.Add(new SheetViewRow
                        {
                            SheetId = sheetId,
                            ViewId = viewId,
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            ViewName = viewName,
                            ViewType = viewType,
                            IsIncluded = true,
                            AvailableViews = GetViewComboItems(allViews, viewId),
                            ReplacementViewId = ElementId.InvalidElementId
                        });
                    }
                }
            }

            DgSheets.ItemsSource = rows;
        }

        private ObservableCollection<ViewComboItem> GetViewComboItems(List<View> allViews, ElementId currentViewId)
        {
            var items = new ObservableCollection<ViewComboItem>
            {
                new ViewComboItem { ViewId = ElementId.InvalidElementId, ViewName = "— 原样复制 —" }
            };
            foreach (var v in allViews)
            {
                items.Add(new ViewComboItem { ViewId = v.Id, ViewName = $"[{v.ViewType}] {v.Name}" });
            }
            return items;
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            DgSheets.SelectAll();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 收集替换关系
            ViewReplacements = new Dictionary<(ElementId, ElementId), ElementId>();

            if (DgSheets.ItemsSource is ObservableCollection<SheetViewRow> rows)
            {
                foreach (var row in rows)
                {
                    if (row.IsIncluded && row.ViewId != ElementId.InvalidElementId)
                    {
                        // 只有选择了替换视图时才记录
                        if (row.ReplacementViewId != ElementId.InvalidElementId &&
                            row.ReplacementViewId != row.ViewId)
                        {
                            ViewReplacements[(row.SheetId, row.ViewId)] = row.ReplacementViewId;
                        }
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// DataGrid 行数据模型
    /// </summary>
    public class SheetViewRow : INotifyPropertyChanged
    {
        public ElementId SheetId { get; set; }
        public ElementId ViewId { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string ViewName { get; set; }
        public string ViewType { get; set; }

        private bool _isIncluded = true;
        public bool IsIncluded
        {
            get => _isIncluded;
            set { _isIncluded = value; OnPropertyChanged(nameof(IsIncluded)); }
        }

        public ObservableCollection<ViewComboItem> AvailableViews { get; set; }

        private ElementId _replacementViewId = ElementId.InvalidElementId;
        public ElementId ReplacementViewId
        {
            get => _replacementViewId;
            set { _replacementViewId = value; OnPropertyChanged(nameof(ReplacementViewId)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ViewComboItem
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
    }
}
