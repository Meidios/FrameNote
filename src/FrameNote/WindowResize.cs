using System.Runtime.InteropServices;

namespace FrameNote;

internal static class WindowResize
{
    const int EnterSizeMove = 0x0231, ExitSizeMove = 0x0232, NcCalcSize = 0x0083, NcDestroy = 0x0082;
    const int Redraw = 0x0300;
    delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    static readonly SubclassProc DuringDrag = ResizeProc;

    internal static nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == EnterSizeMove) SetWindowSubclass(hwnd, DuringDrag, 1, 0);
        return 0;
    }

    static nint ResizeProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message is ExitSizeMove or NcDestroy) RemoveWindowSubclass(hwnd, DuringDrag, id);
        var result = DefSubclassProc(hwnd, message, wParam, lParam);
        // Keep WindowChrome's DPI-aware client rectangle. WVR_REDRAW with non-client
        // borders makes Windows temporarily offset the child windows by the border width.
        return message == NcCalcSize && wParam != 0 && result == Redraw ? 0 : result;
    }

    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
