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

    private static async Task MakeCurrentAsync(TableView tableView, TableViewCellSlot slot)
    {
        tableView.MakeSelection(slot, false);
        await Task.Delay(100);
    }

    private static async Task<TableView> CreateTableViewAsync()
    {
        var tableView = new TableView
        {
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
