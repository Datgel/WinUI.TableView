// Datgel fork (DH-2825): Windows-only, as when the fork's tests had their own Windows test project.
#if WINDOWS
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

    /// <summary>The bookkeeping fault on its own: a shown row that the table's row list lost (a recycled container is
    /// re-listed only on a deferred Prepare callback, after the synchronous Clear took it out).</summary>
    [UITestMethod]
    public async Task AClick_OnAShownRowTheRowListLost_MakesTheClickedCellCurrent()
    {
        var (tableView, _) = await CreateTableViewAsync();
        var rows = RowList(tableView);
        var lost = rows.First(r => r.Index == 1);
        rows.Remove(lost);

        AssertEveryRealisedCellClicksCurrent(tableView, "row 1 not in _rows");
    }

    /// <summary>The other fault: a shown row whose cached position is where it USED to be (its ArrangeOverride skipped
    /// UpdatePosition while it was unlinked, or the panel moved it without re-arranging it).</summary>
    [UITestMethod]
    public async Task AClick_OnARowWhoseCachedPositionIsStale_MakesTheClickedCellCurrent()
    {
        var (tableView, _) = await CreateTableViewAsync();
        var rows = RowList(tableView);
        var row0 = rows.First(r => r.Index == 0);
        var row2 = rows.First(r => r.Index == 2);
        (row0.Position, row2.Position) = (row2.Position, row0.Position);

        AssertEveryRealisedCellClicksCurrent(tableView, "rows 0 and 2 with swapped cached positions");
    }

    // Datgel DH-6303: the SELECTION a click makes. The press runs a drag selection (MakeSelectionInDragRect ->
    // GetRowsInDragRect) BEFORE the release sets the current cell, and 1.5.0.17 placed rows there from the stale cached
    // Position: one click after a reload selected the stale row as well, so a single click made a block (Hub UI run
    // 44733256022, the selection-toolbar test). These click the way the press and the release do and assert the click
    // selected exactly the clicked cell, and that it is current.

    [UITestMethod]
    public async Task AClick_WithNoReload_SelectsOnlyTheClickedCell()
    {
        var (tableView, _) = await CreateTableViewAsync();
        await AssertEveryRealisedCellClickSelectsOnlyItselfAsync(tableView, "no reload");
    }

    [UITestMethod]
    public async Task AClick_AfterTheRowsWereClearedAndReAdded_SelectsOnlyTheClickedCell()
    {
        var (tableView, items) = await CreateTableViewAsync();

        items.Clear();
        await Task.Yield();
        foreach (var item in NewItems("b"))
            items.Add(item);
        await SettleAsync(tableView);

        await AssertEveryRealisedCellClickSelectsOnlyItselfAsync(tableView, "after Clear + re-add");
    }

    [UITestMethod]
    public async Task AClick_AfterTheItemsSourceWasReplaced_SelectsOnlyTheClickedCell()
    {
        var (tableView, _) = await CreateTableViewAsync();

        tableView.ItemsSource = new ObservableCollection<Item>(NewItems("d"));
        await SettleAsync(tableView);

        await AssertEveryRealisedCellClickSelectsOnlyItselfAsync(tableView, "after ItemsSource replaced");
    }

    [UITestMethod]
    public async Task AClick_AfterSeveralReloads_SelectsOnlyTheClickedCell()
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

        await AssertEveryRealisedCellClickSelectsOnlyItselfAsync(tableView, "after three reloads");
    }

    /// <summary>The fault on its own, so the drag selection is pinned on WinAppSDK too (where a reload alone does not
    /// make the cache stale, DH-6234): rows 0 and 2 with swapped cached positions.</summary>
    [UITestMethod]
    public async Task AClick_OnARowWhoseCachedPositionIsStale_SelectsOnlyTheClickedCell()
    {
        var (tableView, _) = await CreateTableViewAsync();
        var rows = RowList(tableView);
        var row0 = rows.First(r => r.Index == 0);
        var row2 = rows.First(r => r.Index == 2);

        await AssertEveryRealisedCellClickSelectsOnlyItselfAsync(tableView, "rows 0 and 2 with swapped cached positions",
            beforeEachClick: () => (row0.Position, row2.Position) = (row2.Position, row0.Position));
    }

    /// <summary>Clicks the centre of every column of every realised row that is fully in view, running what a press does
    /// (OnAnyPointerPressed: anchor on the pressed cell, start the drag, select what the drag rectangle covers) and then
    /// the release, and asserts the selection is exactly that cell and it is current. <paramref name="beforeEachClick"/>
    /// re-applies an injected fault, since every hit-test may refresh the cache it corrupts.</summary>
    private static async Task AssertEveryRealisedCellClickSelectsOnlyItselfAsync(TableView tableView, string when, System.Action? beforeEachClick = null)
    {
        var canvas = tableView.DragRectangleCanvas;
        Assert.IsNotNull(canvas, "Precondition: the template has its drag canvas");
        Assert.IsTrue(tableView.ShowDragRectangle, "Precondition: the drag rectangle is shown (the cell drag selection needs it)");

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var makeSelection = typeof(TableView).GetMethod("MakeSelectionInDragRect", flags)!;
        var lastCellRange = typeof(TableView).GetField("_lastDragSelectionCellRange", flags)!;
        var lastRowRange = typeof(TableView).GetField("_lastDragSelectionRowRange", flags)!;

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
                beforeEachClick?.Invoke();

                // The press, as OnAnyPointerPressed runs it for a cell in a cell-selection table.
                tableView.CurrentCellSlot = null;
                lastCellRange.SetValue(tableView, null);
                lastRowRange.SetValue(tableView, null);
                tableView.SelectionStartCellSlot = expected;
                tableView.SelectionStartRowIndex = expected.Row;
                tableView.LastSelectionUnit = TableViewSelectionUnit.Cell;
                tableView.StartDragSelection(centre);
                makeSelection.Invoke(tableView, null);
                // A mouse that does not move still reports a move at the press point.
                tableView.UpdateDragRectangleVisual(centre);
                makeSelection.Invoke(tableView, null);
                // The release.
                tableView.EndDragSelection();

                // The drag selection is applied on a queued callback.
                await Task.Delay(50);

                var selected = tableView.SelectedCells.OrderBy(s => s.Row).ThenBy(s => s.Column).ToList();
                if (selected.Count != 1 || selected[0] != expected || tableView.CurrentCellSlot != expected)
                {
                    misses.Add($"row {row.Index} col {cell.Index} at y={centre.Y:0.#}: selected [{string.Join(" ", selected.Select(s => Describe(s)))}], current {Describe(tableView.CurrentCellSlot)}");
                }
            }
        }

        if (misses.Count > 0)
        {
            Assert.Fail($"{when}: {misses.Count} click(s) did not select exactly the clicked cell:\n  "
                + string.Join("\n  ", misses)
                + "\n" + DescribeBookkeeping(tableView, canvas!));
        }
    }

    private static List<TableViewRow> RowList(TableView tableView) =>
        (List<TableViewRow>)typeof(TableView)
            .GetField("_rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(tableView)!;

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
#endif
