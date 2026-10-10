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
/// Datgel DH-2718: a cell's Height, MinHeight and MaxHeight are SET from the table's row heights and pushed when those
/// change, instead of three RelativeSource bindings per cell. On a 177-column row those were 531 bindings, ~16 % of
/// opening a wide table.
/// </summary>
[TestClass]
public class PushedRowHeightTests
{
    private const int ColumnCount = 12;

    [UITestMethod]
    public async Task Cells_TakeTheRowHeights_WithoutBindings()
    {
        var tableView = await CreateTableViewAsync();
        tableView.RowMinHeight = 44;
        tableView.RowMaxHeight = 120;
        tableView.RowHeight = 60;
        tableView.UpdateLayout();

        var cells = Cells(tableView);
        Assert.AreEqual(2 * ColumnCount, cells.Length, "Precondition: both rows are realised with every cell");
        foreach (var cell in cells)
        {
            Assert.AreEqual(60, cell.Height, $"cell {cell.Index} takes RowHeight");
            Assert.AreEqual(44, cell.MinHeight, $"cell {cell.Index} takes RowMinHeight");
            Assert.AreEqual(120, cell.MaxHeight, $"cell {cell.Index} takes RowMaxHeight");
            Assert.IsNull(cell.GetBindingExpression(FrameworkElement.HeightProperty), "no per-cell Height binding");
            Assert.IsNull(cell.GetBindingExpression(FrameworkElement.MinHeightProperty), "no per-cell MinHeight binding");
            Assert.IsNull(cell.GetBindingExpression(FrameworkElement.MaxHeightProperty), "no per-cell MaxHeight binding");
        }
    }

    [UITestMethod]
    public async Task ARowHeightChange_ReachesEveryRealisedCell()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(Cells(tableView).All(c => double.IsNaN(c.Height)), "Precondition: the default RowHeight is Auto");
        Assert.IsTrue(Cells(tableView).All(c => c.MinHeight == tableView.RowMinHeight), "Precondition: cells take the default RowMinHeight");

        tableView.RowHeight = 72; // Hub's row-height grip (DH-806) sets RowHeight on an open table
        tableView.UpdateLayout();
        Assert.IsTrue(Cells(tableView).All(c => c.Height == 72), "a RowHeight set after the open reaches every cell");
        Assert.IsTrue(Cells(tableView).All(c => c.ActualHeight == 72), "and is laid out");

        tableView.RowHeight = double.NaN;
        tableView.RowMinHeight = 50;
        tableView.UpdateLayout();
        Assert.IsTrue(Cells(tableView).All(c => double.IsNaN(c.Height)), "back to Auto");
        Assert.IsTrue(Cells(tableView).All(c => c.MinHeight == 50), "a RowMinHeight change reaches every cell");
    }

    private static TableViewCell[] Cells(TableView tableView) =>
        tableView.FindDescendants().OfType<TableViewRowPresenter>()
            .SelectMany(p => p.Cells)
            .ToArray();

    private static async Task<TableView> CreateTableViewAsync()
    {
        var tableView = new TableView
        {
            Width = 600,
            Height = 400,
            AutoGenerateColumns = false,
            DefersOffscreenCellElements = false,
        };

        for (var i = 0; i < ColumnCount; i++)
        {
            tableView.Columns.Add(new TableViewTextColumn
            {
                Header = $"Column {i}",
                Width = new GridLength(80),
                Binding = new Binding { Path = new PropertyPath(nameof(HeightTestItem.Name)) }
            });
        }

        tableView.ItemsSource = new[] { new HeightTestItem { Name = "Alpha" }, new HeightTestItem { Name = "Beta" } };
        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        tableView.UpdateLayout();
        return tableView;
    }

    private sealed class HeightTestItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
