using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using WinUI.TableView.Extensions;
using WinUI.TableView.Helpers;

namespace WinUI.TableView;

/// <summary>
/// Represents a row in a TableView.
/// </summary>

#if WINDOWS
[WinRT.GeneratedBindableCustomProperty]
#endif
public partial class TableViewRow : ListViewItem
{
    private const string Selection_Background = "SelectionBackground";
    private const double Selection_IndicatorHeight = 16d;
    private const string Check_Mark = "\uE73E";
    private Thickness _focusVisualMargin = new(1);
    private readonly Thickness _selectionBackgroundMargin = new(4, 2, 4, 2);
    private readonly Thickness _selectionIndicatorMargin = new(4, 0, 0, 0);
    private ListViewItemPresenter? _itemPresenter;
    private Border? _selectionBackground;
    private bool _ensureCells = true;
    private Brush? _cellPresenterBackground;
    private Brush? _cellPresenterForeground;

    /// <summary>
    /// Initializes a new instance of the TableViewRow class.
    /// </summary>
    public TableViewRow()
    {
        DefaultStyleKey = typeof(TableViewRow);

        SizeChanged += OnSizeChanged;
        Loaded += TableViewRow_Loaded;
#if WINDOWS
        ContextRequested += OnContextRequested;
        RegisterPropertyChangedCallback(IsSelectedProperty, delegate { OnIsSelectedChanged(); });
#endif
        RegisterPropertyChangedCallback(ForegroundProperty, delegate { OnForegroundChanged(); });
        RegisterPropertyChangedCallback(BackgroundProperty, delegate { OnBackgroundChanged(); });
    }

#if !WINDOWS
    /// <inheritdoc/>
    protected override void OnRightTapped(RightTappedRoutedEventArgs e)
    {
        base.OnRightTapped(e);

        var position = e.GetPosition(this);
#else
    /// <summary>
    /// Handles the ContextRequested event.
    /// </summary>
    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!e.TryGetPosition(sender, out var position)) return;
#endif

        // Select the row before showing the Context Menu
        if (TableView is not null && TableView.ForceRowOrCellSelectionOnContextRequested && !IsSelected)
        {
            TableView.MakeSelection(new TableViewCellSlot(Index, -1), false);
        }

        e.Handled = TableView?.ShowRowContext(this, position) is true;
    }

#if WINDOWS
    /// <summary>
    /// Handles the IsSelected property changed.
    /// </summary>
    private void OnIsSelectedChanged()
    {
        EnsureLayout();
        RowPresenter?.SetRowDetailsVisibility();
    }
