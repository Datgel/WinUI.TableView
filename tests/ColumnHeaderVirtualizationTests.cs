using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Linq;
using System.Threading.Tasks;
using WinUI.TableView.Extensions;

namespace WinUI.TableView.Tests;

/// <summary>
/// Datgel DH-3839: with <see cref="TableView.VirtualizesColumnHeaders"/> on, only the headers near the horizontal
/// viewport are elements in the header row; the rest keep their column, width and state, and are inserted - in their
/// own place - as they scroll into view. A 178-column table otherwise put 178 header elements into the tree on its
/// first layout, each with a UI Automation provider on Uno's Win32 head.
/// </summary>
[TestClass]
public class ColumnHeaderVirtualizationTests
{
    private const int ColumnCount = 40;
    private const double ColumnWidth = 100;

    [UITestMethod]
    public async Task OnlyTheHeadersNearTheViewport_AreElements_EachInItsOwnPlace()
    {
        var tableView = await CreateTableViewAsync();
        var realised = RealisedHeaders(tableView);

        Assert.IsTrue(realised.Length < ColumnCount, $"only the headers near the viewport are elements ({realised.Length} of {ColumnCount})");
        Assert.IsTrue(realised.Length >= 4, "every header in view is an element");
        Assert.AreSame(tableView.Columns[0].HeaderControl, realised[0], "the first column's header is in view");
        Assert.IsTrue(tableView.Columns.All(c => c.HeaderControl is not null), "every column still has its header");
        Assert.IsFalse(tableView.Columns[ColumnCount - 1].HeaderControl!.IsRealized, "the last column's header is not realised");
        Assert.IsNull(VisualTreeHelper.GetParent(tableView.Columns[ColumnCount - 1].HeaderControl!), "and is in no panel");
        foreach (var header in realised)
        {
            var index = tableView.Columns.IndexOf(header.Column!);
            Assert.AreEqual(index * ColumnWidth, X(header, tableView), 0.5, $"header {index} sits where its column starts");
        }

        Assert.IsTrue(tableView.Columns.All(c => System.Math.Abs(c.ActualWidth - ColumnWidth) < 0.5),
            "every column has its width, realised header or not");
        Assert.IsTrue(ScrollablePanel(tableView).ActualWidth >= ColumnCount * ColumnWidth - 0.5, "the header panel is as wide as all its columns");
    }

    [UITestMethod]
    public async Task ScrollingSideways_RealisesTheHeadersThatComeIntoView()
    {
        var tableView = await CreateTableViewAsync();
        var last = tableView.Columns[ColumnCount - 1].HeaderControl!;
        Assert.IsFalse(last.IsRealized, "Precondition: the last header is not realised");

        tableView.SetValue(TableView.HorizontalOffsetProperty, (ColumnCount - 4) * ColumnWidth);
        await SettleAsync(tableView);

        Assert.IsTrue(last.IsRealized, "the last header is an element once it is in view");
        Assert.AreSame(last, tableView.Columns[ColumnCount - 1].HeaderControl, "the same header object, not a new one");
        Assert.IsTrue(VisualTreeHelper.GetChildrenCount(last) > 0, "and is templated");
        Assert.IsTrue(tableView.Columns[0].HeaderControl!.IsRealized, "a realised header is kept when it scrolls away");
        var realised = RealisedHeaders(tableView);
        CollectionAssert.AreEqual(realised.Select(h => tableView.Columns.IndexOf(h.Column!)).OrderBy(i => i).ToArray(),
            realised.Select(h => tableView.Columns.IndexOf(h.Column!)).ToArray(), "the realised headers stay in column order");
        foreach (var header in realised)
        {
            var index = tableView.Columns.IndexOf(header.Column!);
            Assert.AreEqual(index * ColumnWidth, X(header, tableView), 0.5, $"header {index} sits where its column starts");
        }
    }

