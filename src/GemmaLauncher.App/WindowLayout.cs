using System.Windows;

namespace GemmaLauncher.App;

public static class WindowLayout
{
    public static void FitToWorkArea(Window window, Rect available)
    {
        var width = Math.Min(window.Width, Math.Max(1, available.Width - 24));
        var height = Math.Min(window.Height, Math.Max(1, available.Height - 24));
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = width;
        window.Height = height;
    }
}
