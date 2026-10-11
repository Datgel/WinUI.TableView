using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Linq;
using System.Threading.Tasks;
using WinUI.TableView.Extensions;

namespace WinUI.TableView.Tests;

/// <summary>
/// Datgel DH-2718: with <see cref="TableView.VirtualizesColumns"/> on, a row creates cells only for the columns near the
/// horizontal viewport, and places each one exactly where it would be if every column had a cell. Creating a cell per
/// column per row was most of what opening a 177-column table still cost after DH-2695.
/// </summary>
[TestClass]
public class ColumnVirtualizationTests
{
    private const int ColumnCount = 40;
    private const double ColumnWidth = 100;

    [UITestMethod]
    public async Task ARow_HasCellsOnlyNearTheViewport_EachInItsOwnPlace()
    {
        var tableView = await CreateTableViewAsync();
        var cells = FirstRowCells(tableView);

        Assert.IsTrue(cells.Length < ColumnCount, $"only the columns near the viewport have cells ({cells.Length} of {ColumnCount})");
        Assert.IsTrue(cells.Length >= 4, "every column in view has a cell");
        Assert.AreEqual(0, cells[0].Index, "the first column is in view");
        foreach (var cell in cells)
            Assert.AreEqual(cell.Index * ColumnWidth, cell.ActualOffset.X, 0.5, $"cell {cell.Index} sits where its column starts");
        Assert.IsTrue(Panel(tableView).ActualWidth >= ColumnCount * ColumnWidth - 0.5, "the row is as wide as all its columns");
    }

    [UITestMethod]
    public async Task ScrollingSideways_CreatesTheCellsThatComeIntoView()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsFalse(FirstRowCells(tableView).Any(c => c.Index == ColumnCount - 1), "Precondition: the last column has no cell");

        tableView.SetValue(TableView.HorizontalOffsetProperty, (ColumnCount - 4) * ColumnWidth);
        await SettleAsync(tableView);