    [UITestMethod]
    public async Task ResizingAColumn_MovesTheRealisedHeadersAfterIt()
    {
        var tableView = await CreateTableViewAsync();
        tableView.SetValue(TableView.HorizontalOffsetProperty, 20 * ColumnWidth);
        await SettleAsync(tableView);
        var header21 = tableView.Columns[21].HeaderControl!;
        Assert.IsTrue(header21.IsRealized, "Precondition: column 21 is in view");

        tableView.Columns[2].Width = new GridLength(ColumnWidth + 50); // a realised header, far to the left of the view
        await SettleAsync(tableView);

        Assert.AreEqual(21 * ColumnWidth + 50, X(header21, tableView), 0.5, "a column widened before it moves the header along");
        tableView.Columns[10].Width = new GridLength(ColumnWidth + 30); // an UNREALISED header: its width is a gap
        await SettleAsync(tableView);
        Assert.IsFalse(tableView.Columns[10].HeaderControl!.IsRealized, "Precondition: column 10 is not realised");
        Assert.AreEqual(ColumnWidth + 30, tableView.Columns[10].ActualWidth, 0.5, "an unrealised header still sets its column's width");
        Assert.AreEqual(21 * ColumnWidth + 80, X(header21, tableView), 0.5, "and the gap in front of the next realised header follows it");
    }

    [UITestMethod]
    public async Task FrozenAndAutoWidthColumns_AreAlwaysRealised()
    {
        var tableView = await CreateTableViewAsync(autoWidth: ColumnCount - 1, frozen: 1);

        Assert.IsTrue(tableView.Columns[0].HeaderControl!.IsRealized, "a frozen header never scrolls, so it is always an element");
        Assert.IsTrue(tableView.Columns[ColumnCount - 1].HeaderControl!.IsRealized, "an auto-width column is measured from its header");
        Assert.IsTrue(tableView.Columns[ColumnCount - 1].ActualWidth > 0, "and has a width");
        Assert.IsFalse(tableView.Columns[ColumnCount - 2].HeaderControl!.IsRealized, "its off-screen neighbour is still not realised");
    }

    [UITestMethod]
    public async Task AnOffscreenColumnMadeAutoWidth_IsRealisedAndMeasuredFromItsHeader()
    {
        var tableView = await CreateTableViewAsync();
        var column = tableView.Columns[ColumnCount - 5];
        Assert.IsFalse(column.HeaderControl!.IsRealized, "Precondition: the column's header is not realised");

        column.Width = GridLength.Auto;
        await SettleAsync(tableView);

        Assert.IsTrue(column.HeaderControl!.IsRealized, "an auto-width column is measured from its header, so it is realised");
        Assert.IsTrue(column.HeaderControl.CachedDesiredWidth > 0, "and its header was measured");
        Assert.IsTrue(column.ActualWidth > 0, "and the column has a width");
    }

    [UITestMethod]
    public async Task AnAlwaysRealizedColumn_HasItsHeaderRealisedWhereverItIs()
    {
        var tableView = await CreateTableViewAsync(alwaysRealize: ColumnCount - 1);

        Assert.IsTrue(tableView.Columns[ColumnCount - 1].HeaderControl!.IsRealized,
            "a column whose cells always exist has its header too, so every cell's header item is in the tree");
        Assert.IsFalse(tableView.Columns[ColumnCount - 2].HeaderControl!.IsRealized, "its off-screen neighbour is still not realised");
    }

    [UITestMethod]
    public async Task AnOffscreenCellsPeer_NamesItsHeaderItem_RealisingTheHeader()
    {
        var tableView = await CreateTableViewAsync(virtualizeCells: true);
        var column = tableView.Columns[ColumnCount - 3];
        Assert.IsFalse(column.HeaderControl!.IsRealized, "Precondition: the column's header is not realised");
        var row = tableView.ContainerFromIndex(0) as TableViewRow;
        Assert.IsNotNull(row, "Precondition: the first row is realised");
        var cell = row!.RealizeCell(column);
        Assert.IsNotNull(cell, "Precondition: the off-screen cell can be created by column");

        var peer = FrameworkElementAutomationPeer.FromElement(cell!) ?? FrameworkElementAutomationPeer.CreatePeerForElement(cell!);
        var tableItem = peer!.GetPattern(PatternInterface.TableItem) as ITableItemProvider;
        Assert.IsNotNull(tableItem, "Precondition: the cell peer is a table item");

        Assert.AreEqual(1, tableItem!.GetColumnHeaderItems().Length, "the cell names its column's header item");
        Assert.IsTrue(column.HeaderControl.IsRealized, "which is realised so that the item named is in the tree");
        await SettleAsync(tableView);
        Assert.AreEqual((ColumnCount - 3) * ColumnWidth, X(column.HeaderControl, tableView), 0.5, "in its own place");
    }

