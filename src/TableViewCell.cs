using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinUI.TableView.Extensions;
using WinUI.TableView.Helpers;

namespace WinUI.TableView;

/// <summary>
/// Represents a cell in a TableView.
/// </summary>
[TemplateVisualState(Name = VisualStates.StateNormal, GroupName = VisualStates.GroupCommon)]
[TemplateVisualState(Name = VisualStates.StatePointerOver, GroupName = VisualStates.GroupCommon)]
[TemplateVisualState(Name = VisualStates.StateRegular, GroupName = VisualStates.GroupCurrent)]
[TemplateVisualState(Name = VisualStates.StateCurrent, GroupName = VisualStates.GroupCurrent)]
[TemplateVisualState(Name = VisualStates.StateSelected, GroupName = VisualStates.GroupSelection)]
[TemplateVisualState(Name = VisualStates.StateUnselected, GroupName = VisualStates.GroupSelection)]
#if WINDOWS
[WinRT.GeneratedBindableCustomProperty]
#endif
public partial class TableViewCell : ContentControl
{
    private ContentPresenter? _contentPresenter;
    private Border? _selectionBorder;
    private Border? _backgroundBorder;
    private Border? _rootBorder;
    private Rectangle? _v_gridLine;
    private object? _uneditedValue;
    private RoutedEventArgs? _editingArgs;
    private IList<TableViewConditionalCellStyle>? _cellStyles;
    private bool? _lastAppliedSelection;
    private bool _resizePreviewActive;
    private double _resizePreviewWidth;
    private double _resizePreviewMaxWidth;
    private RectangleGeometry? _resizeClipGeometry;
    private TranslateTransform? _gridLineShiftTransform;
    private TranslateTransform? _downstreamShiftTransform;

    /// <summary>
    /// Initializes a new instance of the TableViewCell class.
    /// </summary>
    public TableViewCell()
    {
        DefaultStyleKey = typeof(TableViewCell);
        ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        Loaded += OnLoaded;
#if WINDOWS
        ContextRequested += OnContextRequested;
#endif
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

        // Select the cell before showing the Context Menu
        if (TableView is not null && TableView.ForceRowOrCellSelectionOnContextRequested && !IsSelected)
        {
            TableView.MakeSelection(Slot, false);
        }

        e.Handled = TableView?.ShowCellContext(this, position) is true;
    }


    /// <summary>
    /// Handles the Loaded event.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InvalidateMeasure();
        ApplySelectionState();
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _contentPresenter = GetTemplateChild("Content") as ContentPresenter;
        _selectionBorder = GetTemplateChild("SelectionBorder") as Border;
        _backgroundBorder = GetTemplateChild("BackgroundBorder") as Border;
        _rootBorder = GetTemplateChild("RootBorder") as Border;
        _v_gridLine = GetTemplateChild("VerticalGridLine") as Rectangle;

