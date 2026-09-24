using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System;
using System.Linq;
using System.Threading.Tasks;
using WinUI.TableView.Automation;

namespace WinUI.TableView.Tests;

[TestClass]
public class TableViewAutomationPeerTests
{
    [UITestMethod]
    public async Task ColumnHeader_IsAHeaderItem_NamedByItsCaption()
    {
        var tableView = await CreateTableViewAsync();
        var header = tableView.Columns.VisibleColumns[1].HeaderControl!;

        var peer = PeerOf(header);

        Assert.IsInstanceOfType<TableViewColumnHeaderAutomationPeer>(peer);
        Assert.AreEqual(AutomationControlType.HeaderItem, peer.GetAutomationControlType());
        Assert.AreEqual(nameof(TableViewColumnHeader), peer.GetClassName());
        Assert.AreEqual("Column 1", peer.GetName());
    }

    [UITestMethod]
    public async Task ColumnHeader_AnAuthoredName_WinsOverTheCaption()
    {
        var tableView = await CreateTableViewAsync();
        var header = tableView.Columns.VisibleColumns[0].HeaderControl!;
        AutomationProperties.SetName(header, "Depth (m)");

        Assert.AreEqual("Depth (m)", PeerOf(header).GetName());
    }

    [UITestMethod]
    public async Task TableView_IsADataGrid_WithGridAndTablePatterns()
    {
        var tableView = await CreateTableViewAsync();

        var peer = PeerOf(tableView);

        Assert.IsInstanceOfType<TableViewAutomationPeer>(peer);
        Assert.AreEqual(AutomationControlType.DataGrid, peer.GetAutomationControlType());

        var grid = peer.GetPattern(PatternInterface.Grid) as IGridProvider;
        Assert.IsNotNull(grid, "Grid pattern");
        Assert.AreEqual(3, grid.RowCount);
        Assert.AreEqual(4, grid.ColumnCount);
        Assert.IsNotNull(grid.GetItem(2, 3), "GetItem returns the cell");
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => grid.GetItem(3, 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => grid.GetItem(0, 4));

        var table = peer.GetPattern(PatternInterface.Table) as ITableProvider;
        Assert.IsNotNull(table, "Table pattern");
        Assert.AreEqual(RowOrColumnMajor.RowMajor, table.RowOrColumnMajor);
        Assert.AreEqual(4, table.GetColumnHeaders().Length, "one header per visible column");
        Assert.AreEqual(0, table.GetRowHeaders().Length);
    }

    [UITestMethod]
    public async Task TableView_ListSelectionPatterns_AreStillOffered()
    {
        // The grid peer is a ListViewAutomationPeer: adding Grid/Table must not take away the list's
        // own Selection/Scroll patterns.
        var tableView = await CreateTableViewAsync();

        var peer = PeerOf(tableView);

        Assert.IsNotNull(peer.GetPattern(PatternInterface.Selection), "Selection pattern");
        Assert.IsNotNull(peer.GetPattern(PatternInterface.Scroll), "Scroll pattern");
    }

    [UITestMethod]
    public async Task TableView_Children_StartWithTheColumnHeaders()
    {
        var tableView = await CreateTableViewAsync();

        var children = PeerOf(tableView).GetChildren();
        var headers = tableView.Columns.VisibleColumns.Select(c => PeerOf(c.HeaderControl!)).ToList();

        Assert.IsTrue(children.Count > headers.Count, "headers and rows");
        CollectionAssert.AreEqual(headers, children.Take(headers.Count).ToList(),
            "the column headers come first, as their canonical peers");
    }

    [UITestMethod]
    public async Task TableView_Children_OmitHeaders_WhenColumnHeadersAreHidden()
    {
        var tableView = await CreateTableViewAsync();
        tableView.HeadersVisibility = TableViewHeadersVisibility.None;
        tableView.UpdateLayout();

        var children = PeerOf(tableView).GetChildren();

        Assert.IsFalse(children.OfType<TableViewColumnHeaderAutomationPeer>().Any());
        Assert.AreEqual(0, ((ITableProvider)PeerOf(tableView)).GetColumnHeaders().Length);
    }

