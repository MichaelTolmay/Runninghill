using Runninghill.Maui;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks responsive layout decisions without needing a native window or platform workload.
/// </summary>
public sealed class WorkspaceLayoutTests
{
    // Test either side of each CSS breakpoint, not just common monitor sizes.
    /// <summary>
    /// Checks stacking, spacing and heading sizes immediately around each website breakpoint.
    /// </summary>
    [Theory]
    [InlineData(320, true, true, false, 14, 18, 26)]
    [InlineData(560, true, true, false, 14, 18, 26)]
    [InlineData(561, false, true, true, 24, 20, 28)]
    [InlineData(820, false, true, true, 24, 20, 28)]
    [InlineData(821, false, false, false, 24, 20, 32)]
    [InlineData(1100, false, false, false, 24, 20, 32)]
    [InlineData(1101, false, false, false, 40, 24, 32)]
    [InlineData(1500, false, false, false, 48, 24, 32)]
    public void UsesWebsiteBreakpoints(double width, bool phone, bool stacked, bool splitHistory,
        double padding, double panelPadding, double heading)
    {
        var layout = new WorkspaceLayout(width);
        Assert.Equal(phone, layout.Phone);
        Assert.Equal(stacked, layout.Stacked);
        Assert.Equal(splitHistory, layout.SplitSentencePanels);
        Assert.Equal(padding, layout.OuterPadding);
        Assert.Equal(panelPadding, layout.PanelPadding);
        Assert.Equal(heading, layout.HeadingSize);
    }

    /// <summary>
    /// Verifies that desktop column widths leave room for both the word card and readable sentence
    /// panels.
    /// </summary>
    [Theory]
    [InlineData(821)]
    [InlineData(1024)]
    [InlineData(1440)]
    [InlineData(1920)]
    public void DesktopColumnsFitAndKeepHistoryReadable(double width)
    {
        var layout = new WorkspaceLayout(width);
        Assert.True(layout.ContentWidth <= 1440);
        Assert.True(layout.SidebarWidth >= (width <= 1100 ? 290 : 300));
        var wordWidth = layout.ContentWidth - 2 * layout.OuterPadding - 24 - layout.SidebarWidth;
        Assert.True(wordWidth >= 400, "Desktop word card must still fit its stacked form and row actions.");
    }
}
