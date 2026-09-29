using Microsoft.UI.Xaml.Automation.Peers;

namespace WinUI.TableView.Automation;

/// <summary>
/// Exposes a <see cref="TableViewColumnHeader"/> to UI Automation as a column header
/// (<see cref="AutomationControlType.HeaderItem"/>).
/// </summary>
/// <remarks>
/// Without a peer of its own a header falls back to whatever the platform gives a plain
/// <see cref="Microsoft.UI.Xaml.Controls.ContentControl"/>: on Uno that is a generic container
/// (reported as <c>Group</c>), so a screen reader could read the caption but never announce it as the
/// header of a column. The header peers are also what <see cref="TableViewAutomationPeer"/> returns
/// from <c>ITableProvider.GetColumnHeaders</c> and a cell's peer from
/// <c>ITableItemProvider.GetColumnHeaderItems</c>, which is what relates a cell to its header.
/// </remarks>
public partial class TableViewColumnHeaderAutomationPeer : FrameworkElementAutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TableViewColumnHeaderAutomationPeer"/> class.
    /// </summary>
    /// <param name="owner">The column header this peer represents.</param>
    public TableViewColumnHeaderAutomationPeer(TableViewColumnHeader owner) : base(owner)
    {
    }

    private TableViewColumnHeader Header => (TableViewColumnHeader)Owner;

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => nameof(TableViewColumnHeader);

    /// <summary>
    /// The name set on the header (<c>AutomationProperties.Name</c>) wins; otherwise the column's
    /// header text, so an unnamed header is still announced by its caption rather than as nothing.
    /// </summary>
    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return Header.Column?.Header?.ToString() ?? string.Empty;
    }
}
