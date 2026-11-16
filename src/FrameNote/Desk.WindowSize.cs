using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace FrameNote;

public sealed partial class Desk
{
    sealed record WindowSize(double Width, double Height, bool Maximized);
    string WindowSizePath => Path.Combine(root, "window.json");
    bool lastMaximized;

    void InitWindowSize()
    {
        try {
            using var stream = File.OpenRead(WindowSizePath);
            if (JsonSerializer.Deserialize<WindowSize>(stream, Review.Json) is {} size &&
                double.IsFinite(size.Width) && size.Width > 0 && double.IsFinite(size.Height) && size.Height > 0) {
                var area = SystemParameters.WorkArea;
                Width = Math.Clamp(size.Width, MinWidth, Math.Max(MinWidth, area.Width));
                Height = Math.Clamp(size.Height, MinHeight, Math.Max(MinHeight, area.Height));
                if (size.Maximized) WindowState = WindowState.Maximized;
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }

        lastMaximized = WindowState == WindowState.Maximized;
        StateChanged += (_, _) => {
            if (beforeFullscreen == null && WindowState != WindowState.Minimized)
                lastMaximized = WindowState == WindowState.Maximized;
        };
    }

    WindowSize CurrentWindowSize()
    {
        var bounds = RestoreBounds;
        return WindowState == WindowState.Normal || bounds.IsEmpty
            ? new(ActualWidth, ActualHeight, lastMaximized)
            : new(bounds.Width, bounds.Height, lastMaximized);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        var size = beforeFullscreen?.Size ?? CurrentWindowSize();
        if (!(size.Width > 0 && size.Height > 0)) return;
        try {
            Packages.Atomic(WindowSizePath, stream => JsonSerializer.Serialize(stream, size, Review.Json));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
