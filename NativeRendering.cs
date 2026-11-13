using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace NaiwaDesktopPet
{
    // A high-resolution clock posts bounded work to the UI thread every 10 ms.
    internal sealed class FrameClock : IDisposable
    {
        public const int RenderMessage = 0x8000 + 77;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr callback, IntPtr argument, bool resume);
        [DllImport("kernel32.dll")] static extern bool CancelWaitableTimer(IntPtr timer);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
        readonly IntPtr timer, window;
        readonly Thread worker;
        readonly bool fallbackResolution;
        volatile bool stopping;
        int pending;

        public FrameClock(IntPtr window)
        {
            this.window = window;
            timer = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x001F0003);
            if (timer == IntPtr.Zero)
            {
                timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0, 0x001F0003);
                fallbackResolution = timeBeginPeriod(1) == 0;
            }
            if (timer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            worker = new Thread(Pump) { IsBackground = true, Name = "Naiwa 100 FPS clock" };
            worker.Start();
        }
        void Pump()
        {
            // Advance fixed QPC deadlines instead of adding OS wake-up latency
            // to each 10 ms period. Use one-shot waits, without busy spinning.
            double period = Stopwatch.Frequency / 100.0;
            double deadline = Stopwatch.GetTimestamp() + period;
            while (!stopping)
            {
                double remaining = deadline - Stopwatch.GetTimestamp();
                long due = -Math.Max(1, (long)(remaining * 10000000 / Stopwatch.Frequency));
                if (!SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) return;
                uint result = WaitForSingleObject(timer, 50);
                if (result == 0 && !stopping && Interlocked.CompareExchange(ref pending, 1, 0) == 0)
                    if (!PostMessage(window, RenderMessage, IntPtr.Zero, IntPtr.Zero))
                        Interlocked.Exchange(ref pending, 0);
                deadline += period;
                double now = Stopwatch.GetTimestamp();
                if (deadline < now) deadline = now + period;
            }
        }
        public void Consumed() { Interlocked.Exchange(ref pending, 0); }
        public void Dispose()
        {
            if (stopping) return;
            stopping = true; CancelWaitableTimer(timer); worker.Join(); CloseHandle(timer);
            if (fallbackResolution) timeEndPeriod(1);
        }
    }

    // Reuse one premultiplied DIB/DC instead of allocating GDI objects each frame.
    internal sealed class LayeredSurface : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT
        { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] struct SIZE
        { public int X, Y; public SIZE(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND
        { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] struct INFO
        {
            public uint Size; public int Width, Height; public ushort Planes, BitCount;
            public uint Compression, ImageSize; public int XResolution, YResolution;
            public uint UsedColors, ImportantColors;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateDIBSection(IntPtr dc, ref INFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref POINT position,
            ref SIZE size, IntPtr source, ref POINT origin, int key, ref BLEND blend, int flags);
        readonly IntPtr memory, native, old;
        readonly int width, height;
        readonly Bitmap bitmap;
        public readonly Graphics Graphics;
        public LayeredSurface(int width, int height)
        {
            this.width = width; this.height = height;
            memory = CreateCompatibleDC(IntPtr.Zero);
            var info = new INFO { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            IntPtr bits;
            native = CreateDIBSection(memory, ref info, 0, out bits, IntPtr.Zero, 0);
            if (native == IntPtr.Zero)
            { DeleteDC(memory); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            old = SelectObject(memory, native);
            bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
            Graphics = System.Drawing.Graphics.FromImage(bitmap);
        }
        public void Present(IntPtr handle, int x, int y)
        {
            Graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            var position = new POINT(x, y); var size = new SIZE(width, height); var origin = new POINT(0, 0);
            var blend = new BLEND { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(handle, IntPtr.Zero, ref position, ref size, memory, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public Rectangle MeasureVisibleBounds()
        {
            Graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            return AnimationClip.VisibleBounds(bitmap);
        }
        public void Dispose()
        {
            Graphics.Dispose(); bitmap.Dispose(); SelectObject(memory, old);
            DeleteObject(native); DeleteDC(memory);
        }
    }

    public static class DisplayQuality
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        public static void Enable()
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            SetThreadDpiAwarenessContext(new IntPtr(-4));
        }
        public static float Scale { get { return Math.Max(1, GetDpiForSystem() / 96f); } }
    }
}
