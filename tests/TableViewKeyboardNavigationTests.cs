using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;

namespace WinUI.TableView.Tests;

[TestClass]
public class TableViewKeyboardNavigationTests
{
    [UITestMethod]
    public async Task Enter_moves_down_by_default()
    {
        var tableView = await CreateTableViewAsync();
        await MakeCurrentAsync(tableView, new TableViewCellSlot(0, 0));

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Enter));

        Assert.AreEqual(new TableViewCellSlot(1, 0), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task Enter_moves_right_when_EnterKeyNavigation_is_Right()
    {
        var tableView = await CreateTableViewAsync();
        tableView.EnterKeyNavigation = TableViewEnterKeyNavigation.Right;
        await MakeCurrentAsync(tableView, new TableViewCellSlot(0, 0));

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Enter));

        Assert.AreEqual(new TableViewCellSlot(0, 1), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task Enter_moving_right_wraps_from_the_last_column_to_the_first_cell_of_the_next_row()
    {
        var tableView = await CreateTableViewAsync();
        tableView.EnterKeyNavigation = TableViewEnterKeyNavigation.Right;
        await MakeCurrentAsync(tableView, new TableViewCellSlot(0, 2));

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Enter));

        Assert.AreEqual(new TableViewCellSlot(1, 0), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task Shift_Enter_moving_right_goes_left()
    {
        var tableView = await CreateTableViewAsync();
        tableView.EnterKeyNavigation = TableViewEnterKeyNavigation.Right;
        await MakeCurrentAsync(tableView, new TableViewCellSlot(1, 1));

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Enter, shiftKey: true));

        Assert.AreEqual(new TableViewCellSlot(1, 0), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task Right_arrow_and_Tab_move_to_the_next_cell_to_the_right()
    {
        var tableView = await CreateTableViewAsync();
        await MakeCurrentAsync(tableView, new TableViewCellSlot(0, 0));

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Right));
        Assert.AreEqual(new TableViewCellSlot(0, 1), tableView.CurrentCellSlot);

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Tab));
        Assert.AreEqual(new TableViewCellSlot(0, 2), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task CommitEdit_commits_and_ends_the_edit_leaving_the_cell_current()
    {
        var tableView = await CreateTableViewAsync();
        var ended = new List<TableViewEditAction>();
        tableView.CellEditEnded += (_, e) => ended.Add(e.EditAction);
        var slot = new TableViewCellSlot(1, 1);
        Assert.IsTrue(await tableView.BeginEditAsync(slot));

        Assert.IsTrue(tableView.CommitEdit());

        Assert.IsFalse(tableView.IsEditing);
        Assert.AreEqual(slot, tableView.CurrentCellSlot);
        CollectionAssert.AreEqual(new[] { TableViewEditAction.Commit }, ended);
    }

    [UITestMethod]
    public async Task CommitEdit_keeps_editing_when_the_commit_is_cancelled()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(await tableView.BeginEditAsync(new TableViewCellSlot(0, 0)));
        tableView.CellEditEnding += (_, e) => e.Cancel = true;

        Assert.IsFalse(tableView.CommitEdit());

        Assert.IsTrue(tableView.IsEditing);
    }

    [UITestMethod]
    public async Task CommitEdit_with_no_edit_in_progress_returns_true()
    {
        var tableView = await CreateTableViewAsync();

        Assert.IsTrue(tableView.CommitEdit());
    }

    [UITestMethod]
    public async Task NavigateFromCurrentCell_is_refused_while_editing()
    {
        var tableView = await CreateTableViewAsync();
        var slot = new TableViewCellSlot(0, 0);
        Assert.IsTrue(await tableView.BeginEditAsync(slot));

        Assert.IsFalse(tableView.NavigateFromCurrentCell(VirtualKey.Right));

        Assert.AreEqual(slot, tableView.CurrentCellSlot);
        Assert.IsTrue(tableView.IsEditing);
    }

    [UITestMethod]
    public async Task Commit_then_navigate_moves_right_like_entry_mode()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(await tableView.BeginEditAsync(new TableViewCellSlot(0, 0)));

        Assert.IsTrue(tableView.CommitEdit());
        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Right));

        Assert.AreEqual(new TableViewCellSlot(0, 1), tableView.CurrentCellSlot);
        Assert.IsFalse(tableView.IsEditing);
    }

    [UITestMethod]
    public async Task Enter_while_editing_edits_the_next_cell_by_default()
    {
        var tableView = await CreateTableViewAsync();
        Assert.IsTrue(await tableView.BeginEditAsync(new TableViewCellSlot(0, 0)));

        Assert.IsTrue(tableView.HandleTabOrEnter(VirtualKey.Enter, false, CurrentCell(tableView), new RoutedEventArgs()));

        Assert.AreEqual(new TableViewCellSlot(1, 0), tableView.CurrentCellSlot);
        Assert.IsTrue(tableView.IsEditing);
    }

    [UITestMethod]
    public async Task Enter_while_editing_commits_and_only_selects_the_cell_to_the_right_when_not_continuing()
    {
        var tableView = await CreateTableViewAsync();
        tableView.EnterKeyNavigation = TableViewEnterKeyNavigation.Right;
        tableView.ContinueEditingOnNavigation = false;
        var ended = new List<TableViewEditAction>();
        tableView.CellEditEnded += (_, e) => ended.Add(e.EditAction);
        Assert.IsTrue(await tableView.BeginEditAsync(new TableViewCellSlot(0, 0)));

        Assert.IsTrue(tableView.HandleTabOrEnter(VirtualKey.Enter, false, CurrentCell(tableView), new RoutedEventArgs()));

        Assert.AreEqual(new TableViewCellSlot(0, 1), tableView.CurrentCellSlot);
        Assert.IsFalse(tableView.IsEditing);
        CollectionAssert.AreEqual(new[] { TableViewEditAction.Commit }, ended);
    }

    [UITestMethod]
    public async Task Tab_while_editing_only_selects_the_next_cell_when_not_continuing()
    {
        var tableView = await CreateTableViewAsync();
        tableView.ContinueEditingOnNavigation = false;
        Assert.IsTrue(await tableView.BeginEditAsync(new TableViewCellSlot(1, 2)));

        Assert.IsTrue(tableView.HandleTabOrEnter(VirtualKey.Tab, false, CurrentCell(tableView), new RoutedEventArgs()));

        Assert.AreEqual(new TableViewCellSlot(2, 0), tableView.CurrentCellSlot);
        Assert.IsFalse(tableView.IsEditing);
    }

    [UITestMethod]
    public async Task Enter_while_editing_moves_nothing_when_the_commit_is_cancelled()
    {
        var tableView = await CreateTableViewAsync();
        var slot = new TableViewCellSlot(0, 0);
        Assert.IsTrue(await tableView.BeginEditAsync(slot));
        tableView.CellEditEnding += (_, e) => e.Cancel = true;

        Assert.IsFalse(tableView.HandleTabOrEnter(VirtualKey.Enter, false, CurrentCell(tableView), new RoutedEventArgs()));

        Assert.AreEqual(slot, tableView.CurrentCellSlot);
        Assert.IsTrue(tableView.IsEditing);
    }

    // Datgel DH-2751: Shift+arrow and Shift+click after a click must extend the block from the CLICKED cell.
    // Before the fix, SelectCells read CurrentCellSlot as its anchor fallback only after DeselectAll had
    // set it to null, so a click (which leaves SelectionStartCellSlot null) followed by Shift+Down selected
    // one cell: the new one.

    [UITestMethod]
    public async Task Shift_Down_after_a_click_extends_the_block_from_the_clicked_cell()
    {
        var tableView = await CreateTableViewAsync();
        await ClickAsync(tableView, new TableViewCellSlot(0, 1));

        Assert.IsTrue(tableView.NavigateFromCurrentCell(VirtualKey.Down, shiftKey: true));

        AssertSelected(tableView, (0, 1), (1, 1));
        Assert.AreEqual(new TableViewCellSlot(1, 1), tableView.CurrentCellSlot);
    }

    [UITestMethod]
    public async Task A_second_Shift_Down_keeps_the_clicked_cell_as_the_anchor()
    {
        var tableView = await CreateTableViewAsync();
        await ClickAsync(tableView, new TableViewCellSlot(0, 0));

        tableView.NavigateFromCurrentCell(VirtualKey.Down, shiftKey: true);
        tableView.NavigateFromCurrentCell(VirtualKey.Down, shiftKey: true);

        AssertSelected(tableView, (0, 0), (1, 0), (2, 0));
    }

    [UITestMethod]
    public async Task Shift_click_after_a_click_selects_the_rectangle_between_them()
    {
        var tableView = await CreateTableViewAsync();
        await ClickAsync(tableView, new TableViewCellSlot(0, 0));

        tableView.MakeSelection(new TableViewCellSlot(1, 1), shiftKey: true);

        AssertSelected(tableView, (0, 0), (0, 1), (1, 0), (1, 1));
    }

    /// <summary>
    /// The state a pointer click leaves: the clicked cell selected and current, and no selection anchor,
    /// because <c>TableView.EndDragSelection</c> clears <c>SelectionStartCellSlot</c> on release.
    /// </summary>
    private static async Task ClickAsync(TableView tableView, TableViewCellSlot slot)
    {
        await MakeCurrentAsync(tableView, slot);
        tableView.SelectionStartCellSlot = null;
    }

    private static void AssertSelected(TableView tableView, params (int Row, int Column)[] expected)
    {
        foreach (var (row, column) in expected)
        {
            Assert.IsTrue(tableView.IsCellInSelection(new TableViewCellSlot(row, column)), $"cell ({row}, {column}) should be selected");
        }

        Assert.AreEqual(expected.Length, tableView.SelectedCells.Count, "selected cell count");
    }

    private static TableViewCell? CurrentCell(TableView tableView)
    {
        return tableView.CurrentCellSlot is { } slot ? tableView.GetCellFromSlot(slot) : null;
    }

    private static async Task MakeCurrentAsync(TableView tableView, TableViewCellSlot slot)
    {
        tableView.MakeSelection(slot, false);
        await Task.Delay(100);
    }

    private static async Task<TableView> CreateTableViewAsync()
    {
        var tableView = new TableView
        {
            // Exactly the three columns declared below; the default would add three generated ones, and the wrap
            // test's "last column" would then not be the last (CI run 36832285874: landed on column 3 of 6).
            AutoGenerateColumns = false,
            SelectionMode = ListViewSelectionMode.Extended,
            SelectionUnit = TableViewSelectionUnit.Cell,
        };

        foreach (var name in new[] { nameof(NavItem.A), nameof(NavItem.B), nameof(NavItem.C) })
        {
            tableView.Columns.Add(new TableViewTextColumn
            {
                Header = name,
                Binding = new Binding { Path = new PropertyPath(name), Mode = BindingMode.TwoWay }
            });
        }

        tableView.ItemsSource = new[]
        {
            new NavItem { A = "a0", B = "b0", C = "c0" },
            new NavItem { A = "a1", B = "b1", C = "c1" },
            new NavItem { A = "a2", B = "b2", C = "c2" },
        };

        await UnitTestApp.Current.MainWindow.LoadTestContentAsync(tableView);

        return tableView;
    }

    public sealed class NavItem
    {
        public string A { get; set; } = string.Empty;
        public string B { get; set; } = string.Empty;
        public string C { get; set; } = string.Empty;
    }
}
