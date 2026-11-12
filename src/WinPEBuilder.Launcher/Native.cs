using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WinPEBuilder.Launcher
{
    internal static class Native
    {
        private const uint SpiGetWorkArea = 0x0030;
        private const uint SpiSetWorkArea = 0x002F;
        private const int SwRestore = 9;

        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref Rect rect, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        public static Rectangle GetWorkArea()
        {
            var rect = new Rect();
            SystemParametersInfo(SpiGetWorkArea, 0, ref rect, 0);
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        /// <summary>Reserva a área da barra de tarefas: janelas maximizadas não passam por baixo dela.</summary>
        public static void SetWorkArea(Rectangle area)
        {
            var rect = new Rect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
            SystemParametersInfo(SpiSetWorkArea, 0, ref rect, 0);
        }

        public static void BringToFront(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(handle))
            {
                ShowWindow(handle, SwRestore);
            }

            SetForegroundWindow(handle);
        }

        /// <summary>Manda a janela (a área de trabalho) para o fundo, sem ativá-la.</summary>
        public static void SendToBottom(IntPtr handle)
        {
            SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        }
    }
}
