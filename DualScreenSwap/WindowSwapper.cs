using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DualScreenSwap
{
    internal static class WindowSwapper
    {
        private const uint KeepOrder = 0x0004 | 0x0010 | 0x0200;
        private static readonly uint ProcessId = (uint)Process.GetCurrentProcess().Id;

        internal static string Swap()
        {
            List<Monitor> monitors = GetMonitors();
            if (monitors.Count != 2)
                return "请在 Windows 中启用恰好两个扩展显示器后再交换窗口。";

            Snapshot[] windows = new Snapshot[2];
            int rank = 0;
            WindowApi.EnumWindows(delegate(IntPtr handle, IntPtr unused)
            {
                int currentRank = rank++;
                if (!IsApplicationWindow(handle)) return true;
                IntPtr monitor = WindowApi.MonitorFromWindow(handle, 2);
                for (int i = 0; i < monitors.Count; i++)
                {
                    if (windows[i] != null || monitors[i].Handle != monitor) continue;
                    Rect rect;
                    Placement placement = NewPlacement();
                    if (!WindowApi.GetWindowRect(handle, out rect) ||
                        !WindowApi.GetWindowPlacement(handle, ref placement) ||
                        !Intersects(rect, monitors[i].Bounds)) continue;

                    // Borderless monitor-covering windows are not ordinary application windows.
                    long style = WindowApi.GetWindowLong(handle, -16);
                    if ((style & 0x00C00000L) == 0 && Covers(rect, monitors[i].Bounds)) continue;
                    windows[i] = new Snapshot
                    {
                        Handle = handle, Bounds = rect, Placement = placement,
                        Maximized = WindowApi.IsZoomed(handle), Rank = currentRank,
                        Previous = WindowApi.GetWindow(handle, 3)
                    };
                }
                return windows[0] == null || windows[1] == null;
            }, IntPtr.Zero);

            if (windows[0] == null || windows[1] == null)
                return "每个显示器上都需要至少一个可见的普通应用窗口。";
            if (WindowApi.IsHungAppWindow(windows[0].Handle) || WindowApi.IsHungAppWindow(windows[1].Handle))
                return "目标窗口暂时没有响应，请稍后再试。";

            IntPtr foreground = WindowApi.GetForegroundWindow();
            string failure = null;
            try
            {
                if (!Move(windows[0], monitors[0], monitors[1], out failure) ||
                    !Move(windows[1], monitors[1], monitors[0], out failure))
                {
                    bool firstRestored = Restore(windows[0]);
                    bool secondRestored = Restore(windows[1]);
                    return failure + (firstRestored && secondRestored
                        ? "\n已恢复两个窗口原来的位置和状态。"
                        : "\n部分窗口未能恢复，请检查窗口位置和应用权限。");
                }

                // Both incoming windows must remain in front of the other windows on their new screen.
                Snapshot lower = windows[0].Rank > windows[1].Rank ? windows[0] : windows[1];
                Snapshot upper = lower == windows[0] ? windows[1] : windows[0];
                Raise(lower.Handle);
                Raise(upper.Handle);
                return null;
            }
            finally
            {
                if (WindowApi.IsWindow(foreground) && WindowApi.GetForegroundWindow() != foreground)
                    WindowApi.SetForegroundWindow(foreground);
            }
        }

        private static bool Move(Snapshot window, Monitor source, Monitor target, out string failure)
        {
            failure = null;
            Rect destination = window.Bounds;
            if (window.Maximized)
            {
                Placement restored = window.Placement;
                restored.Flags = 0;
                restored.ShowCommand = 4; // SW_SHOWNOACTIVATE: restore without selecting this application.
                if (!WindowApi.SetWindowPlacement(window.Handle, ref restored))
                {
                    failure = NativeFailure("无法还原最大化窗口");
                    return false;
                }
                // GetWindowRect is in screen coordinates; WINDOWPLACEMENT normal bounds are not.
                if (!WindowApi.GetWindowRect(window.Handle, out destination))
                {
                    failure = NativeFailure("无法读取还原后的窗口位置");
                    return false;
                }
            }

            destination.Offset(target.Bounds.Left - source.Bounds.Left, target.Bounds.Top - source.Bounds.Top);
            if (!MoveRectangle(window.Handle, destination))
            {
                failure = "目标应用未能保持指定的位置或大小；窗口可能受应用自身限制或权限限制。";
                return false;
            }

            if (window.Maximized)
            {
                Placement maximized = NewPlacement();
                if (!WindowApi.GetWindowPlacement(window.Handle, ref maximized))
                {
                    failure = NativeFailure("无法读取移动后的窗口状态");
                    return false;
                }
                maximized.Flags = 0;
                maximized.ShowCommand = 3;
                maximized.MaxPosition = new Point { X = -1, Y = -1 };
                if (!WindowApi.SetWindowPlacement(window.Handle, ref maximized) ||
                    !WindowApi.IsZoomed(window.Handle) ||
                    WindowApi.MonitorFromWindow(window.Handle, 2) != target.Handle)
                {
                    failure = "目标应用未能在另一块显示器上保持最大化。";
                    return false;
                }
            }
            return true;
        }

        private static bool MoveRectangle(IntPtr handle, Rect rect)
        {
            // WM_DPICHANGED handlers may resize a window during the first cross-monitor move.
            // Reapply once after that transition so the user's requested physical dimensions win.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!WindowApi.SetWindowPos(handle, IntPtr.Zero, rect.Left, rect.Top,
                    rect.Right - rect.Left, rect.Bottom - rect.Top, KeepOrder)) return false;
                Rect actual;
                if (WindowApi.GetWindowRect(handle, out actual) && actual.SameAs(rect)) return true;
            }
            return false;
        }

        private static bool Restore(Snapshot window)
        {
            Placement placement = window.Placement;
            placement.Flags = 0;
            bool restored = true;
            if (window.Maximized)
            {
                Placement normal = placement;
                normal.ShowCommand = 4;
                restored = WindowApi.SetWindowPlacement(window.Handle, ref normal);
            }
            restored = WindowApi.SetWindowPlacement(window.Handle, ref placement) && restored;
            if (!window.Maximized) restored = MoveRectangle(window.Handle, window.Bounds) && restored;
            else
            {
                Rect current;
                restored = restored && WindowApi.IsZoomed(window.Handle) &&
                    WindowApi.GetWindowRect(window.Handle, out current) && current.SameAs(window.Bounds);
            }
            IntPtr previous = WindowApi.IsWindow(window.Previous) ? window.Previous : IntPtr.Zero;
            WindowApi.SetWindowPos(window.Handle, previous, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            return restored;
        }

        private static void Raise(IntPtr handle)
        {
            WindowApi.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
        }

        private static bool IsApplicationWindow(IntPtr handle)
        {
            if (!WindowApi.IsWindowVisible(handle) || WindowApi.IsIconic(handle) ||
                WindowApi.GetWindowTextLength(handle) == 0) return false;
            uint process;
            WindowApi.GetWindowThreadProcessId(handle, out process);
            if (process == ProcessId) return false;
            long style = WindowApi.GetWindowLong(handle, -16);
            long extended = WindowApi.GetWindowLong(handle, -20);
            if ((style & 0x40000000L) != 0 || (extended & 0x00000080L) != 0) return false;
            IntPtr owner = WindowApi.GetWindow(handle, 4);
            if (owner != IntPtr.Zero && WindowApi.IsWindowVisible(owner) && (extended & 0x00040000L) == 0)
                return false;
            int cloaked;
            if (WindowApi.DwmGetWindowAttribute(handle, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;
            StringBuilder name = new StringBuilder(256);
            WindowApi.GetClassName(handle, name, name.Capacity);
            string className = name.ToString();
            return className != "Progman" && className != "WorkerW" &&
                className != "Shell_TrayWnd" && className != "Shell_SecondaryTrayWnd" &&
                className != "#32768";
        }

        private static List<Monitor> GetMonitors()
        {
            List<Monitor> result = new List<Monitor>();
            WindowApi.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr handle, IntPtr dc, ref Rect bounds, IntPtr unused)
                {
                    result.Add(new Monitor { Handle = handle, Bounds = bounds });
                    return true;
                }, IntPtr.Zero);
            return result;
        }

        private static bool Intersects(Rect a, Rect b)
        {
            return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
        }

        private static bool Covers(Rect a, Rect b)
        {
            return a.Left <= b.Left && a.Top <= b.Top && a.Right >= b.Right && a.Bottom >= b.Bottom;
        }

        private static Placement NewPlacement()
        {
            return new Placement { Length = (uint)Marshal.SizeOf(typeof(Placement)) };
        }

        private static string NativeFailure(string message)
        {
            int error = Marshal.GetLastWin32Error();
            return message + (error == 0 ? "。" : "（Windows 错误 " + error + "）。");
        }

        private sealed class Monitor
        {
            internal IntPtr Handle;
            internal Rect Bounds;
        }

        private sealed class Snapshot
        {
            internal IntPtr Handle;
            internal IntPtr Previous;
            internal Rect Bounds;
            internal Placement Placement;
            internal bool Maximized;
            internal int Rank;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point { internal int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            internal int Left, Top, Right, Bottom;
            internal void Offset(int x, int y) { Left += x; Right += x; Top += y; Bottom += y; }
            internal bool SameAs(Rect other)
            {
                return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Placement
        {
            internal uint Length, Flags, ShowCommand;
            internal Point MinPosition, MaxPosition;
            internal Rect NormalPosition;
        }

        private static class WindowApi
        {
            internal delegate bool EnumWindowCallback(IntPtr window, IntPtr data);
            internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr data);
            [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
            [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
            [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
            [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
            [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
            [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
            [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
            [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr window);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(IntPtr window);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int size);
            [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
            [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
            [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
            [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
            [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
            [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
            [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
            [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        }
    }
}
