using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Linq;
using System.Threading.Tasks;
using WinUI.TableView.Extensions;
using WinUI.TableView.Helpers;

namespace WinUI.TableView.Tests;

/// <summary>
/// Datgel DH-2231: a cell scrolled out of the horizontal viewport keeps its place but does not build its element
/// (<see cref="TableViewColumn.GenerateElement"/>) until it comes into view. Building every cell's element is what still
/// held a 177-column table's first layout for 4.5 s after DH-2215 hid the off-screen cells from the renderer.
/// </summary>
[TestClass]
public class DeferredCellElementTests
{
    private const int ColumnCount = 40;
    private const double ColumnWidth = 100;

    [UITestMethod]
    public async Task OffscreenCells_DeferTheirElement_AndKeepTheirPlace()
    {
        var tableView = await CreateTableViewAsync();
        var cells = Cells(tableView);

        Assert.AreEqual(ColumnCount, cells.Length, "every column still has a cell, so index-by-column holds");
        Assert.IsNotNull(cells[0].Content, "the first cell is on screen and has its element");
        Assert.IsFalse(cells[0].IsElementDeferred);
        Assert.IsTrue(cells[ColumnCount - 1].IsElementDeferred, "the last cell is far off screen and has deferred its element");
        Assert.IsNull(cells[ColumnCount - 1].Content, "a deferred cell has no element");
        Assert.AreEqual(ColumnWidth, cells[ColumnCount - 1].ActualWidth, 0.5, "a deferred cell still takes its column's width");
        for (var i = 0; i < ColumnCount; i++)
        {
            Assert.AreEqual(i, cells[i].Index, $"cell {i} keeps its column index");
            var inView = HorizontalCulling.IsInView(cells[i].ActualOffset.X, cells[i].ActualWidth, tableView.HorizontalOffset, tableView.ActualWidth);
            Assert.AreEqual(!inView, cells[i].IsElementDeferred, $"cell {i} (in view: {inView})");
        }
    }

    [UITestMethod]
    public async Task Scrolling_BuildsTheElementsThatComeIntoView()
    {
        var tableView = await CreateTableViewAsync();
        var cells = Cells(tableView);
        Assert.IsTrue(cells[ColumnCount - 1].IsElementDeferred, "Precondition: the last cell is deferred");

        tableView.SetValue(TableView.HorizontalOffsetProperty, (ColumnCount - 4) * ColumnWidth);
        tableView.UpdateLayout();

        Assert.IsFalse(cells[ColumnCount - 1].IsElementDeferred, "the last cell has scrolled into view");
        Assert.IsNotNull(cells[ColumnCount - 1].Content, "and has its element");
        Assert.IsNotNull(cells[0].Content, "an element already built is kept when its cell scrolls away");
    }

    [UITestMethod]
    public async Task AnAlwaysRealizedColumn_BuildsItsElementOffscreen()
    {
        var tableView = await CreateTableViewAsync(alwaysRealize: ColumnCount - 1);
        var cells = Cells(tableView);

        Assert.IsFalse(cells[ColumnCount - 1].IsElementDeferred, "a wrapping column decides the row height, so it is never deferred");
        Assert.IsNotNull(cells[ColumnCount - 1].Content);
        Assert.IsTrue(cells[ColumnCount - 2].IsElementDeferred, "its off-screen neighbour still defers");
    }

    [UITestMethod]
    public async Task AnAutoWidthColumn_BuildsItsElementOffscreen()
    {
        var tableView = await CreateTableViewAsync(autoWidth: ColumnCount - 1);
        var cells = Cells(tableView);

        Assert.IsFalse(cells[ColumnCount - 1].IsElementDeferred, "an auto-width column is measured from its cells' elements");
        Assert.IsNotNull(cells[ColumnCount - 1].Content);
    }

    [UITestMethod]
    public async Task TurningDeferralOff_BuildsEveryElement()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(Cells(tableView).Any(c => c.IsElementDeferred), "Precondition: something is deferred");

        tableView.DefersOffscreenCellElements = false;
        tableView.UpdateLayout();

        Assert.IsTrue(Cells(tableView).All(c => c.Content is not null && !c.IsElementDeferred), "every cell has its element with deferral off");
    }

    [UITestMethod]
    public async Task TheCurrentCell_AlwaysHasItsElement()
    {
        var tableView = await CreateTableViewAsync();
        var cells = Cells(tableView);
        Assert.IsTrue(cells[ColumnCount - 1].IsElementDeferred, "Precondition: the last cell is deferred");

        tableView.CurrentCellSlot = new TableViewCellSlot(0, ColumnCount - 1);
        await SettleAsync(tableView); // the current-cell change is applied after a yield, once scrolled into view

        Assert.IsFalse(cells[ColumnCount - 1].IsElementDeferred, "the current cell is built at once");
        Assert.IsNotNull(cells[ColumnCount - 1].Content);
    }

    [UITestMethod]
    public async Task ADeferredCell_IsStillSelectedByItsSlot()
    {
        var tableView = await CreateTableViewAsync();
        tableView.SelectionUnit = TableViewSelectionUnit.Cell;
        var cells = Cells(tableView);

        Assert.IsTrue(cells[ColumnCount - 2].IsElementDeferred, "Precondition: the cell is deferred");

        tableView.MakeSelection(new TableViewCellSlot(0, ColumnCount - 2), false);
        await SettleAsync(tableView);

        Assert.IsTrue(cells[ColumnCount - 2].IsSelected, "selection is by slot, so a deferred cell is selected like any other");
        Assert.IsNotNull(cells[ColumnCount - 2].Content, "and, now current, it has its element");
    }

    private static async Task SettleAsync(TableView tableView)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(100);
            tableView.UpdateLayout();
        }
    }

    private static TableViewCell[] Cells(TableView tableView)
    {
        var panel = tableView.FindDescendants().OfType<TableViewRowPresenter>().First()
            .FindDescendant<StackPanel>(x => x.Name is "ScrollableCellsPanel");
        Assert.IsNotNull(panel, "Precondition: a row's scrollable cells panel is in the visual tree");
        return panel!.Children.OfType<TableViewCell>().ToArray();
    }

    private static async Task<TableView> CreateTableViewAsync(int alwaysRealize = -1, int autoWidth = -1)
    {
        var tableView = new TableView
        {
            Width = 400,
            Height = 300,
            AutoGenerateColumns = false,
        };

        for (var i = 0; i < ColumnCount; i++)
        {
            tableView.Columns.Add(new TableViewTextColumn
            {
                Header = $"Column {i}",
                Width = i == autoWidth ? GridLength.Auto : new GridLength(ColumnWidth),
                AlwaysRealizeElement = i == alwaysRealize,
                Binding = new Binding { Path = new PropertyPath(nameof(DeferTestItem.Name)) }
            });
        }

        tableView.ItemsSource = new[] { new DeferTestItem { Name = "Alpha" }, new DeferTestItem { Name = "Beta" } };
        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        tableView.UpdateLayout();
        return tableView;
    }

    private sealed class DeferTestItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
