using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SD = WinUI.TableView.SortDirection;

namespace WinUI.TableView;

partial class TableViewColumnHeader
{
    private bool _commandsInitialized;
    // Datgel fork (DH-2252): created with the menu, on its first open, not with every header. A wide table built
    // six commands per header (four on Uno) for menus nobody had opened.
#if WINDOWS
    private StandardUICommand? _groupCommand;
    private StandardUICommand? _sortGroupsByCountCommand;
#endif
    private StandardUICommand? _sortAscendingCommand;
    private StandardUICommand? _sortDescendingCommand;
    private StandardUICommand? _clearSortingCommand;
    private StandardUICommand? _clearFilterCommand;

    /// <summary>
    /// Sets commands to the option menu items of a template that still declares the menu itself (an <c>OptionsFlyout</c>
    /// part, as before DH-2252). The default template does not: <see cref="CreateOptionsFlyout"/> builds the menu.
    /// </summary>
    private void SetOptionCommands()
    {
        InitializeCommands();

#if WINDOWS
        if (GetTemplateChild("GroupMenuItem") is MenuFlyoutItem groupMenuItem)
            groupMenuItem.Command = _groupCommand;
        if (GetTemplateChild("SortGroupsByCountMenuItem") is MenuFlyoutItem sortGroupsByCountMenuItem)
            sortGroupsByCountMenuItem.Command = _sortGroupsByCountCommand;
#endif
        if (GetTemplateChild("SortAscendingMenuItem") is MenuFlyoutItem sortAscendingMenuItem)
            sortAscendingMenuItem.Command = _sortAscendingCommand;
        if (GetTemplateChild("SortDescendingMenuItem") is MenuFlyoutItem sortDescendingMenuItem)
            sortDescendingMenuItem.Command = _sortDescendingCommand;
        if (GetTemplateChild("ClearSortingMenuItem") is MenuFlyoutItem clearSortingMenuItem)
            clearSortingMenuItem.Command = _clearSortingCommand;
        if (GetTemplateChild("ClearFilterMenuItem") is MenuFlyoutItem clearFilterMenuItem)
            clearFilterMenuItem.Command = _clearFilterCommand;
    }

    /// <summary>
    /// Builds the options menu (Datgel fork, DH-2252): the items the default template used to declare under the
    /// options button, with the same names, icons and commands, styled by <see cref="OptionsFlyoutPresenterStyle"/>.
    /// Called on the first open only.
    /// </summary>
    private TableViewFilterMenuFlyout CreateOptionsFlyout()
    {
        InitializeCommands();

        var flyout = new TableViewFilterMenuFlyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = OptionsFlyoutPresenterStyle,
        };

#if WINDOWS
        flyout.Items.Add(MenuItem("GroupMenuItem", _groupCommand, ""));
        flyout.Items.Add(MenuItem("SortGroupsByCountMenuItem", _sortGroupsByCountCommand, ""));
        flyout.Items.Add(new MenuFlyoutSeparator());
#endif
        flyout.Items.Add(MenuItem("SortAscendingMenuItem", _sortAscendingCommand, ""));
        flyout.Items.Add(MenuItem("SortDescendingMenuItem", _sortDescendingCommand, ""));
        flyout.Items.Add(MenuItem("ClearSortingMenuItem", _clearSortingCommand, glyph: null));
        flyout.Items.Add(new MenuFlyoutSeparator { Name = "MenuFlyoutSeparator" });
        flyout.Items.Add(MenuItem("ClearFilterMenuItem", _clearFilterCommand, glyph: null));
        return flyout;
    }

    private static MenuFlyoutItem MenuItem(string name, StandardUICommand? command, string? glyph)
    {
        var item = new MenuFlyoutItem { Name = name, Command = command };
        if (glyph is not null)
            item.Icon = new FontIcon { Glyph = glyph };
        return item;
    }

    /// <summary>
    /// Initializes the commands.
    /// </summary>
    private void InitializeCommands()
    {
        if (_commandsInitialized)
        {
            return;
        }

#if WINDOWS
        _groupCommand = new() { Label = TableViewLocalizedStrings.Group };
        _sortGroupsByCountCommand = new() { Label = TableViewLocalizedStrings.SortGroupsByCount };
#endif
        _sortAscendingCommand = new() { Label = TableViewLocalizedStrings.SortAscending };
        _sortDescendingCommand = new() { Label = TableViewLocalizedStrings.SortDescending };
        _clearSortingCommand = new() { Label = TableViewLocalizedStrings.ClearSorting };
        _clearFilterCommand = new() { Label = TableViewLocalizedStrings.ClearFilter };

#if WINDOWS
        _groupCommand.ExecuteRequested += delegate { Group(); };
        _groupCommand.CanExecuteRequested += (_, e) =>
        {
            e.CanExecute = CanGroup;
            _groupCommand!.Label = IsGrouped ? TableViewLocalizedStrings.Ungroup : TableViewLocalizedStrings.Group;
        };

        _sortGroupsByCountCommand.ExecuteRequested += delegate { ToggleGroupSortMode(); };
        _sortGroupsByCountCommand.CanExecuteRequested += (_, e) =>
        {
            e.CanExecute = IsGrouped;
            _sortGroupsByCountCommand!.Label = IsGroupSortedByCount
                ? TableViewLocalizedStrings.SortGroupsByValue
                : TableViewLocalizedStrings.SortGroupsByCount;
        };
#endif

        _sortAscendingCommand.ExecuteRequested += delegate { DoSort(SD.Ascending); };
        _sortAscendingCommand.CanExecuteRequested += (_, e) => e.CanExecute = CanSort && Column?.SortDirection != SD.Ascending;

        _sortDescendingCommand.ExecuteRequested += delegate { DoSort(SD.Descending); };
        _sortDescendingCommand.CanExecuteRequested += (_, e) => e.CanExecute = CanSort && Column?.SortDirection != SD.Descending;

        _clearSortingCommand.ExecuteRequested += delegate { ClearSortingWithEvent(); };
        _clearSortingCommand.CanExecuteRequested += (_, e) => e.CanExecute = Column?.SortDirection is not null &&
#if WINDOWS
        !IsGrouped;
#else
        true;
#endif

        _clearFilterCommand.ExecuteRequested += delegate { ClearFilter(); };
        _clearFilterCommand.CanExecuteRequested += (_, e) => e.CanExecute = Column?.IsFiltered is true;

        _commandsInitialized = true;
    }
}
