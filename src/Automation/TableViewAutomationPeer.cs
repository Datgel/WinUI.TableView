#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUI.TableView.Automation;

/// <summary>
/// Exposes a <see cref="TableView"/> to UI Automation as a data grid
/// (<see cref="AutomationControlType.DataGrid"/>) with the <c>Grid</c> and <c>Table</c> patterns, and
/// puts the column headers into the automation tree in front of the rows.
/// </summary>
/// <remarks>
/// <para><b>Headers.</b> The list peer's children are the realized item containers (plus the items
/// presenter's own header/footer). The column headers live in the <c>HeaderRow</c> template part,
/// outside the items presenter, so without this override no automation walk reaches them at all.
/// Each header is returned as its CANONICAL peer (<see cref="FrameworkElementAutomationPeer.CreatePeerForElement"/>),
/// never a second object for the same element.</para>
/// <para><b>Grid / Table.</b> <c>RowCount</c> is the item count and <c>ColumnCount</c> the visible
/// column count; <c>GetItem</c> returns the cell's peer, scrolling its row into view when it is not
/// realized. The table is row-major and has column headers only — row headers are presentation
/// (row numbers, templates) rather than a header a cell belongs to.</para>
/// <para><b>Windows App SDK only.</b> On Uno this peer would have to call the list peer's
/// <c>GetPatternCore</c> and <c>GetChildrenCore</c> as base calls, and a base call from a library
/// compiled against one Uno reference binds NON-VIRTUALLY to the nearest type that declared the
/// method in THAT reference. Measured: in Uno.WinUI 6.2.87 (this package's reference) neither method
/// is declared between <c>ListViewAutomationPeer</c> and <c>FrameworkElementAutomationPeer</c> /
/// <c>AutomationPeer</c>, while the 6.7 Skia runtime overrides both on
/// <c>ListViewBaseAutomationPeer</c> / <c>SelectorAutomationPeer</c> / <c>ItemsControlAutomationPeer</c>
/// — so on a newer runtime the base calls would silently skip the list's own Selection/Scroll
/// patterns and item children. An Uno app that wants the Grid/Table patterns supplies its own
/// <c>TableView</c> peer, compiled against the Uno it runs on; the cell, header and row peers in
/// this package make no such base call and work on every head.</para>
/// </remarks>
public partial class TableViewAutomationPeer : ListViewAutomationPeer, IGridProvider, ITableProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TableViewAutomationPeer"/> class.
    /// </summary>
    /// <param name="owner">The table this peer represents.</param>
    public TableViewAutomationPeer(TableView owner) : base(owner)
    {
    }

    private TableView Table => (TableView)Owner;

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => nameof(TableView);

    /// <inheritdoc/>
    protected override object GetPatternCore(PatternInterface patternInterface)
    {
        return patternInterface is PatternInterface.Grid or PatternInterface.Table
            ? this
            : base.GetPatternCore(patternInterface)!;
    }

    /// <inheritdoc/>
    protected override IList<AutomationPeer> GetChildrenCore()
    {
        var items = base.GetChildrenCore();
        var headers = HeaderPeers();
        if (headers.Count == 0)
        {
            return items;
        }

        var children = new List<AutomationPeer>(headers);
        if (items is not null)
        {
            children.AddRange(items);
        }

        return children;
    }

    /// <summary>The visible column headers' canonical peers, in visible-column order.</summary>
    private List<AutomationPeer> HeaderPeers()
    {
        var peers = new List<AutomationPeer>();
        if (!TableViewCellAutomationPeer.AreColumnHeadersVisible(Table))
        {
            return peers;
        }

        foreach (var column in Table.Columns.VisibleColumns)
        {
            if (column.HeaderControl is { Visibility: Visibility.Visible } header
                && (FromElement(header) ?? CreatePeerForElement(header)) is { } peer)
            {
                peers.Add(peer);
            }
        }

        return peers;
    }

    /// <inheritdoc/>
    public int RowCount => Table.Items.Count;

    /// <inheritdoc/>
    public int ColumnCount => Table.Columns.VisibleColumns.Count;

    /// <inheritdoc/>
    public IRawElementProviderSimple? GetItem(int row, int column)
    {
        if (row < 0 || row >= RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if (column < 0 || column >= ColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        var slot = new TableViewCellSlot(row, column);
        var cell = Table.GetCellFromSlot(slot);
        if (cell is null)
        {
            // Not realized: bring the row into view so a container (and its cells) exists.
            Table.ScrollIntoView(Table.Items[row]);
            Table.UpdateLayout();
            cell = Table.GetCellFromSlot(slot);
        }

        return cell is not null && (FromElement(cell) ?? CreatePeerForElement(cell)) is { } peer
            ? ProviderFromPeer(peer)
            : null;
    }

    /// <inheritdoc/>
    public RowOrColumnMajor RowOrColumnMajor => RowOrColumnMajor.RowMajor;

    /// <inheritdoc/>
    public IRawElementProviderSimple[] GetColumnHeaders() =>
        [.. HeaderPeers().Select(ProviderFromPeer).Where(x => x is not null)];

    /// <inheritdoc/>
    public IRawElementProviderSimple[] GetRowHeaders() => [];
}
#endif