        EnsureGridLines();
        EnsureStyle(Row?.Content);
    }

    /// <inheritdoc/>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (newContent is ContentControl contentControl)
        {
            contentControl.Loaded += OnContentLoaded;
        }

        void OnContentLoaded(object sender, RoutedEventArgs e)
        {
            ((ContentControl)sender).Loaded -= OnContentLoaded;
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (TableView is not null && Column is not null && Row is not null && _contentPresenter is not null && Content is FrameworkElement element)
        {
            if (Column is TableViewTemplateColumn)
            {
#if WINDOWS
                if (element is ContentControl { ContentTemplateRoot: FrameworkElement root })
#else
                if (element.FindDescendant<ContentPresenter>() is { ContentTemplateRoot: FrameworkElement root })
#endif
                    element = root;
                else
                    return base.MeasureOverride(availableSize);
            }

            #region TEMP_FIX_FOR_ISSUE https://github.com/microsoft/microsoft-ui-xaml/issues/9860
            element.MaxWidth = double.PositiveInfinity;
            element.MaxHeight = double.PositiveInfinity;
            #endregion

            // Skip the unconstrained auto-width measurement while the column is being manually
            // resized — it only feeds Column.DesiredWidth, which is irrelevant to a pixel-width drag,
            // and this cell doesn't get remeasured on every drag frame anyway (see BeginResizePreview).
            if (!Column.IsResizing)
            {
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var autoSizeMode = Column.ColumnAutoWidthMode ?? TableView.ColumnAutoWidthMode;
                if (autoSizeMode is TableViewColumnAutoWidthMode.Cells or TableViewColumnAutoWidthMode.Both)
                {
                    var desiredWidth = element.DesiredSize.Width;
                    desiredWidth += Padding.Left;
                    desiredWidth += Padding.Right;
                    desiredWidth += BorderThickness.Left;
                    desiredWidth += BorderThickness.Right;
                    desiredWidth += _selectionBorder?.BorderThickness.Right ?? 0;
                    desiredWidth += _selectionBorder?.BorderThickness.Left ?? 0;
                    desiredWidth += _v_gridLine?.ActualWidth ?? 0d;

                    Column.DesiredWidth = Math.Max(Column.DesiredWidth, desiredWidth);
                }
            }

            #region TEMP_FIX_FOR_ISSUE https://github.com/microsoft/microsoft-ui-xaml/issues/9860
            // While a resize preview is active, the content was already generously (re)measured once
            // in BeginResizePreview and must keep that width so Clip can freely reveal/hide it every
            // frame without another Measure pass — using the live Column.ActualWidth here (which is
            // intentionally frozen during the drag, see TableView.UpdateColumnResizePreview) would
            // re-clamp the content straight back to the pre-drag size.
            var contentWidth = _resizePreviewActive ? _resizePreviewWidth : Column.ActualWidth;
            contentWidth -= element.Margin.Left;
            contentWidth -= element.Margin.Right;
            contentWidth -= Padding.Left;
            contentWidth -= Padding.Right;
            contentWidth -= BorderThickness.Left;
            contentWidth -= BorderThickness.Right;
            contentWidth -= _selectionBorder?.BorderThickness.Left ?? 0;
            contentWidth -= _selectionBorder?.BorderThickness.Right ?? 0;
            contentWidth -= _v_gridLine?.ActualWidth ?? 0d;

            var height = Height is double.NaN ? double.PositiveInfinity : Height;
            var contentHeight = Math.Min(height, MaxHeight);
            contentHeight -= element.Margin.Top;
            contentHeight -= element.Margin.Bottom;
            contentHeight -= Padding.Top;
            contentHeight -= Padding.Bottom;
            contentHeight -= BorderThickness.Top;
            contentHeight -= BorderThickness.Bottom;
            contentHeight -= _selectionBorder?.BorderThickness.Top ?? 0;
            contentHeight -= _selectionBorder?.BorderThickness.Bottom ?? 0;
            contentHeight -= GetHorizontalGridlineHeight();

            if (contentWidth < 0 || contentHeight < 0)
            {
                _contentPresenter.Visibility = Visibility.Collapsed;
            }
            else
            {
                element.MaxWidth = contentWidth;
                element.MaxHeight = contentHeight;
                _contentPresenter.Visibility = Visibility.Visible;
            }
            #endregion
        }

        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        finalSize = base.ArrangeOverride(finalSize);

        // During a resize-drag preview, manually re-arrange the overlapping template borders wider
        // than the Grid's own column-based sizing would give them (the Grid still thinks this cell is
        // its pre-drag width, since Width itself is left untouched for the whole drag) — this is what
        // lets the generously-premeasured content in BeginResizePreview actually render past the old
        // boundary; Clip then reveals/hides it every frame. Same "arrange a child beyond what the
        // framework gave it" technique already used in TableViewRow.ArrangeOverride for _itemPresenter.
        if (_resizePreviewActive)
        {
            var bordersRect = new Rect(0, 0, _resizePreviewMaxWidth, finalSize.Height);
            _backgroundBorder?.Arrange(bordersRect);
            _selectionBorder?.Arrange(bordersRect);
            _rootBorder?.Arrange(new Rect(0, 0, _resizePreviewWidth, finalSize.Height));
        }

        return finalSize;
    }

    /// <summary>
    /// Begins a live resize-drag preview for this cell: generously (re)measures its content once so
    /// widening can freely reveal more of it, and creates this cell's own <see cref="Clip"/> geometry
    /// and gridline shift transform. These are per-cell instances (not shared across cells — WinUI
    /// throws if the same <see cref="RectangleGeometry"/> is assigned as <see cref="Clip"/> on more
    /// than one element at a time), mutated in place every frame by
    /// <see cref="UpdateResizePreviewClip"/>/<see cref="UpdateGridLineShift"/> — still no Measure/Arrange
    /// per frame, just not a single shared instance across every row.
    /// </summary>
    internal void BeginResizePreview(double maxPreviewWidth)
    {
        _resizePreviewWidth = ActualWidth;

        if (Content is FrameworkElement element)
        {
            if (Column is TableViewTemplateColumn)
            {
#if WINDOWS
                if (element is ContentControl { ContentTemplateRoot: FrameworkElement root })
#else
                if (element.FindDescendant<ContentPresenter>() is { ContentTemplateRoot: FrameworkElement root })
#endif
                    element = root;
                else
                    element = null!;
            }

            if (element is not null)
            {
                element.MaxWidth = maxPreviewWidth;
                element.MaxHeight = double.PositiveInfinity;
                element.Measure(new Size(maxPreviewWidth, double.PositiveInfinity));

                var desiredWidth = element.DesiredSize.Width;
                desiredWidth += element.Margin.Left;
                desiredWidth += element.Margin.Right;
                desiredWidth += Padding.Left;
                desiredWidth += Padding.Right;
                desiredWidth += BorderThickness.Left;
                desiredWidth += BorderThickness.Right;
                desiredWidth += _selectionBorder?.BorderThickness.Left ?? 0;
                desiredWidth += _selectionBorder?.BorderThickness.Right ?? 0;
                desiredWidth += _v_gridLine?.ActualWidth ?? 0d;

                _resizePreviewWidth = Math.Min(maxPreviewWidth, Math.Max(ActualWidth, desiredWidth));
            }
        }

        _resizePreviewActive = true;
        _resizePreviewMaxWidth = maxPreviewWidth;

        _resizeClipGeometry = new RectangleGeometry { Rect = ComputeClipRect(ActualWidth, ActualHeight) };
        Clip = _resizeClipGeometry;

        if (_v_gridLine is not null)
        {
            _gridLineShiftTransform = new TranslateTransform();
            _v_gridLine.RenderTransform = _gridLineShiftTransform;
        }

        InvalidateArrange();
    }

    /// <summary>
    /// Shifts this cell sideways to visually make room for the column being resized, without any
    /// real layout — creates this cell's own <see cref="TranslateTransform"/>, mutated in place every
    /// frame by <see cref="UpdateDownstreamShift"/>.
    /// </summary>
    internal void ApplyDownstreamShift()
    {
        _downstreamShiftTransform = new TranslateTransform();
        RenderTransform = _downstreamShiftTransform;
    }

    /// <summary>
    /// Updates this resize-preview cell's clip to the given live drag width. No-op if this cell
    /// isn't the one being resized (i.e. <see cref="BeginResizePreview"/> was never called on it).
    /// </summary>
    internal void UpdateResizePreviewClip(double liveWidth, double height)
    {
        if (_resizeClipGeometry is not null)
        {
            _resizeClipGeometry.Rect = ComputeClipRect(liveWidth, height);
        }
    }

    /// <summary>
    /// Shifts this resize-preview cell's own gridline to track the live drag boundary. No-op if this
    /// cell isn't the one being resized.
    /// </summary>
    internal void UpdateGridLineShift(double deltaX)
    {
        if (_gridLineShiftTransform is not null)
        {
            _gridLineShiftTransform.X = deltaX;
        }
    }

    /// <summary>
    /// Updates this downstream cell's shift to the given delta. No-op if <see cref="ApplyDownstreamShift"/>
    /// was never called on this cell.
    /// </summary>
    internal void UpdateDownstreamShift(double deltaX)
    {
        if (_downstreamShiftTransform is not null)
        {
            _downstreamShiftTransform.X = deltaX;
        }
    }

    /// <summary>
    /// Ends a resize-drag preview started by <see cref="BeginResizePreview"/> or
    /// <see cref="ApplyDownstreamShift"/>, reverting this cell to normal layout-driven sizing.
    /// </summary>
    internal void EndResizePreview()
    {
        _resizePreviewActive = false;
        _resizePreviewMaxWidth = 0d;
        Clip = null;
        RenderTransform = null;
        _resizeClipGeometry = null;
        _gridLineShiftTransform = null;
        _downstreamShiftTransform = null;

        if (_v_gridLine is not null)
        {
            _v_gridLine.RenderTransform = null;
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    /// <summary>
    /// Computes the clip rect that reveals/hides a resize-preview cell's content for a given live
    /// drag width. Pure function — no side effects — so it's directly unit-testable.
    /// </summary>
    internal static Rect ComputeClipRect(double liveWidth, double height)
    {
        return new Rect(0, 0, Math.Max(0, liveWidth), Math.Max(0, height));
    }

    /// <inheritdoc/>
    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);

        if ((TableView?.SelectionMode is not ListViewSelectionMode.None
           && TableView?.SelectionUnit is not TableViewSelectionUnit.Row)
           || !TableView.IsReadOnly)
        {
            VisualStates.GoToState(this, false, VisualStates.StatePointerOver);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);

        if ((TableView?.SelectionMode is not ListViewSelectionMode.None
            && TableView?.SelectionUnit is not TableViewSelectionUnit.Row)
            || !TableView.IsReadOnly)
        {
            VisualStates.GoToState(this, false, VisualStates.StateNormal);
        }
    }

    /// <inheritdoc/>
    protected override void OnTapped(TappedRoutedEventArgs e)
    {
        base.OnTapped(e);

        if (!TryEndCurrentCellEdit())
        {
            e.Handled = true;
            return;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!TryEndCurrentCellEdit())
        {
            e.Handled = true;
            return;
        }

        e.Handled = TableView?.OnAnyPointerPressed(this, e) ?? false;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (TableView?.SelectionUnit is not TableViewSelectionUnit.Row)
        {
            e.Handled = true;
        }

        TableView?.EndDragSelection();
    }

    /// <summary>
    /// Tries to end the current edit operation, if any.
    /// </summary>
    /// <returns>True if an edit operation was successfully ended, or there is no edit operation.
    /// False if the current edit operation can not be ended.</returns>
    private bool TryEndCurrentCellEdit()
    {
        if ((TableView?.IsEditing ?? false) &&
             TableView.CurrentCellSlot != Slot &&
             TableView.CurrentCellSlot.HasValue &&
             TableView.GetCellFromSlot(TableView.CurrentCellSlot.Value) is { } currentCell)
        {
            if (!TableView.EndCellEditing(TableViewEditAction.Commit, currentCell)) return false;

            TableView.SetIsEditing(false);
        }

        return true;
    }

    /// <summary>
    /// Gets the height of the horizontal gridlines/>.
    /// </summary>
    private double GetHorizontalGridlineHeight()
    {
        return TableView?.GridLinesVisibility is TableViewGridLinesVisibility.All or TableViewGridLinesVisibility.Horizontal
            ? TableView.HorizontalGridLinesStrokeThickness : 0d;
    }

    /// <inheritdoc/>
    protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        var eventArgs = new TableViewCellDoubleTappedEventArgs(Slot, this, Row?.Content);
        TableView?.OnCellDoubleTapped(eventArgs);
        e.Handled = eventArgs.Handled;

        if (e.Handled) return;

        base.OnDoubleTapped(e);

        e.Handled = IsReadOnly || TableView is null || TableView.IsEditing || !Column?.UseSingleElement is not true || BeginCellEditing(e);
    }

    /// <summary>
    /// Initiates editing mode for the current cell, raising the beginning edit event and allowing cancellation.
    /// </summary>
    /// <param name="editingArgs">The event data associated with the editing request. Cannot be null.</param>
    /// <remarks>
    /// <para>A column drawing itself through <see cref="TableViewColumn.UseSingleElement"/> never enters
    /// an edit session, and that refusal is decided HERE - in the one method every route into a session
    /// reaches - rather than once per caller. <see cref="OnDoubleTapped"/> and <see cref="BeginEdit"/>
    /// each carry their own earlier check, so for them this is belt-and-braces; the keyboard route
    /// (<c>TableView.HandleNavigations</c>, F2 and the tab/enter hop) carried none, so a check box or a
    /// toggle switch DID enter a session. Nothing was regenerated - <see cref="SetEditingElement"/>
    /// answers with the resting content on such a column - but <see cref="TableView"/> was left
    /// reporting <c>IsEditing</c> with no editor behind it, and that state suppresses arrow-key
    /// navigation, every cell's double-tap and every <see cref="BeginEdit"/> until it is ended.</para>
    /// <para>Guarding the callers one at a time is what produced that gap: two of the three routes
    /// remembered the flag and the third did not. A fourth route cannot forget.</para>
    /// </remarks>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if cell editing was
    /// successfully started; otherwise, <see langword="false"/> if the operation was canceled.</returns>
    internal bool BeginCellEditing(RoutedEventArgs editingArgs)
    {
        if (Column?.UseSingleElement is true)
        {
            return false;
        }

        var args = new TableViewBeginningEditEventArgs(this, Row?.Content, Column!, editingArgs);
        TableView?.OnBeginningEdit(args);

        if (!args.Cancel)
        {
            PrepareForEdit(editingArgs);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Begins editing this cell, as a double tap or F2 does.
    /// </summary>
    /// <remarks>
    /// The control starts an edit session from <see cref="OnDoubleTapped"/> and from the
    /// <c>TableView</c> key handler, but offers no public way to ask for one. A column whose RESTING
    /// cell carries an affordance of its own - a drop-down arrow, a picker button - needs to open the
    /// editor on a single click of that affordance, and that is not a gesture the control can infer.
    /// <para>The same conditions apply as to a double tap: a read-only cell, a cell with no owning
    /// <see cref="TableView"/>, a table that is already editing, or a column drawing itself through
    /// <see cref="TableViewColumn.UseSingleElement"/> will not begin an edit, and this returns
    /// <see langword="false"/>. <c>BeginningEdit</c> is raised, and a handler that cancels it is
    /// honoured, exactly as on the existing paths.</para>
    /// <para>On v1.5.0 <c>BeginCellEditing</c> is synchronous, so the task is always already
    /// complete. It stays <c>Task&lt;bool&gt;</c> because the v1.4.1 cut of this patch returned the
    /// task (there <c>BeginCellEditing</c> was <c>async</c>), and consumers read
    /// <c>IsCompletedSuccessfully</c>/<c>Result</c> synchronously; keeping the shape keeps them
    /// source- and binary-compatible across the re-cut.</para>
    /// </remarks>
    /// <returns><see langword="true"/> if the cell entered edit mode; otherwise <see langword="false"/>.</returns>
    public Task<bool> BeginEdit()
    {
        if (IsReadOnly || TableView is null || TableView.IsEditing || Column?.UseSingleElement is not false)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(BeginCellEditing(new RoutedEventArgs()));
    }

    /// <summary>
    /// Prepares the cell for editing.
    /// </summary>
    internal void PrepareForEdit(RoutedEventArgs editingArgs)
    {
        // DH-2231: a single-element column edits its resting element, so a deferred one must exist first.
        if (Column?.UseSingleElement is true)
            EnsureElement();
        EnsureTemplate(); // DH-2695: a cell being edited is on screen, so it has its template
        var editingElement = SetEditingElement();
        IsElementDeferred = false;
        Content = editingElement;

        if (TableView is not null)
        {
            TableView.SetIsEditing(true);
            TableView.UpdateCornerButtonState();
        }

        if (editingElement is { IsHitTestVisible: true })
        {
            _editingArgs = editingArgs;
            editingElement.Loaded += OnEditingElementLoaded;
        }
    }

    private void OnEditingElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement editingElement)
        {
            editingElement.Loaded -= OnEditingElementLoaded;
            editingElement.Focus(FocusState.Pointer);
            _editingArgs ??= new RoutedEventArgs();

            var args = new TableViewPreparingCellForEditEventArgs(this, Row?.Content, Column!, editingElement, _editingArgs);
            _uneditedValue = Column?.PrepareCellForEdit(this, _editingArgs);
            TableView?.OnPreparingCellForEdit(args);
        }
    }

    /// <summary>
    /// Sets the editing element for the cell.
    /// </summary>
    private FrameworkElement? SetEditingElement()
    {
        if (Column?.UseSingleElement ?? false)
        {
            return Content as FrameworkElement;
        }
        else
        {
            var element = Column?.GenerateEditingElement(this, Row?.Content);

            if (element is not null && Column is TableViewBoundColumn { EditingElementStyle: { } } boundColumn)
            {
                element.Style = boundColumn.EditingElementStyle;
            }

            return element;
        }
    }

    internal void EndEditing(TableViewEditAction editAction)
    {
        Column?.EndCellEditing(this, Row?.Content, editAction, _uneditedValue);
        // The cell just edited is the current one and in view: never deferred (DH-2231).
        RealizeElement(focus: true);
    }

    /// <summary>
    /// Sets the element for the cell, or - for a cell scrolled out of the horizontal viewport - defers building it
    /// until it comes into view (<see cref="TableView.DefersOffscreenCellElements"/>, Datgel DH-2231).
    /// </summary>
    internal void SetElement()
    {
        if (ShouldDeferElement() && !IsArrangedInView())
        {
            IsElementDeferred = true;
            DeferTemplate(); // DH-2695: nor its template - neither is needed until it comes into view
            if (Content is not null)
                Content = null;
            return;
        }

        RealizeElement(focus: true);
    }

    /// <summary>
    /// Whether this cell has not built its element yet because it was out of view (Datgel DH-2231). It keeps its
    /// place, width, index and slot; <see cref="EnsureElement"/> builds the element.
    /// </summary>
    internal bool IsElementDeferred { get; private set; }

    /// <summary>
    /// Whether this cell has not applied its template because its element is deferred (Datgel DH-2695). It keeps its
    /// column, width, index and slot but has no visual tree at all - no borders, presenter or grid line - until its
    /// element is built.
    /// </summary>
    /// <remarks>
    /// Templating every cell of every realised row was what remained of a wide table's first layout after DH-2231
    /// deferred the elements: Datgel Hub's LOCATION, 177 columns x 42 rows (21 realised), took 14-18 s to open, and a
    /// CPU trace put ~40 % of it in the cell template's Build.
    /// </remarks>
    internal bool IsTemplateDeferred { get; private set; }

    /// <summary>
    /// Withholds the template (a local <c>null</c> Template, which outranks the style's) until
    /// <see cref="EnsureTemplate"/> (Datgel DH-2695) - the mechanism a deferred column header uses (DH-2252).
    /// </summary>
    internal void DeferTemplate()
    {
        if (IsTemplateDeferred)
            return;
        IsTemplateDeferred = true;
        Template = null;
    }

    /// <summary>Gives a deferred cell its template back (the style's). Does nothing otherwise.</summary>
    internal void EnsureTemplate()
    {
        if (!IsTemplateDeferred)
            return;
        IsTemplateDeferred = false;
        ClearValue(TemplateProperty);
        // Uno's Control.OnTemplateChanged neither applies the new template nor invalidates measure (DH-2252), and a
        // cell's width is fixed, so nothing else would.
        ApplyTemplate();
        InvalidateMeasure();
    }

    /// <summary>
    /// Builds the element of a cell whose element was deferred (Datgel DH-2231). Does nothing otherwise.
    /// </summary>
    internal void EnsureElement()
    {
        if (IsElementDeferred)
            RealizeElement(focus: false);
    }

    /// <summary>
    /// Whether this cell may defer its element (Datgel DH-2231): the table defers off-screen elements, and the
    /// column is not frozen (the frozen panel is never scrolled), not auto-width (its width is measured from its
    /// cells' elements) and not marked <see cref="TableViewColumn.AlwaysRealizeElement"/> (a wrapping column
    /// decides the row's height).
    /// </summary>
    internal bool ShouldDeferElement() =>
        (TableView ?? Row?.TableView) is { DefersOffscreenCellElements: true }
        && Column is { AlwaysRealizeElement: false, IsFrozen: false } column
        && !column.Width.IsAuto;

    /// <summary>
    /// Whether this cell has been arranged and lies within <see cref="HorizontalCulling.Margin"/> of the viewport.
    /// A cell not laid out yet is not known to be in view.
    /// </summary>
    private bool IsArrangedInView()
    {
        var tableView = TableView ?? Row?.TableView;
        return tableView is not null
               && tableView.ActualWidth > 0
               && ActualWidth > 0
               && HorizontalCulling.IsInView(ActualOffset.X, ActualWidth, tableView.HorizontalOffset, tableView.ActualWidth);
    }

    /// <summary>
    /// Builds the element for the cell. <paramref name="focus"/> keeps the library's own focus request (Uno) for a
    /// cell built normally; a cell realised because it scrolled into view must not take focus from the user.
    /// </summary>
    private void RealizeElement(bool focus)
    {
        EnsureTemplate(); // DH-2695: a cell with an element has its template
        IsElementDeferred = false;
        var element = Column?.GenerateElement(this, Row?.Content);

        if (element is not null && Column is TableViewBoundColumn { ElementStyle: { } } boundColumn)
        {
            element.Style = boundColumn.ElementStyle;
        }

        Content = element;

#if !WINDOWS
        if (focus)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(20);
                Focus(FocusState.Pointer);
            });
        }
