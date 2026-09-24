using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using System;
using WinUI.TableView.Extensions;

namespace WinUI.TableView.Automation;

/// <summary>
/// Exposes a <see cref="TableViewCell"/> to UI Automation as an item of a grid
/// (<see cref="AutomationControlType.DataItem"/>) with the <c>GridItem</c> and <c>TableItem</c>
/// patterns, and — when the table selects cells — the <c>SelectionItem</c> pattern.
/// </summary>
/// <remarks>
/// <para><b>GridItem / TableItem</b> give a cell its row and column and relate it to its column
/// header, which is what lets a screen reader announce "Depth, row 3" rather than a bare value.</para>
/// <para><b>SelectionItem is advertised only when the table selects CELLS</b>
/// (<see cref="TableViewSelectionUnit.Cell"/> or <see cref="TableViewSelectionUnit.CellOrRow"/>) and
/// selection is enabled. "Selected" means the cell is in the table's selected cell ranges — the same
/// set the table paints as selected — not merely that it is the current (focused) cell; the current
/// cell is conveyed by keyboard focus, and becomes selected whenever it is reached by click or
/// keyboard navigation. <c>Select</c> goes through the same path as a click, so it also moves the
/// current cell.</para>
/// </remarks>
public partial class TableViewCellAutomationPeer : FrameworkElementAutomationPeer,
    IGridItemProvider, ITableItemProvider, ISelectionItemProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TableViewCellAutomationPeer"/> class.
    /// </summary>
    /// <param name="owner">The cell this peer represents.</param>
    public TableViewCellAutomationPeer(TableViewCell owner) : base(owner)
    {
    }

    private TableViewCell Cell => (TableViewCell)Owner;

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => nameof(TableViewCell);

    /// <inheritdoc/>
    protected override object GetPatternCore(PatternInterface patternInterface)
    {
        if (Cell.TableView is { } tableView)
        {
            if (patternInterface is PatternInterface.GridItem or PatternInterface.TableItem)
            {
                return this;
            }

            if (patternInterface is PatternInterface.SelectionItem && SelectsCells(tableView))
            {
                return this;
            }
        }

        return base.GetPatternCore(patternInterface);
    }

    /// <summary>Whether <paramref name="tableView"/> lets a UI Automation client select cells.</summary>
    internal static bool SelectsCells(TableView tableView) =>
        tableView.SelectionMode is not Microsoft.UI.Xaml.Controls.ListViewSelectionMode.None
        && tableView.SelectionUnit is TableViewSelectionUnit.Cell or TableViewSelectionUnit.CellOrRow;

    /// <inheritdoc/>
    public int Row => Cell.Slot.Row;

    /// <inheritdoc/>
    public int Column => Cell.Index;

    /// <inheritdoc/>
    public int RowSpan => 1;

    /// <inheritdoc/>
    public int ColumnSpan => 1;

    /// <inheritdoc/>
    public IRawElementProviderSimple? ContainingGrid => ProviderFor(Cell.TableView);

    /// <inheritdoc/>
    public IRawElementProviderSimple[] GetColumnHeaderItems()
    {
        return Cell.Column?.HeaderControl is { } header
               && Cell.TableView is { } tableView
               && AreColumnHeadersVisible(tableView)
               && ProviderFor(header) is { } provider
            ? [provider]
            : [];
    }

    /// <inheritdoc/>
    public IRawElementProviderSimple[] GetRowHeaderItems() => [];

    /// <summary>Whether <paramref name="tableView"/> is currently showing its column headers.</summary>
    internal static bool AreColumnHeadersVisible(TableView tableView) =>
        tableView.HeadersVisibility is TableViewHeadersVisibility.All or TableViewHeadersVisibility.Columns;

    /// <inheritdoc/>
    public bool IsSelected => Cell.TableView?.IsCellInSelection(Cell.Slot) is true;

    /// <inheritdoc/>
    public IRawElementProviderSimple? SelectionContainer => ProviderFor(Cell.TableView);

    /// <inheritdoc/>
    public void Select() => Apply(SelectionItemRequest.Select);

    /// <inheritdoc/>
    public void AddToSelection() => Apply(SelectionItemRequest.AddToSelection);

    /// <inheritdoc/>
    public void RemoveFromSelection() => Apply(SelectionItemRequest.RemoveFromSelection);

    private void Apply(SelectionItemRequest request)
    {
        var tableView = Cell.TableView ?? throw new InvalidOperationException("The cell is not in a TableView.");

        if (!tableView.IsEnabled || !SelectsCells(tableView))
        {
            throw new InvalidOperationException("The TableView does not select cells.");
        }

        var slot = Cell.Slot;
        if (!slot.IsValid(tableView))
        {
            throw new InvalidOperationException("The cell is not attached to a row.");
        }

        var canSelectMultiple = tableView.SelectionMode is not Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single;
        var isSelected = tableView.IsCellInSelection(slot);

        switch (SelectionItemRule.Decide(canSelectMultiple, isSelected, tableView.IsAnotherCellSelected(slot), request))
        {
            case SelectionItemAction.Refuse:
                throw new InvalidOperationException(
                    $"'{request}' cannot be performed on a TableView whose selection mode is {tableView.SelectionMode}.");
            case SelectionItemAction.NoChange:
                return;
            case SelectionItemAction.SelectOnly:
                tableView.SelectCellForAutomation(slot, addToSelection: false);
                return;
            case SelectionItemAction.Add:
                tableView.SelectCellForAutomation(slot, addToSelection: true);
                return;
            case SelectionItemAction.Remove:
                tableView.DeselectCell(slot);
                return;
        }
    }

    private IRawElementProviderSimple? ProviderFor(UIElement? element) =>
        element is not null && (FromElement(element) ?? CreatePeerForElement(element)) is { } peer
            ? ProviderFromPeer(peer)
            : null;
}
