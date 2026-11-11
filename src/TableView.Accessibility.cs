using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;
using WinUI.TableView.Extensions;

namespace WinUI.TableView;

/// <summary>
/// Partial class for TableView that provides UI Automation support.
/// </summary>
/// <remarks>
/// <para><b>Datgel fork (DH-1445 re-cut onto v1.5.0, DH-1879).</b> v1.5.0 added its own peers under
/// <c>WinUI.TableView.AutomationPeers</c>; this fork keeps the DH-1445 peers under
/// <c>WinUI.TableView.Automation</c> as the ones the controls CREATE, so a consumer's automation tree
/// is unchanged from 1.4.1.3. The v1.5.0 peer classes are still compiled (public).</para>
/// <para><b>Except the column header (DH-1944).</b> Once v1.5.0's peers took their control types and row
/// names from <see cref="TableViewLocalizedStrings"/> instead of English literals, its column-header peer
/// became a superset of the DH-1445 one (Invoke to cycle the sort, sort/filter hints) and replaced it. The
/// cell, table, row and row-header behaviour is unchanged.</para>
/// <para>The table's own peer is created on the Windows App SDK targets only, as in 1.4.1.3: on Uno a
/// list peer's base calls, compiled against this package's Uno reference, bind non-virtually and can
/// skip the runtime's own list overrides, so an Uno app supplies its own table peer.</para>
/// </remarks>
public partial class TableView
{
    /// <summary>
    /// Gets the currently realized row containers.
    /// </summary>
    internal IReadOnlyList<TableViewRow> Rows => _rows;

#if WINDOWS
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new Automation.TableViewAutomationPeer(this);
    }
#endif

    /// <summary>
    /// Whether the specified cell is in the selected cell ranges. Read from the ranges rather than
    /// <see cref="SelectedCells"/>, which is refreshed on the dispatcher after a selection change.
    /// </summary>
    internal bool IsCellInSelection(TableViewCellSlot slot) =>
        SelectedCellRanges.Any(range => range.Contains(slot.Row, slot.Column));

    /// <summary>Whether any cell other than the specified one is selected.</summary>
    internal bool IsAnotherCellSelected(TableViewCellSlot slot) =>
        SelectedCellRanges.Any(range => range.Length > 1 || range.FirstSlot != slot);

    /// <summary>
    /// Selects a cell on behalf of UI Automation, through the same path a click takes (so the current
    /// cell moves with it): replacing the selection, or adding to it as a Ctrl+click would.
    /// </summary>
    internal void SelectCellForAutomation(TableViewCellSlot slot, bool addToSelection)
    {
        // In Multiple mode every selection is additive (MakeSelection forces Ctrl), so a Select that
        // must replace the selection clears it first.
        if (!addToSelection && SelectionMode is ListViewSelectionMode.Multiple)
        {
            DeselectAll();
        }

        SelectionStartCellSlot = slot;
        MakeSelection(slot, false, addToSelection);
    }
}
