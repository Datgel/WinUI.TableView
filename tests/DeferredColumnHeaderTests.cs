using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Linq;
using System.Threading.Tasks;
using WinUI.TableView.Extensions;
using WinUI.TableView.Helpers;

namespace WinUI.TableView.Tests;

/// <summary>
/// Datgel DH-2252: what remained of a 177-column table's open after DH-2231 was its column headers. Every header was
/// templated, and every template built an options menu - a flyout of eight items with their icons and commands -
/// that nobody had opened. The menu is now built on its first open, and a header out of the horizontal viewport
/// keeps its place and width but has no template until it comes into view.
/// </summary>
[TestClass]
public class DeferredColumnHeaderTests
{
    private const int ColumnCount = 40;
    private const double ColumnWidth = 100;

    [UITestMethod]
    public async Task NoHeader_BuildsItsOptionsMenu_UntilItOpens()
    {
        var tableView = await CreateTableViewAsync();
        var headers = Headers(tableView);

        Assert.IsTrue(headers.All(h => !h.HasOptionsFlyout), "no header has built its options menu before it is opened");
        var button = headers[0].FindDescendant<Button>(b => b.Name is "OptionsButton");
        Assert.IsNotNull(button, "the options button is still a template part, by the same name");
        Assert.IsNull(button!.Flyout, "the default template no longer declares the menu under the button");

        headers[0].ShowOptionsFlyout();
        await SettleAsync(tableView);

        Assert.IsTrue(headers[0].HasOptionsFlyout, "opening the menu builds it");
        Assert.IsFalse(headers[1].HasOptionsFlyout, "and only that header's");
        var flyout = headers[0].EnsureOptionsFlyout()!;
        var names = flyout.Items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n)).ToArray();
        CollectionAssert.IsSubsetOf(
            new[] { "SortAscendingMenuItem", "SortDescendingMenuItem", "ClearSortingMenuItem", "MenuFlyoutSeparator", "ClearFilterMenuItem" },
            names, "the built menu carries the items the template used to declare");
        Assert.IsTrue(flyout.Items.OfType<MenuFlyoutItem>().All(i => i.Command is not null), "every item has its command");
        Assert.IsNotNull(flyout.FlyoutPresenterStyle, "the presenter is styled from the header's OptionsFlyoutPresenterStyle");
        flyout.Hide();
    }

    [UITestMethod]
    public async Task ABuiltMenu_SortsItsColumn()
    {
        var tableView = await CreateTableViewAsync();
        var header = Headers(tableView)[0];

        var sortAscending = header.EnsureOptionsFlyout()!.Items.OfType<MenuFlyoutItem>().First(i => i.Name is "SortAscendingMenuItem");
        sortAscending.Command.Execute(null);

        Assert.AreEqual(SortDirection.Ascending, header.Column!.SortDirection, "the lazily built command sorts the column");
    }

    [UITestMethod]
    public async Task OffscreenHeaders_DeferTheirTemplate_AndKeepTheirPlace()
    {
        var tableView = await CreateTableViewAsync();
        var headers = Headers(tableView);

        Assert.AreEqual(ColumnCount, headers.Length, "every column still has its header");
        Assert.IsFalse(headers[0].IsTemplateDeferred, "the first header is on screen");
        Assert.IsTrue(VisualTreeHelper.GetChildrenCount(headers[0]) > 0, "and is templated");
        Assert.IsTrue(headers[ColumnCount - 1].IsTemplateDeferred, "the last header is far off screen and has no template");
        Assert.IsNull(headers[ColumnCount - 1].FindDescendant<Button>(b => b.Name is "OptionsButton"),
            "a deferred header builds none of its template's parts");
        for (var i = 0; i < ColumnCount; i++)
        {
            Assert.AreEqual(ColumnWidth, headers[i].ActualWidth, 0.5, $"header {i} keeps its column's width");
            Assert.AreEqual(ColumnWidth, headers[i].Column!.ActualWidth, 0.5, $"column {i} keeps its width");
            var inView = HorizontalCulling.IsInView(headers[i].ActualOffset.X, headers[i].ActualWidth, tableView.HorizontalOffset, tableView.ActualWidth);
            Assert.AreEqual(!inView, headers[i].IsTemplateDeferred, $"header {i} (in view: {inView})");
        }
    }

    [UITestMethod]
    public async Task Scrolling_TemplatesTheHeadersThatComeIntoView()
    {
        var tableView = await CreateTableViewAsync();
        var headers = Headers(tableView);
        Assert.IsTrue(headers[ColumnCount - 1].IsTemplateDeferred, "Precondition: the last header is deferred");

        tableView.SetValue(TableView.HorizontalOffsetProperty, (ColumnCount - 4) * ColumnWidth);
        await SettleAsync(tableView);

        Assert.IsFalse(headers[ColumnCount - 1].IsTemplateDeferred, "the last header has scrolled into view");
        Assert.IsTrue(VisualTreeHelper.GetChildrenCount(headers[ColumnCount - 1]) > 0, "and has its template");
        Assert.IsNotNull(headers[ColumnCount - 1].FindDescendant<Button>(b => b.Name is "OptionsButton"), "with its options button");
        Assert.IsFalse(headers[0].IsTemplateDeferred, "a template already applied is kept when its header scrolls away");
    }

    [UITestMethod]
    public async Task AFilteredColumn_ShowsItWhenItsHeaderIsTemplatedLater()
    {
        var tableView = await CreateTableViewAsync();
        var headers = Headers(tableView);
        var last = headers[ColumnCount - 1];
        Assert.IsTrue(last.IsTemplateDeferred, "Precondition: the last header is deferred");

        last.Column!.IsFiltered = true;
        tableView.SetValue(TableView.HorizontalOffsetProperty, (ColumnCount - 4) * ColumnWidth);
        await SettleAsync(tableView);

        var filterIcon = last.FindDescendant<FontIcon>(f => f.Name is "FilterIcon");
        Assert.IsNotNull(filterIcon, "Precondition: the header is templated");
        Assert.AreEqual(Visibility.Visible, filterIcon!.Visibility, "the filter state set while it had no template is shown");
    }

    [UITestMethod]
    public async Task AutoWidthAndFrozenColumns_AreNeverDeferred()
    {
        var tableView = await CreateTableViewAsync(autoWidth: ColumnCount - 1);
        var headers = Headers(tableView);

        Assert.IsFalse(headers[ColumnCount - 1].IsTemplateDeferred, "an auto-width column is measured from its header");
        Assert.IsTrue(headers[ColumnCount - 2].IsTemplateDeferred, "its off-screen neighbour still defers");

        Assert.IsTrue(headers[ColumnCount - 2].ShouldDeferTemplate(), "Precondition: an off-screen scrollable header may defer");
        headers[ColumnCount - 2].Column!.IsFrozen = true;
        Assert.IsFalse(headers[ColumnCount - 2].ShouldDeferTemplate(), "a frozen header never scrolls, so it never defers");
    }

    [UITestMethod]
    public async Task TurningDeferralOff_TemplatesEveryHeader()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(Headers(tableView).Any(h => h.IsTemplateDeferred), "Precondition: something is deferred");

        tableView.DefersOffscreenColumnHeaders = false;
        await SettleAsync(tableView);

        Assert.IsTrue(Headers(tableView).All(h => !h.IsTemplateDeferred && VisualTreeHelper.GetChildrenCount(h) > 0),
            "every header has its template with deferral off");
    }

    private static async Task SettleAsync(TableView tableView)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(100);
            tableView.UpdateLayout();
        }
    }

    private static TableViewColumnHeader[] Headers(TableView tableView) =>
        tableView.Columns.Select(c => c.HeaderControl!).ToArray();

    private static async Task<TableView> CreateTableViewAsync(int autoWidth = -1)
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