    [UITestMethod]
    public async Task Cell_IsADataItem_WithItsRowAndColumn()
    {
        var tableView = await CreateTableViewAsync();
        var cell = CellAt(tableView, 1, 2);

        var peer = PeerOf(cell);

        Assert.IsInstanceOfType<TableViewCellAutomationPeer>(peer);
        Assert.AreEqual(AutomationControlType.DataItem, peer.GetAutomationControlType());

        var gridItem = peer.GetPattern(PatternInterface.GridItem) as IGridItemProvider;
        Assert.IsNotNull(gridItem, "GridItem pattern");
        Assert.AreEqual(1, gridItem.Row);
        Assert.AreEqual(2, gridItem.Column);
        Assert.AreEqual(1, gridItem.RowSpan);
        Assert.AreEqual(1, gridItem.ColumnSpan);
        Assert.IsNotNull(gridItem.ContainingGrid);
    }

    [UITestMethod]
    public async Task Cell_TableItem_NamesItsOwnColumnHeader()
    {
        var tableView = await CreateTableViewAsync();
        var peer = (TableViewCellAutomationPeer)PeerOf(CellAt(tableView, 0, 3));

        var tableItem = peer.GetPattern(PatternInterface.TableItem) as ITableItemProvider;

        Assert.IsNotNull(tableItem, "TableItem pattern");
        Assert.AreEqual(1, tableItem.GetColumnHeaderItems().Length);
        Assert.AreEqual(0, tableItem.GetRowHeaderItems().Length);
    }

    [UITestMethod]
    public async Task Cell_OffersSelectionItem_OnlyWhenTheTableSelectsCells()
    {
        var tableView = await CreateTableViewAsync(TableViewSelectionUnit.Cell);
        Assert.IsNotNull(PeerOf(CellAt(tableView, 0, 0)).GetPattern(PatternInterface.SelectionItem), "Cell");

        tableView.SelectionUnit = TableViewSelectionUnit.CellOrRow;
        Assert.IsNotNull(PeerOf(CellAt(tableView, 0, 0)).GetPattern(PatternInterface.SelectionItem), "CellOrRow");

        tableView.SelectionUnit = TableViewSelectionUnit.Row;
        Assert.IsNull(PeerOf(CellAt(tableView, 0, 0)).GetPattern(PatternInterface.SelectionItem), "Row");

        tableView.SelectionUnit = TableViewSelectionUnit.Cell;
        tableView.SelectionMode = ListViewSelectionMode.None;
        Assert.IsNull(PeerOf(CellAt(tableView, 0, 0)).GetPattern(PatternInterface.SelectionItem), "selection off");
    }

