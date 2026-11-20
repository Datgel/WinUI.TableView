using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using WinUI.TableView.Extensions;

namespace WinUI.TableView.Tests;

/// <summary>
/// Datgel DH-6234 — a click on a cell after the rows were reloaded must make that cell current.
///
/// <para>A left press clears <see cref="TableView.CurrentCellSlot"/> (it may start a drag selection) and the release
/// sets it again from the row the table hit-tests under the pointer (<c>EndDragSelection</c> →
/// <c>GetSlotAtCanvasPoint</c>), which walks the table's own list of realised rows and their cached positions. Hub
/// measured that straight after its rows were cleared and re-added, most clicks found no row there, so the click left
/// NO current cell (Hub UI runs 44629726609 / 44641934849). These tests click — press point and release point are the
/// centre of a realised cell, as a mouse click is — and assert the cell the pointer was on is current.</para>
/// </summary>
[TestClass]
public class RowReloadClickTests
{
    private const int ItemCount = 30;

    public sealed class Item
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    private static List<Item> NewItems(string tag) =>
        Enumerable.Range(0, ItemCount).Select(i => new Item { Name = $"{tag}{i}", Value = $"v{i}" }).ToList();

    private static async Task<(TableView, ObservableCollection<Item>)> CreateTableViewAsync()
    {
        var items = new ObservableCollection<Item>(NewItems("a"));
        var tableView = new TableView
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            SelectionMode = ListViewSelectionMode.Extended,
            SelectionUnit = TableViewSelectionUnit.Cell,
            Height = 400,
        };
        tableView.Columns.Add(new TableViewTextColumn { Header = "Name", Binding = new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(Item.Name)) }, Width = new GridLength(120) });
        tableView.Columns.Add(new TableViewTextColumn { Header = "Value", Binding = new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(Item.Value)) }, Width = new GridLength(120) });

        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        await SettleAsync(tableView);
        return (tableView, items);
    }

    [UITestMethod]
    public async Task AClick_WithNoReload_MakesTheClickedCellCurrent()
    {
        var (tableView, _) = await CreateTableViewAsync();
        AssertEveryRealisedCellClicksCurrent(tableView, "no reload");
    }

    /// <summary>What Hub does on a reload (DataManagementViewModel.LoadRowsCoreAsync): Rows.Clear(), an await, then the
    /// re-read rows added one by one — new instances.</summary>
    [UITestMethod]
    public async Task AClick_AfterTheRowsWereClearedAndReAdded_MakesTheClickedCellCurrent()
    {
        var (tableView, items) = await CreateTableViewAsync();

        items.Clear();
        await Task.Yield();
        foreach (var item in NewItems("b"))
            items.Add(item);
        await SettleAsync(tableView);

        AssertEveryRealisedCellClicksCurrent(tableView, "after Clear + re-add");
    }

    [UITestMethod]
    public async Task AClick_AfterTheRowsWereClearedAndReAddedInTheSameTurn_MakesTheClickedCellCurrent()
    {
        var (tableView, items) = await CreateTableViewAsync();

        items.Clear();
        foreach (var item in NewItems("c"))
            items.Add(item);
        await SettleAsync(tableView);

        AssertEveryRealisedCellClicksCurrent(tableView, "after Clear + re-add in one turn");
    }

    [UITestMethod]
    public async Task AClick_AfterTheItemsSourceWasReplaced_MakesTheClickedCellCurrent()
    {
        var (tableView, _) = await CreateTableViewAsync();

        tableView.ItemsSource = new ObservableCollection<Item>(NewItems("d"));
        await SettleAsync(tableView);

        AssertEveryRealisedCellClicksCurrent(tableView, "after ItemsSource replaced");
    }

    [UITestMethod]
    public async Task AClick_AfterSeveralReloads_MakesTheClickedCellCurrent()
    {
        var (tableView, items) = await CreateTableViewAsync();

        for (var pass = 0; pass < 3; pass++)
        {
            items.Clear();
            await Task.Yield();
            foreach (var item in NewItems($"e{pass}-"))
                items.Add(item);
            await SettleAsync(tableView);
        }

        AssertEveryRealisedCellClicksCurrent(tableView, "after three reloads");
    }

    /// <summary>Clicks the centre of every column of every realised row that is fully in view and asserts that cell is
    /// current after the release. A failure names every row that missed and the table's row bookkeeping.</summary>
    private static void AssertEveryRealisedCellClicksCurrent(TableView tableView, string when)
    {
        var canvas = tableView.DragRectangleCanvas;
        Assert.IsNotNull(canvas, "Precondition: the template has its drag canvas");

        var shown = tableView.FindDescendants().OfType<TableViewRow>()
            .Where(r => r.Index >= 0 && r.Visibility == Visibility.Visible && r.ActualHeight > 0)
            .Where(r => IsFullyInView(tableView, r))
            .OrderBy(r => r.Index)
            .ToList();
        Assert.IsTrue(shown.Count >= 3, $"Precondition ({when}): at least three rows are in view, found {shown.Count}");

        var misses = new List<string>();
        foreach (var row in shown)
        {
            foreach (var cell in row.Cells.Where(c => c.ActualWidth > 0))
            {
                var centre = cell.TransformToVisual(canvas).TransformPoint(new Point(cell.ActualWidth / 2, cell.ActualHeight / 2));
                var expected = new TableViewCellSlot(row.Index, cell.Index);

                // The press (OnAnyPointerPressed) clears the current cell and starts the drag; the release ends it.
                tableView.CurrentCellSlot = null;
                tableView.StartDragSelection(centre);
                tableView.EndDragSelection();

                if (tableView.CurrentCellSlot != expected)
                {
                    misses.Add($"row {row.Index} col {cell.Index} at y={centre.Y:0.#}: current {Describe(tableView.CurrentCellSlot)}");
                }
            }
        }

        if (misses.Count > 0)
        {
            Assert.Fail($"{when}: {misses.Count} click(s) did not make the clicked cell current:\n  "
                + string.Join("\n  ", misses)
                + "\n" + DescribeBookkeeping(tableView, canvas!));
        }
    }

    private static bool IsFullyInView(TableView tableView, TableViewRow row)
    {
        var top = row.TransformToVisual(tableView).TransformPoint(default).Y;
        return top >= 0 && top + row.ActualHeight <= tableView.ActualHeight;
    }

    private static string Describe(TableViewCellSlot? slot) => slot is { } s ? $"({s.Row},{s.Column})" : "none";

    private static string DescribeBookkeeping(TableView tableView, Microsoft.UI.Xaml.Controls.Canvas canvas)
    {
        var rows = (IReadOnlyList<TableViewRow>)typeof(TableView)
            .GetField("_rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(tableView)!;
        var shown = tableView.FindDescendants().OfType<TableViewRow>().ToList();
        var lines = new List<string> { $"_rows: {rows.Count} entries; rows in the visual tree: {shown.Count}" };
        foreach (var row in rows)
        {
            string live;
            try { live = row.TransformToVisual(canvas).TransformPoint(default).Y.ToString("0.#"); }
            catch { live = "n/a"; }
            lines.Add($"  _rows: index {row.Index} cachedY={row.Position.Y:0.#} liveY={live} h={row.ActualHeight:0.#} tableView={(row.TableView is null ? "null" : "set")} inTree={shown.Contains(row)}");
        }
        foreach (var row in shown.Where(r => !rows.Contains(r)))
            lines.Add($"  NOT in _rows: index {row.Index} cachedY={row.Position.Y:0.#} h={row.ActualHeight:0.#}");
        return string.Join("\n", lines);
    }

    private static async Task SettleAsync(TableView tableView)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(100);
            tableView.UpdateLayout();
        }
    }
}
