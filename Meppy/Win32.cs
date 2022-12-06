using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Wiltoga.Meppy
{
    public static class Win32
    {
        public enum ShowWindowCommands : int
        {
            Hide = 0,
            Normal = 1,
            Minimized = 2,
            Maximized = 3,
        }

        [DllImport("dwmapi")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, Int32 dwAttribute, ref RECT pvAttribute, Int32 cbAttribute);

        [DllImport("user32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref RECT lpRect);

        [DllImport("user32", SetLastError = true)]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32")]
        public static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr hwnd, ref Point lpRect);

        [DllImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
            public int Width { get => Right - Left; set => Right = value + Left; }
            public int Height { get => Bottom - Top; set => Bottom = value + Top; }

            private RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public static RECT FromPoints(int left, int top, int right, int bottom) => new RECT(left, top, right, bottom);

            public static RECT FromSizes(int left, int top, int width, int height) => new RECT(left, top, width + left, height + top);
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public ShowWindowCommands showCmd;
            public System.Drawing.Point ptMinPosition;
            public System.Drawing.Point ptMaxPosition;
            public RECT rcNormalPosition;
        }
    }
}