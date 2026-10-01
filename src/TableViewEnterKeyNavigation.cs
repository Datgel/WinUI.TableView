namespace WinUI.TableView;

/// <summary>
/// Specifies where the Enter key moves the current cell.
/// </summary>
public enum TableViewEnterKeyNavigation
{
    /// <summary>
    /// Enter moves to the cell below; Shift+Enter to the cell above. This is the default.
    /// </summary>
    Down,

    /// <summary>
    /// Enter moves to the next cell to the right, wrapping to the first cell of the next row;
    /// Shift+Enter moves left. This is how the gINT data-entry grid behaves.
    /// </summary>
    Right
}