#endif

    /// <summary>
    /// Handles the Foreground property changed.
    /// </summary>
    private void OnForegroundChanged()
    {
        _cellPresenterForeground = Foreground;
        EnsureAlternateColors();
    }

    /// <summary>
    /// Handles the Background property changed.
    /// </summary>
    private void OnBackgroundChanged()
    {
        _cellPresenterBackground = Background;
        EnsureAlternateColors();
    }

    /// <summary>
    /// Handles the Loaded event.
    /// </summary>
    private void TableViewRow_Loaded(object sender, RoutedEventArgs e)
    {
        _focusVisualMargin = FocusVisualMargin;

        RowPresenter?.EnsureGridLines();
        EnsureLayout();
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _cellPresenterBackground = Background;
        _cellPresenterForeground = Foreground;
        _itemPresenter = GetTemplateChild("Root") as ListViewItemPresenter;
#if !WINDOWS
        RowPresenter = GetTemplateChild("RowPresenter") as TableViewRowPresenter;
        _selectionBackground = GetTemplateChild("SelectionBackground") as Border;
#endif
    }

    /// <inheritdoc/>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (_ensureCells)
        {
            EnsureCells();
        }
        else
        {
            foreach (var cell in Cells)
            {
                // Defensively resync width on reuse — a recycled container can otherwise keep a
                // stale Width if it missed a Column.ActualWidth change while off-screen (e.g. an
                // auto-width recalculation triggered by a sort), leaving cells misaligned with headers.
                if (cell.Column is not null)
                {
                    cell.Width = cell.Column.ActualWidth;
                }

                // Datgel DH-2718: likewise the row heights — a recycled container is out of TableView's row list while
                // off-screen, so a RowHeight change pushed meanwhile did not reach its cells.
                if (TableView is not null)
                {
                    cell.ApplyRowHeights(TableView);
                }

                cell.RefreshElement();
            }
        }

        RowPresenter?.InvalidateMeasure(); // The cells presenter does not measure every time.
        TableView?.EnsureAlternateRowColors();
    }

    /// <inheritdoc/>
    protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        var eventArgs = new TableViewRowDoubleTappedEventArgs(Index, this, Content);
        TableView?.OnRowDoubleTapped(eventArgs);
        e.Handled = eventArgs.Handled;

        base.OnDoubleTapped(e);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        finalSize = base.ArrangeOverride(finalSize);

        var cornerRadius = _itemPresenter?.CornerRadius ?? new();
        var left = Math.Max(cornerRadius.TopLeft, cornerRadius.BottomLeft);

        _itemPresenter?.Arrange(new Rect(-left, 0, _itemPresenter.ActualWidth + left, _itemPresenter.ActualHeight));

        // Position feeds drag-selection hit testing only; a column-width change never moves a row
        // relative to the drag canvas, so recomputing it (a visual-tree transform walk) on every
        // row on every frame of a resize drag is pure waste.
        if (TableView?.IsColumnResizing != true)
        {
            UpdatePosition();
        }

        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);

        e.Handled = TableView?.OnAnyPointerPressed(this, e) ?? false;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);

        TableView?.EndDragSelection();
    }

    /// <summary>
    /// Updates the position of the row relative to the TableView.
    /// </summary>
    internal void UpdatePosition()
    {
        if (TableView is null) return;

        try
        {
            Position = TransformToVisual(TableView.DragRectangleCanvas).TransformPoint(default);
        }
        catch (Exception ex)
        {
            TableViewTrace.Write($"UpdatePosition failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets or sets the position of the row relative to the TableView.
    /// </summary>
    internal Point Position { get; set; }

    /// <summary>
    /// Ensures cells are created for the row.
    /// </summary>
    internal void EnsureCells()
    {
        if (TableView is null)
        {
            return;
        }

        if (RowPresenter is not null && _ensureCells)
        {
            RowPresenter.ClearCells();

            AddCells(TableView.Columns.VisibleColumns);
            _ensureCells = false;
        }
    }

    /// <summary>
    /// Handles the SizeChanged event.
    /// </summary>
    private async void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (TableView?.CurrentCellSlot?.Row == Index)
        {
            _ = await TableView.ScrollCellIntoView(TableView.CurrentCellSlot.Value);
        }

        // Update the row positions in the TableView after the size change.
        TableView?.UpdateRowPositions();
    }

    /// <summary>
    /// Handles the collection changed event for the columns.
    /// </summary>
    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.OfType<TableViewColumn>() is IEnumerable<TableViewColumn> newItems)
        {
            AddCells(newItems.Where(x => x.Visibility == Visibility.Visible));
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems?.OfType<TableViewColumn>() is IEnumerable<TableViewColumn> oldItems)
        {
            RemoveCells(oldItems);
        }
        else if (e.Action == NotifyCollectionChangedAction.Move && e.NewItems?.Count > 0)
        {
            RowPresenter?.MoveCells(e.NewItems.OfType<TableViewColumn>().First(), e.NewStartingIndex);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset && RowPresenter is not null)
        {
            RowPresenter.ClearCells();
        }

        if (TableView?.VirtualizesColumns is true)
        {
            RowPresenter?.UpdateColumnGaps(); // Datgel DH-2718
            RealizeColumnsInView();
        }
    }

    /// <summary>
    /// Handles the property changed event for a column.
    /// </summary>
    private void OnColumnPropertyChanged(object? sender, TableViewColumnPropertyChangedEventArgs e)
    {
        OnColumnPropertyChangedCore(e);

        // Datgel DH-2718: a change to which columns are shown, where, or how wide moves the columns a virtualising row
        // has no cell for, and may bring new ones into view.
        if (TableView?.VirtualizesColumns is true
            && e.PropertyName is nameof(TableViewColumn.Visibility) or nameof(TableViewColumn.Order)
                or nameof(TableViewColumn.IsFrozen) or nameof(TableViewColumn.ActualWidth))
        {
            RowPresenter?.UpdateColumnGaps();
            RealizeColumnsInView();
        }
    }

    private void OnColumnPropertyChangedCore(TableViewColumnPropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TableViewColumn.Visibility))
        {
            if (e.Column.Visibility == Visibility.Visible)
            {
                AddCells([e.Column]);
            }
            else
            {
                RemoveCells([e.Column]);
            }
        }
        else if ((e.PropertyName is nameof(TableViewColumn.Order) ||
            e.PropertyName is nameof(TableViewColumn.IsFrozen)) &&
            e.Column.Visibility is Visibility.Visible)
        {
            RemoveCells([e.Column]);
            AddCells([e.Column]);
        }
        else if (e.PropertyName is nameof(TableViewColumn.ActualWidth))
        {
            if (Cells.FirstOrDefault(x => x.Column == e.Column) is { } cell)
            {
                cell.Width = e.Column.ActualWidth;
            }
        }
        else if (e.PropertyName is nameof(TableViewColumn.IsReadOnly))
        {
            UpdateCellsState();
        }
        else if (e.PropertyName is nameof(TableViewColumn.CellStyle))
        {
            EnsureCellsStyle(e.Column);
        }
        else if (e.PropertyName is nameof(TableViewBoundColumn.ElementStyle))
        {
            EnsureElementStyle(e.Column);
        }
        else if (e.PropertyName is nameof(TableViewBoundColumn.EditingElementStyle))
        {
            EnsureEditingElementStyle(e.Column);
        }
    }

    /// <summary>
    /// Removes cells for the specified columns.
    /// </summary>
    private void RemoveCells(IEnumerable<TableViewColumn> columns)
    {
        if (RowPresenter is not null)
        {
            foreach (var column in columns)
            {
                var cell = RowPresenter.Cells.FirstOrDefault(x => x.Column == column);
                if (cell is not null)
                {
                    RowPresenter.RemoveCell(cell);
                }
            }
        }
    }

    /// <summary>
    /// Adds cells for the specified columns.
    /// </summary>
    private void AddCells(IEnumerable<TableViewColumn> columns)
    {
        if (RowPresenter is not null && TableView is not null)
        {
            // Datgel DH-2695: VisibleColumns materialises a fresh sorted list on every read, and adding a row read it once
            // per cell here and twice more per cell in InsertCell - O(n^2 log n) per row, ~13 % of a 177-column open.
            var visibleColumns = TableView.Columns.VisibleColumns;
            var frozenColumns = visibleColumns.Where(x => x.IsFrozen).ToList();
            var scrollableColumns = visibleColumns.Where(x => !x.IsFrozen).ToList();

            // Datgel DH-2718: a virtualising row creates cells only for the columns it must realise.
            var virtualizes = TableView.VirtualizesColumns;
            var starts = virtualizes ? ColumnStarts(scrollableColumns) : null;
            var band = virtualizes ? TableView.ColumnRealizationBand() : default;

            foreach (var column in columns)
            {
                if (virtualizes && !MustRealize(column, starts!, band))
                    continue;

                CreateCell(column, visibleColumns, frozenColumns, scrollableColumns);
            }

            if (virtualizes)
                RowPresenter.UpdateColumnGaps();
        }
    }

    /// <summary>Creates the cell for <paramref name="column"/> and inserts it into the row presenter.</summary>
    private TableViewCell CreateCell(TableViewColumn column, IList<TableViewColumn> visibleColumns,
        IList<TableViewColumn> frozenColumns, IList<TableViewColumn> scrollableColumns)
    {
        var cell = new TableViewCell
        {
            Row = this,
            Column = column,
            TableView = TableView,
            Index = visibleColumns.IndexOf(column),
            Width = column.ActualWidth
        };

        // Datgel DH-2718: set once and pushed by TableView's RowHeight/RowMinHeight/RowMaxHeight callbacks, not
        // three RelativeSource bindings per cell (531 per 177-column row, ~16 % of a wide table's open).
        cell.ApplyRowHeights(TableView!);

        RowPresenter!.InsertCell(cell, frozenColumns, scrollableColumns);
        return cell;
    }

    /// <summary>
    /// The width a column takes in a row's layout (Datgel DH-2718): its measured width, else its absolute width, else
    /// NaN when it is not known yet (auto or star before the first layout).
    /// </summary>
    internal static double LayoutWidth(TableViewColumn column) =>
        column.ActualWidth > 0 ? column.ActualWidth
        : column.Width.IsAbsolute ? column.Width.Value
        : double.NaN;

    /// <summary>Where each scrollable column starts in the scrollable cells panel (Datgel DH-2718).</summary>
    private static Dictionary<TableViewColumn, double> ColumnStarts(IList<TableViewColumn> scrollableColumns)
    {
        var starts = new Dictionary<TableViewColumn, double>(scrollableColumns.Count);
        var x = 0d;
        foreach (var column in scrollableColumns)
        {
            starts[column] = x;
            var width = LayoutWidth(column);
            x += double.IsNaN(width) ? 0 : width;
        }

        return starts;
    }

    /// <summary>
    /// Whether a virtualising row must have a cell for <paramref name="column"/> (Datgel DH-2718): a frozen, auto-width,
    /// always-realised or not-yet-measured column always; the current cell's column; and any column within the band.
    /// </summary>
    private bool MustRealize(TableViewColumn column, Dictionary<TableViewColumn, double> starts, (double Left, double Width) band)
    {
        if (column.IsFrozen || column.AlwaysRealizeElement || column.Width.IsAuto || !starts.TryGetValue(column, out var start))
            return true;

        var width = LayoutWidth(column);
        if (double.IsNaN(width))
            return true;

        if (TableView?.CurrentCellSlot is { } current && current.Row == Index
            && current.Column == TableView.Columns.VisibleColumns.IndexOf(column))
            return true;

        return HorizontalCulling.IsInView(start, width, band.Left, band.Width, margin: 0);
    }

    /// <summary>
    /// Creates the cells a virtualising row is missing for the columns now within the realisation band (Datgel DH-2718).
    /// Called when the table scrolls sideways or changes width, and when columns change. A realised cell is kept when it
    /// scrolls away again. Returns how many cells were created.
    /// </summary>
    internal int RealizeColumnsInView()
    {
        if (TableView is not { VirtualizesColumns: true } tableView || RowPresenter is null || _ensureCells)
            return 0;

        var visibleColumns = tableView.Columns.VisibleColumns;
        var frozenColumns = visibleColumns.Where(x => x.IsFrozen).ToList();
        var scrollableColumns = visibleColumns.Where(x => !x.IsFrozen).ToList();
        var starts = ColumnStarts(scrollableColumns);
        var band = tableView.ColumnRealizationBand();
        var realised = new HashSet<TableViewColumn>(RowPresenter.Cells.Select(c => c.Column).OfType<TableViewColumn>());

        var created = new List<TableViewCell>();
        foreach (var column in visibleColumns)
        {
            if (!realised.Contains(column) && MustRealize(column, starts, band))
                created.Add(CreateCell(column, visibleColumns, frozenColumns, scrollableColumns));
        }

        if (created.Count > 0)
        {
            RowPresenter.UpdateColumnGaps();
            foreach (var cell in created)
                ApplyLateCellState(cell);
        }

        return created.Count;
    }

    /// <summary>
    /// The cell for the visible column at <paramref name="columnIndex"/>: by position when every column has a cell; by
    /// column, created on demand, when the row virtualises its columns (Datgel DH-2718). Null when there is none.
    /// </summary>
    internal TableViewCell? GetCell(int columnIndex)
    {
        if (TableView is not { VirtualizesColumns: true } tableView)
        {
            var cells = Cells;
            return columnIndex >= 0 && columnIndex < cells.Count ? cells[columnIndex] : null;
        }

        var visibleColumns = tableView.Columns.VisibleColumns;
        return columnIndex >= 0 && columnIndex < visibleColumns.Count ? RealizeCell(visibleColumns[columnIndex]) : null;
    }

    /// <summary>
    /// Gets this row's cell for <paramref name="column"/>, creating it if the row virtualises its columns and has none
    /// yet (Datgel DH-2718). Null when the column is not visible or the row has no presenter.
    /// </summary>
    public TableViewCell? RealizeCell(TableViewColumn column)
    {
        if (RowPresenter is null || TableView is null)
            return null;

        if (Cells.FirstOrDefault(c => c.Column == column) is { } existing)
            return existing;

        if (!TableView.VirtualizesColumns || column.Visibility is not Visibility.Visible || _ensureCells)
            return null;

        var visibleColumns = TableView.Columns.VisibleColumns;
        var cell = CreateCell(column, visibleColumns,
            visibleColumns.Where(x => x.IsFrozen).ToList(), visibleColumns.Where(x => !x.IsFrozen).ToList());
        RowPresenter.UpdateColumnGaps();
        ApplyLateCellState(cell);
        return cell;
    }

    /// <summary>
    /// Gives a cell created after its row was prepared the state a cell created with the row gets from the table's
    /// pending-state pass: its selection and, if it is the current cell, its current state (Datgel DH-2718).
    /// </summary>
    private static void ApplyLateCellState(TableViewCell cell)
    {
        cell.ApplySelectionState();
        if (cell.IsCurrent)
            cell.ApplyCurrentCellState(skipFocus: true);
    }

    /// <summary>
    /// Throws away and recreates this row's cells, for a change of <see cref="TableView.VirtualizesColumns"/> (Datgel DH-2718).
    /// </summary>
    internal void RebuildCells()
    {
        _ensureCells = true;
        RowPresenter?.ResetColumnGaps();
        EnsureCells();
    }

    /// <summary>
    /// Handles the TableView changing event.
    /// </summary>
    private void OnTableViewChanging()
    {
        if (TableView is not null)
        {
            TableView.IsReadOnlyChanged -= OnTableViewIsReadOnlyChanged;

            if (TableView.Columns is not null)
            {
                TableView.Columns.CollectionChanged -= OnColumnsCollectionChanged;
                TableView.Columns.ColumnPropertyChanged -= OnColumnPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Handles the TableView changed event.
    /// </summary>
    private void OnTableViewChanged()
    {
        if (TableView is not null)
        {
            TableView.IsReadOnlyChanged += OnTableViewIsReadOnlyChanged;

            if (TableView.Columns is not null)
            {
                TableView.Columns.CollectionChanged += OnColumnsCollectionChanged;
                TableView.Columns.ColumnPropertyChanged += OnColumnPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Handles the IsReadOnly property changed event for the TableView.
    /// </summary>
    private void OnTableViewIsReadOnlyChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateCellsState();
    }

    /// <summary>
    /// Updates the state of the cells.
    /// </summary>
    private void UpdateCellsState()
    {
        foreach (var cell in Cells)
        {
            cell.UpdateElementState();
        }
    }

    private void EnsureElementStyle(TableViewColumn column)
    {
        foreach (var cell in Cells)
        {
            if (cell.Column == column
                && cell.Content is FrameworkElement element
                && cell.Column is TableViewBoundColumn boundColumn
                && (TableView?.IsEditing is false || TableView?.CurrentCellSlot != cell.Slot))
            {
                element.Style = boundColumn.ElementStyle;
            }
        }
    }

    private void EnsureEditingElementStyle(TableViewColumn column)
    {
        if (TableView?.IsEditing is true
            && TableView.CurrentCellSlot is not null
            && column is TableViewBoundColumn boundColumn
            && TableView.GetCellFromSlot(TableView.CurrentCellSlot.Value) is { } cell
            && cell.Column == column
            && cell.Content is FrameworkElement element)
        {
            element.Style = boundColumn.EditingElementStyle;
        }
    }

    /// <summary>
    /// Ensures the cells style is applied.
    /// </summary>
    internal void EnsureCellsStyle(TableViewColumn? column = null, object? dataItem = null)
    {
        var cells = Cells.Where(x => column is null || x.Column == column);

        foreach (var cell in cells)
        {
            cell.EnsureStyle(dataItem ?? Content);
        }
    }

    /// <summary>
    /// Applies the current cell state to the specified slot.
    /// </summary>
    internal void ApplyCurrentCellState(TableViewCellSlot slot)
    {
        // Datgel DH-2718: by column, so a virtualising row creates the current cell if it has none.
        GetCell(slot.Column)?.ApplyCurrentCellState();
    }

    /// <summary>
    /// Applies the selection state to the cells.
    /// </summary>
    internal void ApplyCellsSelectionState()
    {
        foreach (var cell in Cells)
        {
            cell.ApplySelectionState();
        }
    }

    /// <summary>
    /// Ensures the layout of the row.
    /// </summary>
    internal void EnsureLayout()
    {
        var cornerRadius = _itemPresenter?.CornerRadius ?? new();
        var left = Math.Max(cornerRadius.TopLeft, cornerRadius.BottomLeft) / 2;
        var detailsHeight = RowPresenter?.GetDetailsContentHeight() ?? 0d;
#if WINDOWS
        var selectionIndicator = _itemPresenter?.FindDescendants()
                                                .OfType<Border>()
                                                .FirstOrDefault(x => x is { Width: 3 });

        var cellsHeight = ActualHeight - detailsHeight;
        var selectionIndicatorHeight = Math.Max(Selection_IndicatorHeight, cellsHeight - 40);

        if (selectionIndicator is not null)
        {
            selectionIndicator.MaxHeight = selectionIndicatorHeight;
            selectionIndicator.Margin = new Thickness(
                _selectionIndicatorMargin.Left + left,
                _selectionIndicatorMargin.Top,
                _selectionIndicatorMargin.Right,
                _selectionIndicatorMargin.Bottom);
        }

        if (TableView is ListView { SelectionMode: ListViewSelectionMode.Multiple })
        {
            var fontIcon = this.FindDescendant<FontIcon>(x => x.Glyph == Check_Mark);
            selectionIndicator = fontIcon?.Parent as Border;
        }

        if (TableView is ListView { SelectionMode: ListViewSelectionMode.Multiple })
        {
            var fontIcon = this.FindDescendant<FontIcon>(x => x.Glyph == Check_Mark);
            selectionIndicator = fontIcon?.Parent as Border;
        }


        _selectionBackground ??= _itemPresenter?.FindDescendants()
                                                .OfType<Border>()
                                                .FirstOrDefault(x => x.Name is not Selection_Background && x.Margin == _selectionBackgroundMargin);

        FocusVisualMargin = new Thickness(
            _focusVisualMargin.Left + left,
            _focusVisualMargin.Top,
            _focusVisualMargin.Right,
            _focusVisualMargin.Bottom + GetHorizontalGridlineHeight());

        EnsureSelectionIndicatorPosition(detailsHeight, selectionIndicator);
#endif
        if (_selectionBackground is not null)
        {
            _selectionBackground.Name = Selection_Background;
            _selectionBackground.Margin = new Thickness(
                _selectionBackgroundMargin.Left + left,
                _selectionBackgroundMargin.Top,
                _selectionBackgroundMargin.Right,
                _selectionBackgroundMargin.Bottom + GetHorizontalGridlineHeight() + detailsHeight);
        }
    }

    /// <summary>
    /// Ensures the position of the selection indicator.
    /// </summary>
    private async void EnsureSelectionIndicatorPosition(double detailsHeight, Border? selectionIndicator)
    {
        await Task.Yield(); // let the animations and visual state changes complete

        if (selectionIndicator is not null)
        {
            // Assign a TranslateTransform for animation
            var translateTransform = new TranslateTransform();
            selectionIndicator.RenderTransform = translateTransform;

            var toValue = RowPresenter?.IsDetailsPanelVisible ?? false ? Math.Round(-detailsHeight / 2) : 0; // move up or down

            var animation = new DoubleAnimation
            {
                To = toValue,
                Duration = new Duration(TimeSpan.Zero)
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, translateTransform);
            Storyboard.SetTargetProperty(animation, "Y"); // vertical movement
            storyboard.Children.Add(animation);

            storyboard.Begin();
        }
    }

    /// <summary>
    /// Ensures alternate colors are applied to the row.
    /// </summary>
    internal void EnsureAlternateColors()
    {
        if (TableView is null || RowPresenter is null) return;

        RowPresenter.Background =
            Index % 2 == 1 && TableView.AlternateRowBackground is not null ? TableView.AlternateRowBackground : _cellPresenterBackground;

        RowPresenter.Foreground =
            Index % 2 == 1 && TableView.AlternateRowForeground is not null ? TableView.AlternateRowForeground : _cellPresenterForeground;
    }

    internal void UpdateSelectCheckMarkOpacity()
    {
        var fontIcon = this.FindDescendant<FontIcon>(x => x.Glyph == Check_Mark);

        if (fontIcon?.Parent is Border border)
        {
            border.Opacity = TableView?.IsEditing is true ? 0.3 : 1;
        }
    }

    /// <summary>
    /// Gets the height of the horizontal gridlines.
    /// </summary>
    private double GetHorizontalGridlineHeight()
    {
        return TableView?.GridLinesVisibility is TableViewGridLinesVisibility.All or TableViewGridLinesVisibility.Horizontal
            ? TableView.HorizontalGridLinesStrokeThickness : 0d;
    }

    /// <summary>
    /// Gets the list of cells in the row.
    /// </summary>
    public IReadOnlyList<TableViewCell> Cells => RowPresenter?.Cells ?? [];

    /// <summary>
    /// Gets the index of the row.
    /// </summary>
    public int Index => TableView?.IndexFromContainer(this) ?? -1;

    /// <summary>
    /// Gets or sets the TableView associated with the row.
    /// </summary>
    public TableView? TableView
    {
        get;
        internal set
        {
            if (field != value)
            {
                OnTableViewChanging();
                field = value;
                OnTableViewChanged();
            }
        }
    }

    /// <inheritdoc/>
    public TableViewRowPresenter? RowPresenter
#if WINDOWS
       => ContentTemplateRoot as TableViewRowPresenter;
#else
    { get; private set; }
#endif

    // Datgel fork (DH-1879): v1.5.0 creates AutomationPeers.TableViewRowAutomationPeer here. This fork
    // does not (as in 1.4.1.3): a UI Automation client sees a row as the item DATA peer the list peer
    // creates, and a consumer that decides the row's answer there (Datgel Hub's
    // HubGridRowItemAutomationPeer, DH-1450) must not have the container's peer change underneath it
    // unmeasured. The v1.5.0 class is still compiled. Adopting it is DH-1880.
}
