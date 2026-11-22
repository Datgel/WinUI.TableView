using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace WinUI.TableView.Tests;

/// <summary>
/// Datgel DH-6363 — making the WHOLE top visible row of a scrolled table current must not scroll it. The header row sits
/// above the content viewer (its own grid row in the template), but <see cref="TableView.ScrollRowIntoView(int)"/> took
/// any row in the viewer's first <see cref="TableView.HeaderRowHeight"/> DIPs as hidden and scrolled up about one row
/// (Hub UI run 44922704618: 46 DIP on a row wholly in view). <c>TableView.RevealRowOffset</c> is the rule; the UI
/// tests run it through the real control.
/// </summary>
[TestClass]
public class ScrollRowIntoViewTopRowTests
{
    [UITestMethod]
    public async Task Making_the_whole_top_visible_row_current_does_not_scroll_the_table()
    {
        var tableView = await CreateScrolledTableViewAsync();
        var sv = ScrollViewerOf(tableView);
        var (index, top) = TopVisibleRow(tableView, sv);
        Assert.IsTrue(index > 0, $"the table must be scrolled for this test (top row {index}).");
        Assert.IsTrue(top >= -0.5, $"control: row {index} must be WHOLE at the top of the viewer (its top reads {top}).");
        var before = sv.VerticalOffset;

        await tableView.ScrollRowIntoView(index);
        await SettleAsync();

        Assert.AreEqual(before, sv.VerticalOffset, 0.5, $"making row {index} current scrolled the table (DH-6363).");
    }

    /// <summary>Negative control on the same table: a row partly above the viewer IS scrolled into view.</summary>
    [UITestMethod]
    public async Task A_row_partly_above_the_viewer_is_scrolled_into_view()
    {
        var tableView = await CreateScrolledTableViewAsync(extra: 20);
        var sv = ScrollViewerOf(tableView);
        var (index, top) = TopVisibleRow(tableView, sv);
        Assert.IsTrue(top < -1, $"control: row {index} must be partly above the viewer (its top reads {top}).");

        var row = await tableView.ScrollRowIntoView(index);
        await SettleAsync();

        var after = row!.TransformToVisual(sv).TransformPoint(new Point(0, 0)).Y;
        Assert.IsTrue(after >= -0.5, $"row {index}'s top is still above the viewer ({after}).");
    }

    private static async Task<TableView> CreateScrolledTableViewAsync(double extra = 0)
    {
        var items = Enumerable.Range(0, 60).Select(i => new Item { Name = $"n{i}" }).ToList();
        var tableView = new TableView
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            SelectionUnit = TableViewSelectionUnit.Cell,
            Height = 400,
        };
        tableView.Columns.Add(new TableViewTextColumn
        {
            Header = "Name",
            Binding = new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(Item.Name)) },
            Width = new GridLength(120),
        });
        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        await SettleAsync();

        var sv = ScrollViewerOf(tableView);
        var firstRow = (TableViewRow)tableView.ContainerFromIndex(0);
        var rowHeight = firstRow.ActualHeight;
        sv.ChangeView(null, rowHeight * 5 + extra, null, disableAnimation: true);
        await SettleAsync();
        return tableView;
    }

    private static ScrollViewer ScrollViewerOf(TableView tableView) =>
        (ScrollViewer)typeof(TableView).GetField("_scrollViewer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(tableView)!;

    /// <summary>The first realised row whose bottom is inside the viewer, and its top in the viewer.</summary>
    private static (int Index, double Top) TopVisibleRow(TableView tableView, ScrollViewer sv)
    {
        for (var i = 0; i < tableView.Items.Count; i++)
        {
            if (tableView.ContainerFromIndex(i) is not TableViewRow { ActualHeight: > 0 } row)
                continue;
            var top = row.TransformToVisual(sv).TransformPoint(new Point(0, 0)).Y;
            if (top + row.ActualHeight <= 0.5)
                continue;
            return (i, top);
        }
        throw new AssertFailedException("No visible row.");
    }

    private static async Task SettleAsync()
    {
        for (var i = 0; i < 5; i++)
            await Task.Delay(50);
    }

    public sealed class Item
    {
        public string? Name { get; set; }
    }
}
