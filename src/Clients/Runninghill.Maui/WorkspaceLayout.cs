namespace Runninghill.Maui;

// These are the website's CSS breakpoints, measured in logical pixels (not monitor pixels).
// Keeping the decisions separate from controls makes resizing cheap and testable on any OS.
/// <summary>
/// Calculates panel widths, spacing and stacking from the available logical width, using the
/// website responsive breakpoints.
/// </summary>
internal readonly record struct WorkspaceLayout(double Width)
{
    public double ContentWidth => Math.Min(1440, Width);
    public bool Phone => Width <= 560;
    public bool Stacked => Width <= 820;
    public bool SplitSentencePanels => Stacked && !Phone;
    public double OuterPadding => Phone ? 14 : Width <= 1100 ? 24 : Width >= 1500 ? 48 : 40;
    public double PanelPadding => Phone ? 18 : Width <= 1100 ? 20 : 24;
    public double HeadingSize => Phone ? 26 : Stacked ? 28 : 32;
    public double SidebarWidth => Stacked ? 0 : Math.Max(Width <= 1100 ? 290 : 300,
        (ContentWidth - 2 * OuterPadding - 24) / (Width <= 1100 ? 2.4 : 2.7));
    public bool StackActions => Phone || (Width > 820 && Width <= 1100);
}
