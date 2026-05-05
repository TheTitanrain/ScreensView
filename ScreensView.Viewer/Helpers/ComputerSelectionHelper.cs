using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Helpers;

internal static class ComputerSelectionHelper
{
    public static ComputerViewModel ApplyClick(
        IReadOnlyList<ComputerViewModel> computers,
        ComputerViewModel clicked,
        ComputerViewModel? anchor,
        bool isControlPressed,
        bool isShiftPressed)
    {
        if (isShiftPressed)
        {
            var effectiveAnchor = Contains(computers, anchor) ? anchor! : clicked;
            ApplyRangeSelection(computers, effectiveAnchor, clicked);
            return effectiveAnchor;
        }

        if (isControlPressed)
        {
            clicked.IsSelected = !clicked.IsSelected;
            return clicked;
        }

        SelectOnly(computers, clicked);
        return clicked;
    }

    public static ComputerViewModel ApplyRightClick(
        IReadOnlyList<ComputerViewModel> computers,
        ComputerViewModel clicked,
        ComputerViewModel? anchor)
    {
        if (!clicked.IsSelected)
        {
            SelectOnly(computers, clicked);
            return clicked;
        }

        return Contains(computers, anchor) ? anchor! : clicked;
    }

    public static IReadOnlyList<ComputerViewModel> GetContextMenuTargets(
        IReadOnlyList<ComputerViewModel> computers,
        ComputerViewModel clicked)
    {
        return clicked.IsSelected
            ? computers.Where(computer => computer.IsSelected).ToList()
            : [clicked];
    }

    public static ComputerViewModel? SelectAll(IReadOnlyList<ComputerViewModel> computers)
    {
        foreach (var computer in computers)
            computer.IsSelected = true;

        return computers.Count > 0 ? computers[0] : null;
    }

    private static void SelectOnly(IReadOnlyList<ComputerViewModel> computers, ComputerViewModel selected)
    {
        foreach (var computer in computers)
            computer.IsSelected = ReferenceEquals(computer, selected);
    }

    private static void ApplyRangeSelection(
        IReadOnlyList<ComputerViewModel> computers,
        ComputerViewModel anchor,
        ComputerViewModel clicked)
    {
        var anchorIndex = IndexOf(computers, anchor);
        var clickedIndex = IndexOf(computers, clicked);
        if (anchorIndex < 0 || clickedIndex < 0)
        {
            SelectOnly(computers, clicked);
            return;
        }

        var start = Math.Min(anchorIndex, clickedIndex);
        var end = Math.Max(anchorIndex, clickedIndex);
        for (var i = 0; i < computers.Count; i++)
            computers[i].IsSelected = i >= start && i <= end;
    }

    private static int IndexOf(IReadOnlyList<ComputerViewModel> computers, ComputerViewModel target)
    {
        for (var i = 0; i < computers.Count; i++)
        {
            if (ReferenceEquals(computers[i], target))
                return i;
        }

        return -1;
    }

    private static bool Contains(IReadOnlyList<ComputerViewModel> computers, ComputerViewModel? target) =>
        target != null && IndexOf(computers, target) >= 0;
}
