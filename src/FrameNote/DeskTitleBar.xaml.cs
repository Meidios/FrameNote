using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace FrameNote;

internal partial class DeskTitleBar : Grid
{
    readonly Window window;
    readonly FrameworkElement content;
    readonly WindowChrome chrome;

    public DeskTitleBar(Window window, FrameworkElement content)
    {
        InitializeComponent();
        this.window = window;
        this.content = content;
        window.Background = Brushes.Transparent;
        chrome = new WindowChrome {
            CaptionHeight = Height,
            GlassFrameThickness = new Thickness(0, Height, 0, 0),
            NonClientFrameEdges = NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
            UseAeroCaptionButtons = true
        };
        WindowChrome.SetWindowChrome(window, chrome);
        Loaded += (_, _) => UpdateInsets();
        window.StateChanged += (_, _) => UpdateInsets();
        window.DpiChanged += (_, _) => UpdateInsets();
    }

    public void SetFullscreen(bool fullscreen)
    {
        Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        WindowChrome.SetWindowChrome(window, fullscreen ? null : chrome);
        DeskTheme.SetWindowCorners(window, fullscreen);
        if (!fullscreen) DeskTheme.Caption(window);
        UpdateInsets();
        if (!fullscreen) Dispatcher.InvokeAsync(RepaintRestoredCaption, DispatcherPriority.Loaded);
    }

    void RepaintRestoredCaption()
    {
        if (!IsVisible || PresentationSource.FromVisual(this) is not HwndSource source) return;
        // Windows can preserve pixels from the borderless client origin when the
        // frame returns. Invalidate the caption after WPF arranges the new insets.
        var dpi = VisualTreeHelper.GetDpi(this);
        var caption = new NativeRect {
            Right = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            Bottom = (int)Math.Ceiling(chrome.GlassFrameThickness.Top * dpi.DpiScaleY)
        };
        InvalidateRect(source.Handle, ref caption, false);
    }

    internal void SetHistoryShortcuts(ShortcutMap shortcuts)
    {
        void Set(Button button,string id,string label){
            var keys=shortcuts[id].Label(InputKind.Keyboard);
            button.ToolTip=L.WithShortcut(label,keys);
            System.Windows.Automation.AutomationProperties.SetAcceleratorKey(button,keys);
        }
        Set(UndoButton,"undo",L.Text("撤销"));
        Set(RedoButton,"redo",L.Text("重做"));
    }

    void UpdateInsets()
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || window.WindowState == WindowState.Minimized) return;
        if (Visibility != Visibility.Visible) {
            content.Margin = default;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        var border = SystemParameters.WindowResizeBorderThickness;
        var insets = new Thickness(border.Left, 0, border.Right, border.Bottom);
        if (window.WindowState == WindowState.Maximized) {
            // A maximized HWND also extends its padded resize frame past the work area.
            const int sizeFrame = 32, paddedBorder = 92;
            uint monitorDpi = (uint)Math.Round(dpi.PixelsPerInchX);
            insets = new Thickness((GetSystemMetricsForDpi(sizeFrame, monitorDpi) + GetSystemMetricsForDpi(paddedBorder, monitorDpi)) / dpi.DpiScaleX);
        }
        content.Margin = insets;
        double captionBottom = insets.Top + Height;
        chrome.GlassFrameThickness = new Thickness(0, captionBottom, 0, 0);
        chrome.CaptionHeight = Math.Max(0, captionBottom - chrome.ResizeBorderThickness.Top);
        // Convert screen coordinates after WPF has arranged the new client insets.
        Dispatcher.InvokeAsync(AlignSettingsButton, DispatcherPriority.Loaded);
    }

    void AlignSettingsButton()
    {
        if (!IsVisible || ActualWidth <= 0 || window.WindowState == WindowState.Minimized) return;
        var info = new TitleBarInfo { Size = Marshal.SizeOf<TitleBarInfo>(), States = new uint[6], Rects = new NativeRect[6] };
        SendMessage(new WindowInteropHelper(window).Handle, 0x033F /* WM_GETTITLEBARINFOEX */, IntPtr.Zero, ref info);
        var minimize = info.Rects[2];
        if (minimize.Right <= minimize.Left || minimize.Bottom <= minimize.Top) return;

        var start = PointFromScreen(new Point(minimize.Left, minimize.Top));
        var end = PointFromScreen(new Point(minimize.Right, minimize.Bottom));
        CaptionButtons.Width = new GridLength(Math.Max(0, ActualWidth - start.X));
        SettingsGear.Width = end.X - start.X;
        SettingsGear.Height = end.Y - start.Y;
        SettingsGear.Margin = new Thickness(0, start.Y, 0, 0);
        // Native glyphs are centered above the last physical row of the caption button.
        var scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var top = Math.Floor((minimize.Bottom - minimize.Top - 1 - GearIcon.Height * scaleY) / 2);
        SettingsGear.Padding = new Thickness(SettingsGear.Padding.Left, Math.Max(0, top / scaleY), 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct TitleBarInfo {
        public int Size;
        public NativeRect Bounds;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] States;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public NativeRect[] Rects;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref TitleBarInfo info);
    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")]
    static extern bool InvalidateRect(IntPtr hwnd, ref NativeRect rect, bool erase);
}