        var cells = FirstRowCells(tableView);
        var last = cells.SingleOrDefault(c => c.Index == ColumnCount - 1);
        Assert.IsNotNull(last, "the last column has a cell once it is in view");
        Assert.AreEqual((ColumnCount - 1) * ColumnWidth, last!.ActualOffset.X, 0.5, "in its own place");
        Assert.IsTrue(cells.Any(c => c.Index == 0), "a cell already created is kept when it scrolls away");
        CollectionAssert.AreEqual(cells.Select(c => c.Index).OrderBy(i => i).ToArray(), cells.Select(c => c.Index).ToArray(),
            "the row's cells stay in column order");
        foreach (var cell in cells)
            Assert.AreEqual(cell.Index * ColumnWidth, cell.ActualOffset.X, 0.5, $"cell {cell.Index} sits where its column starts");
    }

    [UITestMethod]
    public async Task AnAlwaysRealizedColumn_HasACellWhereverItIs()
    {
        var tableView = await CreateTableViewAsync(alwaysRealize: ColumnCount - 1);
        var last = FirstRowCells(tableView).SingleOrDefault(c => c.Index == ColumnCount - 1);

        Assert.IsNotNull(last, "a column that decides the row's height (a wrapping column) always has a cell");
        Assert.AreEqual((ColumnCount - 1) * ColumnWidth, last!.ActualOffset.X, 0.5, "in its own place, past the gap");
        Assert.IsNotNull(last.Content, "and, never deferred, its element");
    }

    [UITestMethod]
    public async Task TheCurrentCell_IsCreatedOnDemand()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsFalse(FirstRowCells(tableView).Any(c => c.Index == ColumnCount - 2), "Precondition: no cell yet");

        tableView.CurrentCellSlot = new TableViewCellSlot(0, ColumnCount - 2);
        await SettleAsync(tableView);

        var cell = FirstRowCells(tableView).SingleOrDefault(c => c.Index == ColumnCount - 2);
        Assert.IsNotNull(cell, "the current cell exists");
        Assert.IsTrue(cell!.IsCurrent);
        Assert.IsNotNull(cell.Content, "and has its element");
        Assert.AreEqual((ColumnCount - 2) * ColumnWidth, cell.ActualOffset.X, 0.5, "in its own place");
    }

    [UITestMethod]
    public async Task ACellAskedForBySlot_IsCreated_AndSelectedLikeAnyOther()
    {
        var tableView = await CreateTableViewAsync();
        tableView.SelectionUnit = TableViewSelectionUnit.Cell;
        var slot = new TableViewCellSlot(1, ColumnCount - 3);

        tableView.MakeSelection(slot, false);
        await SettleAsync(tableView);

        var cell = tableView.GetCellFromSlot(slot);
        Assert.IsNotNull(cell, "a slot always resolves to a cell");
        Assert.AreEqual(ColumnCount - 3, cell!.Index);
        Assert.IsTrue(cell.IsSelected, "selection is by slot");
    }

    [UITestMethod]
    public async Task AColumnWidthChange_MovesTheCellsAfterIt()
    {
        var tableView = await CreateTableViewAsync();
        tableView.Columns[0].Width = new GridLength(150);
        await SettleAsync(tableView);

        foreach (var cell in FirstRowCells(tableView).Where(c => c.Index > 0))
            Assert.AreEqual(50 + cell.Index * ColumnWidth, cell.ActualOffset.X, 0.5, $"cell {cell.Index} moved with the wider first column");
        Assert.IsTrue(Panel(tableView).ActualWidth >= ColumnCount * ColumnWidth + 50 - 0.5, "the row grew with the column");
    }

    [UITestMethod]
    public async Task TurningItOff_CreatesEveryCell()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(FirstRowCells(tableView).Length < ColumnCount, "Precondition: virtualised");

        tableView.VirtualizesColumns = false;
        await SettleAsync(tableView);

        var cells = FirstRowCells(tableView);
        Assert.AreEqual(ColumnCount, cells.Length, "every column has a cell");
        for (var i = 0; i < ColumnCount; i++)
        {
            Assert.AreEqual(i, cells[i].Index);
            Assert.AreEqual(i * ColumnWidth, cells[i].ActualOffset.X, 0.5, $"cell {i}, no gap margins left behind");
        }
    }

    [UITestMethod]
    public async Task ARecycledRow_HasTheCellsOfTheColumnsScrolledIntoViewWhileItWasAway()
    {
        var tableView = await CreateTableViewAsync(itemCount: 300);
        tableView.SetValue(TableView.HorizontalOffsetProperty, (ColumnCount - 4) * ColumnWidth);
        await SettleAsync(tableView);

        // Scroll to the bottom: the rows now shown are containers recycled from rows cleared at the top - some of them
        // cleared before the sideways scroll above, so it never reached them.
        await tableView.ScrollRowIntoView(tableView.Items.Count - 1);
        await SettleAsync(tableView);

        var rows = tableView.FindDescendants().OfType<TableViewRow>()
            .Where(r => r.Visibility == Visibility.Visible && r.Index > 100 && ReferenceEquals(tableView.ContainerFromIndex(r.Index), r))
            .ToList();
        Assert.IsTrue(rows.Count > 0, "Precondition: rows far down the list are shown, in recycled containers");
        foreach (var row in rows)
        {
            var last = row.Cells.SingleOrDefault(c => c.Index == ColumnCount - 1);
            Assert.IsNotNull(last, $"row {row.Index} has the cell of the column in view");
            Assert.AreEqual((ColumnCount - 1) * ColumnWidth, last!.ActualOffset.X, 0.5, $"row {row.Index}: in its own place");
        }
    }

    [UITestMethod]
    public async Task OffByDefault_EveryColumnHasACell()
    {
        var tableView = await CreateTableViewAsync(virtualize: false);
        Assert.IsFalse(new TableView().VirtualizesColumns, "column virtualisation is opt-in");
        Assert.AreEqual(ColumnCount, FirstRowCells(tableView).Length);
    }

    private static async Task SettleAsync(TableView tableView)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(100);
            tableView.UpdateLayout();
        }
    }

    private static StackPanel Panel(TableView tableView)
    {
        var panel = tableView.FindDescendants().OfType<TableViewRowPresenter>().First()
            .FindDescendant<StackPanel>(x => x.Name is "ScrollableCellsPanel");
        Assert.IsNotNull(panel, "Precondition: a row's scrollable cells panel is in the visual tree");
        return panel!;
    }

    private static TableViewCell[] FirstRowCells(TableView tableView) =>
        Panel(tableView).Children.OfType<TableViewCell>().ToArray();

    private static async Task<TableView> CreateTableViewAsync(int alwaysRealize = -1, bool virtualize = true, int itemCount = 2)
    {
        var tableView = new TableView
        {
            Width = 400,
            Height = 300,
            AutoGenerateColumns = false,
            VirtualizesColumns = virtualize,
        };

        for (var i = 0; i < ColumnCount; i++)
        {
            tableView.Columns.Add(new TableViewTextColumn
            {
                Header = $"Column {i}",
                Width = new GridLength(ColumnWidth),
                AlwaysRealizeElement = i == alwaysRealize,
                Binding = new Binding { Path = new PropertyPath(nameof(VirtualTestItem.Name)) }
            });
        }

        tableView.ItemsSource = itemCount == 2
            ? new[] { new VirtualTestItem { Name = "Alpha" }, new VirtualTestItem { Name = "Beta" } }
            : Enumerable.Range(0, itemCount).Select(i => new VirtualTestItem { Name = $"Item {i}" }).ToArray();
        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        tableView.UpdateLayout();
        await SettleAsync(tableView);
        return tableView;
    }

    private sealed class VirtualTestItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