#endif

        DispatcherQueue.TryEnqueue(InvalidateMeasure);
    }

    /// <summary>
    /// Refreshes the element for the cell. A deferred cell has none; it is built for the row's current item when it
    /// comes into view.
    /// </summary>
    internal void RefreshElement()
    {
        if (IsElementDeferred)
            return;
        Column?.RefreshElement(this, Row?.Content);
    }

    /// <summary>
    /// Applies the selection state to the cell.
    /// </summary>
    internal void ApplySelectionState()
    {
        var isSelected = IsSelected;
        var stateName = isSelected ? VisualStates.StateSelected : VisualStates.StateUnselected;
        VisualStates.GoToState(this, false, stateName);

        // Tell a listening screen reader, so a selection change is announced rather than found. The
        // first application after the cell is realized is its initial state, not a change.
        var previous = _lastAppliedSelection;
        _lastAppliedSelection = isSelected;
        if (previous.HasValue && previous.Value != isSelected
            && AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)
            && FrameworkElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous.Value, isSelected);
        }
    }

    /// <summary>
    /// Applies the current cell state to the cell.
    /// </summary>
    internal async void ApplyCurrentCellState(bool skipFocus = false)
    {
        var stateName = IsCurrent ? VisualStates.StateCurrent : VisualStates.StateRegular;
        VisualStates.GoToState(this, false, stateName);

        // DH-2231: the current cell always has its element (keyboard toggles, focus and edits act on it).
        if (IsCurrent)
            EnsureElement();

        if (IsCurrent && !skipFocus)
        {
            Focus(FocusState.Pointer);

            await Task.Delay(20);
            if (Content is UIElement { IsHitTestVisible: true } element)
            {
                element.Focus(FocusState.Pointer);
            }
        }
    }

    /// <summary>
    /// Updates the element state for the cell.
    /// </summary>
    internal void UpdateElementState()
    {
        if (IsElementDeferred)
            return; // built in the current state when it comes into view (DH-2231)
        Column?.UpdateElementState(this, Row?.Content);
    }

    /// <summary>
    /// Handles changes to the column.
    /// </summary>
    private void OnColumnChanged()
    {
        if (TableView?.IsEditing == true)
        {
            SetEditingElement();
        }
        else
        {
            SetElement();
        }
    }

    /// <summary>
    /// Ensures grid lines are applied to the cell.
    /// </summary>
    internal void EnsureGridLines()
    {
        if (_v_gridLine is not null && TableView is not null)
        {
            _v_gridLine.Fill = TableView.GridLinesVisibility is TableViewGridLinesVisibility.All or TableViewGridLinesVisibility.Vertical
                               ? TableView.VerticalGridLinesStroke : new SolidColorBrush(Colors.Transparent);
            _v_gridLine.Width = TableView.VerticalGridLinesStrokeThickness;
            _v_gridLine.Visibility = TableView.HeaderGridLinesVisibility is TableViewGridLinesVisibility.All or TableViewGridLinesVisibility.Vertical
                                     || TableView.GridLinesVisibility is TableViewGridLinesVisibility.All or TableViewGridLinesVisibility.Vertical
                                     ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Ensures the correct style is applied to the cell.
    /// </summary>
    /// <param name="item">The data item associated with the cell.</param>
    internal void EnsureStyle(object? item)
    {
        _cellStyles ??= [
            .. Column?.ConditionalCellStyles ?? [], // Column styles have first priority
            .. TableView?.ConditionalCellStyles ?? []]; // TableView styles have second priority

        Style = _cellStyles.FirstOrDefault(c => c.Predicate?.Invoke(new(Column!, item)) is true)?
                          .Style ?? Column?.CellStyle ?? TableView?.CellStyle;
    }

    /// <summary>
    /// Gets a value indicating whether the cell is read-only.
    /// </summary>
    public bool IsReadOnly => TableView?.IsReadOnly is true
                              || Column is TableViewTemplateColumn { EditingTemplate: null, EditingTemplateSelector: null } or { IsReadOnly: true };

    /// <summary>
    /// Gets the slot for the cell.
    /// </summary>
    public TableViewCellSlot Slot => new(Row?.Index ?? -1, Index);

    /// <summary>
    /// Gets or sets the index of the cell.
    /// </summary>
    internal int Index { get; set; }

    /// <summary>
    /// Gets a value indicating whether the cell is selected.
    /// </summary>
    public bool IsSelected => TableView?.SelectedCells.Contains(Slot) is true;

    /// <summary>
    /// Gets a value indicating whether the cell is the current cell.
    /// </summary>
    public bool IsCurrent => TableView?.CurrentCellSlot == Slot;

    /// <summary>
    /// Gets or sets the column for the cell.
    /// </summary>
    public TableViewColumn? Column
    {
        get;
        internal set
        {
            if (field != value)
            {
                field = value;
                OnColumnChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the row for the cell.
    /// </summary>
    public TableViewRow? Row { get; internal set; }

    /// <summary>
    /// Gets or sets the TableView for the cell.
    /// </summary>
    public TableView? TableView { get; internal set; }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        // Datgel fork (DH-1879): the DH-1445 peer (WinUI.TableView.Automation), not v1.5.0's
        // AutomationPeers.TableViewCellAutomationPeer; see TableView.Accessibility.cs.
        return new Automation.TableViewCellAutomationPeer(this);
    }
}
