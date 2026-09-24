#if !WINDOWS
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;

namespace WinUI.TableView.Automation;

/// <summary>
/// The platform's list-item peer for a <see cref="TableViewRow"/> — name, class and position in set
/// unchanged — plus the <c>SelectionItem</c> pattern when the table selects ROWS.
/// </summary>
/// <remarks>
/// <para><b>Why it exists (Uno only).</b> Uno's <c>ListViewItem.OnCreateAutomationPeer</c> returns a
/// <c>ListViewItemAutomationPeer</c> that implements no provider interface, so on every Uno head a
/// row exposed no pattern at all and a client could not tell which row was selected. Real WinUI
/// publishes the row's selection through the list's item data peer, so this peer is compiled out on
/// the Windows App SDK targets rather than replacing a platform behaviour that already works.</para>
/// <para><b>SelectionItem is advertised only when the table selects rows</b>
/// (<see cref="TableViewSelectionUnit.Row"/> or <see cref="TableViewSelectionUnit.CellOrRow"/>) and
/// selection is enabled. On a cell-selection table a row is never "selected", so advertising the
/// pattern there would report every row as unselected however much of it is selected — the
/// selection is published on the cells instead (<see cref="TableViewCellAutomationPeer"/>).</para>
/// </remarks>
public partial class TableViewRowAutomationPeer : ListViewItemAutomationPeer, ISelectionItemProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TableViewRowAutomationPeer"/> class.
    /// </summary>
    /// <param name="owner">The row this peer represents.</param>
    public TableViewRowAutomationPeer(TableViewRow owner) : base(owner)
    {
    }

    private TableViewRow Row => (TableViewRow)Owner;

    /// <inheritdoc/>
    protected override object GetPatternCore(PatternInterface patternInterface)
    {
        return patternInterface is PatternInterface.SelectionItem && Row.TableView is { } tableView && SelectsRows(tableView)
            ? this
            : base.GetPatternCore(patternInterface)!;
    }

    /// <summary>Whether <paramref name="tableView"/> lets a UI Automation client select rows.</summary>
    internal static bool SelectsRows(TableView tableView) =>
        tableView.SelectionMode is not ListViewSelectionMode.None
        && tableView.SelectionUnit is TableViewSelectionUnit.Row or TableViewSelectionUnit.CellOrRow;

    /// <inheritdoc/>
    public bool IsSelected => Row.IsSelected;

    /// <inheritdoc/>
    public IRawElementProviderSimple? SelectionContainer =>
        Row.TableView is { } tableView && (FromElement(tableView) ?? CreatePeerForElement(tableView)) is { } peer
            ? ProviderFromPeer(peer)
            : null;

    /// <inheritdoc/>
    public void Select() => Apply(SelectionItemRequest.Select);

    /// <inheritdoc/>
    public void AddToSelection() => Apply(SelectionItemRequest.AddToSelection);

    /// <inheritdoc/>
    public void RemoveFromSelection() => Apply(SelectionItemRequest.RemoveFromSelection);

    private void Apply(SelectionItemRequest request)
    {
        var tableView = Row.TableView ?? throw new InvalidOperationException("The row is not in a TableView.");

        if (!tableView.IsEnabled || !SelectsRows(tableView))
        {
            throw new InvalidOperationException("The TableView does not select rows.");
        }

        var index = Row.Index;
        if (index < 0)
        {
            throw new InvalidOperationException("The row is not attached to an item.");
        }

        var single = tableView.SelectionMode is ListViewSelectionMode.Single;
        var isSelected = Row.IsSelected;
        var anotherIsSelected = tableView.SelectedItems.Count > (isSelected ? 1 : 0);

        switch (SelectionItemRule.Decide(!single, isSelected, anotherIsSelected, request))
        {
            case SelectionItemAction.Refuse:
                throw new InvalidOperationException(
                    $"'{request}' cannot be performed on a TableView whose selection mode is {tableView.SelectionMode}.");
            case SelectionItemAction.NoChange:
                return;
            case SelectionItemAction.SelectOnly:
                tableView.SelectRowForAutomation(index, addToSelection: false);
                return;
            case SelectionItemAction.Add:
                tableView.SelectRowForAutomation(index, addToSelection: true);
                return;
            case SelectionItemAction.Remove when single:
                tableView.SelectedItem = null;
                return;
            case SelectionItemAction.Remove:
                tableView.DeselectRange(new ItemIndexRange(index, 1));
                return;
        }
    }
}
#endif