    [UITestMethod]
    public async Task Cell_Select_SelectsItAndMovesTheCurrentCell()
    {
        var tableView = await CreateTableViewAsync(TableViewSelectionUnit.Cell);
        var first = SelectionItemOf(tableView, 0, 0);
        var second = SelectionItemOf(tableView, 2, 1);

        first.Select();
        Assert.IsTrue(first.IsSelected);
        Assert.AreEqual(new TableViewCellSlot(0, 0), tableView.CurrentCellSlot);

        second.Select();
        Assert.IsTrue(second.IsSelected);
        Assert.IsFalse(first.IsSelected, "Select replaces the selection");
        Assert.AreEqual(new TableViewCellSlot(2, 1), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task Cell_Select_ReplacesTheSelection_EvenInMultipleMode()
    {
        var tableView = await CreateTableViewAsync(TableViewSelectionUnit.Cell);
        tableView.SelectionMode = ListViewSelectionMode.Multiple;
        var first = SelectionItemOf(tableView, 0, 0);
        var second = SelectionItemOf(tableView, 1, 1);

        first.Select();
        second.Select();

        Assert.IsTrue(second.IsSelected);
        Assert.IsFalse(first.IsSelected);
    }

    [UITestMethod]
    public async Task Cell_AddAndRemove_ChangeOnlyThatCell()
    {
        var tableView = await CreateTableViewAsync(TableViewSelectionUnit.Cell);
        var first = SelectionItemOf(tableView, 0, 0);
        var second = SelectionItemOf(tableView, 1, 2);

        first.Select();
        second.AddToSelection();
        Assert.IsTrue(first.IsSelected && second.IsSelected, "AddToSelection keeps the rest");

        second.AddToSelection();
        Assert.IsTrue(second.IsSelected, "adding a selected cell is a no-op, not a toggle");

        second.RemoveFromSelection();
        Assert.IsFalse(second.IsSelected);
        Assert.IsTrue(first.IsSelected);
    }

    [UITestMethod]
    public async Task Cell_AddToSelection_IsRefusedInSingleMode_WhenAnotherCellIsSelected()
    {
        var tableView = await CreateTableViewAsync(TableViewSelectionUnit.Cell);
        tableView.SelectionMode = ListViewSelectionMode.Single;
        var first = SelectionItemOf(tableView, 0, 0);
        var second = SelectionItemOf(tableView, 1, 1);

        first.Select();

        Assert.ThrowsException<InvalidOperationException>(() => second.AddToSelection());
        Assert.IsTrue(first.IsSelected);
        Assert.IsFalse(second.IsSelected);
    }

    [TestMethod]
    [DataRow(true, false, false, (int)SelectionItemRequest.Select, (int)SelectionItemAction.SelectOnly)]
    [DataRow(true, true, false, (int)SelectionItemRequest.Select, (int)SelectionItemAction.NoChange)]
    [DataRow(true, true, true, (int)SelectionItemRequest.Select, (int)SelectionItemAction.SelectOnly)]
    [DataRow(true, false, true, (int)SelectionItemRequest.AddToSelection, (int)SelectionItemAction.Add)]
    [DataRow(false, false, true, (int)SelectionItemRequest.AddToSelection, (int)SelectionItemAction.Refuse)]
    [DataRow(false, false, false, (int)SelectionItemRequest.AddToSelection, (int)SelectionItemAction.Add)]
    [DataRow(false, true, false, (int)SelectionItemRequest.AddToSelection, (int)SelectionItemAction.NoChange)]
    [DataRow(true, true, true, (int)SelectionItemRequest.RemoveFromSelection, (int)SelectionItemAction.Remove)]
    [DataRow(true, false, true, (int)SelectionItemRequest.RemoveFromSelection, (int)SelectionItemAction.NoChange)]
    public void SelectionItemRule_FollowsTheUiaContract(
        bool canSelectMultiple, bool isSelected, bool anotherIsSelected,
        int request, int expected)
    {
        // The rule is internal (InternalsVisibleTo), so the enums travel as ints through the public
        // test signature.
        Assert.AreEqual((SelectionItemAction)expected,
            SelectionItemRule.Decide(canSelectMultiple, isSelected, anotherIsSelected, (SelectionItemRequest)request));
    }

    private static AutomationPeer PeerOf(UIElement element) =>
        FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);

    private static ISelectionItemProvider SelectionItemOf(TableView tableView, int row, int column) =>
        (ISelectionItemProvider)PeerOf(CellAt(tableView, row, column)).GetPattern(PatternInterface.SelectionItem);

    private static TableViewCell CellAt(TableView tableView, int row, int column)
    {
        var cell = tableView.GetCellFromSlot(new TableViewCellSlot(row, column));
        Assert.IsNotNull(cell, $"Precondition: cell ({row}, {column}) is realized");
        return cell!;
    }

    private static async Task<TableView> CreateTableViewAsync(TableViewSelectionUnit unit = TableViewSelectionUnit.CellOrRow)
    {
        var tableView = new TableView
        {
            Width = 600,
            Height = 300,
            AutoGenerateColumns = false,
            SelectionUnit = unit,
            SelectionMode = ListViewSelectionMode.Extended,
        };

        for (var i = 0; i < 4; i++)
        {
            tableView.Columns.Add(new TableViewTextColumn
            {
                Header = $"Column {i}",
                Width = new GridLength(100),
                Binding = new Binding { Path = new PropertyPath(nameof(PeerTestItem.Name)) }
            });
        }

        tableView.ItemsSource = new[]
        {
            new PeerTestItem { Name = "Alpha" },
            new PeerTestItem { Name = "Beta" },
            new PeerTestItem { Name = "Gamma" }
        };

        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        tableView.UpdateLayout();

        return tableView;
    }

    private sealed class PeerTestItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
