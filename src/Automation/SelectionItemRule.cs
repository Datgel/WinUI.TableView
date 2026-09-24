namespace WinUI.TableView.Automation;

/// <summary>The three requests a UI Automation client can make of a <c>SelectionItem</c> pattern.</summary>
internal enum SelectionItemRequest
{
    Select,
    AddToSelection,
    RemoveFromSelection,
}

/// <summary>What the table must do to honour a <see cref="SelectionItemRequest"/>.</summary>
internal enum SelectionItemAction
{
    /// <summary>The request cannot be honoured; the provider throws <c>InvalidOperationException</c>,
    /// which is the UI Automation contract.</summary>
    Refuse,

    /// <summary>Already in the requested state.</summary>
    NoChange,

    /// <summary>Make this item the only selected item.</summary>
    SelectOnly,

    /// <summary>Add this item to the selection, leaving the rest.</summary>
    Add,

    /// <summary>Take this item out of the selection.</summary>
    Remove,
}

/// <summary>
/// The UI Automation <c>SelectionItem</c> semantics shared by the row and cell peers.
/// </summary>
/// <remarks>
/// <c>Select</c> deselects everything else. <c>AddToSelection</c> on a single-selection table is
/// refused when something else is already selected — the contract says it must not silently
/// replace the selection, which is what <c>Select</c> is for. A request that cannot be honoured is
/// refused, never ignored: a client told "done" about a selection that did not happen cannot tell.
/// </remarks>
internal static class SelectionItemRule
{
    internal static SelectionItemAction Decide(
        bool canSelectMultiple, bool isSelected, bool anotherIsSelected, SelectionItemRequest request)
    {
        return request switch
        {
            SelectionItemRequest.Select =>
                isSelected && !anotherIsSelected ? SelectionItemAction.NoChange : SelectionItemAction.SelectOnly,

            SelectionItemRequest.AddToSelection =>
                isSelected ? SelectionItemAction.NoChange
                : !canSelectMultiple && anotherIsSelected ? SelectionItemAction.Refuse
                : SelectionItemAction.Add,

            SelectionItemRequest.RemoveFromSelection =>
                isSelected ? SelectionItemAction.Remove : SelectionItemAction.NoChange,

            _ => SelectionItemAction.Refuse,
        };
    }
}
