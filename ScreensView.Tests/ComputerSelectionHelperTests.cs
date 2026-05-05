using ScreensView.Shared.Models;
using ScreensView.Viewer.Helpers;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Tests;

public class ComputerSelectionHelperTests
{
    [Fact]
    public void ApplyClick_WithoutModifiers_SelectsOnlyClickedComputer()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        computers[0].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyClick(
            computers,
            computers[1],
            computers[0],
            isControlPressed: false,
            isShiftPressed: false);

        Assert.Equal(computers[1], anchor);
        Assert.Equal([false, true, false], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void ApplyClick_WithControl_TogglesClickedComputerAndKeepsOthers()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        computers[0].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyClick(
            computers,
            computers[1],
            computers[0],
            isControlPressed: true,
            isShiftPressed: false);

        Assert.Equal(computers[1], anchor);
        Assert.Equal([true, true, false], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void ApplyClick_WithShift_SelectsRangeFromAnchorToClickedComputer()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3", "PC-4");
        computers[0].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyClick(
            computers,
            computers[3],
            computers[1],
            isControlPressed: false,
            isShiftPressed: true);

        Assert.Equal(computers[1], anchor);
        Assert.Equal([false, true, true, true], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void ApplyClick_WithShiftAndStaleAnchor_SelectsOnlyClickedComputer()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        var staleAnchor = MakeComputers("Removed")[0];
        computers[0].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyClick(
            computers,
            computers[2],
            staleAnchor,
            isControlPressed: false,
            isShiftPressed: true);

        Assert.Equal(computers[2], anchor);
        Assert.Equal([false, false, true], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void ApplyRightClick_WhenClickedComputerIsAlreadySelected_KeepsSelection()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        computers[0].IsSelected = true;
        computers[1].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyRightClick(computers, computers[1], computers[0]);

        Assert.Equal(computers[0], anchor);
        Assert.Equal([true, true, false], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void ApplyRightClick_WhenAnchorIsStale_ReturnsClickedComputer()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        var staleAnchor = MakeComputers("Removed")[0];
        computers[0].IsSelected = true;
        computers[1].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyRightClick(computers, computers[1], staleAnchor);

        Assert.Equal(computers[1], anchor);
        Assert.Equal([true, true, false], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void ApplyRightClick_WhenClickedComputerIsNotSelected_SelectsOnlyClickedComputer()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        computers[0].IsSelected = true;
        computers[1].IsSelected = true;

        var anchor = ComputerSelectionHelper.ApplyRightClick(computers, computers[2], computers[0]);

        Assert.Equal(computers[2], anchor);
        Assert.Equal([false, false, true], computers.Select(c => c.IsSelected).ToArray());
    }

    [Fact]
    public void GetContextMenuTargets_WhenClickedComputerIsSelected_ReturnsAllSelectedComputers()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        computers[0].IsSelected = true;
        computers[2].IsSelected = true;

        var targets = ComputerSelectionHelper.GetContextMenuTargets(computers, computers[2]);

        Assert.Equal(["PC-1", "PC-3"], targets.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void GetContextMenuTargets_WhenClickedComputerIsNotSelected_ReturnsClickedComputerOnly()
    {
        var computers = MakeComputers("PC-1", "PC-2", "PC-3");
        computers[0].IsSelected = true;

        var targets = ComputerSelectionHelper.GetContextMenuTargets(computers, computers[2]);

        Assert.Equal(["PC-3"], targets.Select(c => c.Name).ToArray());
    }

    private static List<ComputerViewModel> MakeComputers(params string[] names) =>
        names.Select(name => new ComputerViewModel(new ComputerConfig
        {
            Id = Guid.NewGuid(),
            Name = name,
            Host = name,
            Port = 5443,
            ApiKey = "key",
            IsEnabled = true
        })).ToList();
}