    [UITestMethod]
    public async Task HidingAndReorderingColumns_KeepsTheRealisedHeadersInOrder()
    {
        var tableView = await CreateTableViewAsync();

        tableView.Columns[1].Visibility = Visibility.Collapsed;
        await SettleAsync(tableView);
        Assert.IsNull(tableView.Columns[1].HeaderControl, "a hidden column has no header");

        tableView.Columns.Move(0, 3);
        await SettleAsync(tableView);

        var visible = tableView.Columns.VisibleColumns;
        var realised = RealisedHeaders(tableView);
        CollectionAssert.AreEqual(realised.Select(h => visible.IndexOf(h.Column!)).OrderBy(i => i).ToArray(),
            realised.Select(h => visible.IndexOf(h.Column!)).ToArray(), "the realised headers follow the columns' order");
        foreach (var header in realised)
        {
            var index = visible.IndexOf(header.Column!);
            Assert.AreEqual(index * ColumnWidth, X(header, tableView), 0.5, $"header of visible column {index} sits where its column starts");
        }

        Assert.IsTrue(visible.All(c => c.HeaderControl is not null), "every visible column has its header");
    }

    [UITestMethod]
    public async Task OnlyRealisedHeaders_AreReportedToUIAutomation()
    {
        var tableView = await CreateTableViewAsync();
        var peer = FrameworkElementAutomationPeer.FromElement(tableView) ?? FrameworkElementAutomationPeer.CreatePeerForElement(tableView);
        Assert.IsNotNull(peer, "Precondition: the table has a peer");

        var table = peer!.GetPattern(PatternInterface.Table) as ITableProvider;
        Assert.IsNotNull(table, "Precondition: the table peer is a table provider");
        var headers = table!.GetColumnHeaders();

        Assert.AreEqual(RealisedHeaders(tableView).Length, headers.Length, "one header item per realised header, none for the rest");
    }

    [UITestMethod]
    public async Task TurningVirtualisationOff_RealisesEveryHeader()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(tableView.Columns.Any(c => c.HeaderControl is { IsRealized: false }), "Precondition: something is not realised");

        tableView.VirtualizesColumnHeaders = false;
        await SettleAsync(tableView);

        Assert.IsTrue(tableView.Columns.All(c => c.HeaderControl is { IsRealized: true }), "every header is an element with virtualisation off");
        Assert.AreEqual(ColumnCount, RealisedHeaders(tableView).Length, "one per column");
        Assert.IsTrue(RealisedHeaders(tableView).All(h => h.Margin == default), "with no gaps");
    }

    [UITestMethod]
    public async Task VirtualisationIsOptIn()
    {
        Assert.IsFalse(new TableView().VirtualizesColumnHeaders, "column header virtualisation is opt-in");

        var tableView = await CreateTableViewAsync(virtualize: false);
        Assert.AreEqual(ColumnCount, RealisedHeaders(tableView).Length, "without it every column's header is an element");
    }

    private static double X(FrameworkElement header, TableView tableView) =>
        header.TransformToVisual(ScrollablePanel(tableView)).TransformPoint(default).X;

    private static TableViewColumnHeader[] RealisedHeaders(TableView tableView) =>
        [.. tableView.FindDescendant<TableViewHeaderRow>()!.Headers.Where(h => !h.Column!.IsFrozen)];

    private static StackPanel ScrollablePanel(TableView tableView) =>
        tableView.FindDescendant<StackPanel>(p => p.Name is "ScrollableHeadersPanel")!;

    private static async Task SettleAsync(TableView tableView)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(100);
            tableView.UpdateLayout();
        }
    }

    private static async Task<TableView> CreateTableViewAsync(int autoWidth = -1, int frozen = 0, bool virtualize = true, int alwaysRealize = -1, bool virtualizeCells = false)
    {
        var tableView = new TableView
        {
            Width = 400,
            Height = 300,
            AutoGenerateColumns = false,
            VirtualizesColumnHeaders = virtualize,
            VirtualizesColumns = virtualizeCells,
            FrozenColumnCount = frozen,
        };

        for (var i = 0; i < ColumnCount; i++)
        {
            tableView.Columns.Add(new TableViewTextColumn
            {
                Header = $"Column {i}",
                Width = i == autoWidth ? GridLength.Auto : new GridLength(ColumnWidth),
                AlwaysRealizeElement = i == alwaysRealize,
                Binding = new Binding { Path = new PropertyPath(nameof(HeaderTestItem.Name)) }
            });
        }

        tableView.ItemsSource = new[] { new HeaderTestItem { Name = "Alpha" }, new HeaderTestItem { Name = "Beta" } };
        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);
        await SettleAsync(tableView);
        return tableView;
    }

    private sealed class HeaderTestItem
    {
        public string Name { get; set; } = string.Empty;
    }
}
