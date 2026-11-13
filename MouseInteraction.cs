using System;
using System.Drawing;

namespace NaiwaDesktopPet
{
    internal enum MousePetAction { None, Belly, Feet }

    internal static class MouseMotion
    {
        public const int BellyDuration = 1200, RetreatDuration = 1100;
        static double Ease(double a, double b, double time)
        { double t = Math.Max(0, Math.Min(1, (time - a) / (b - a))); return t * t * (3 - 2 * t); }
        public static double BellyAt(double milliseconds)
        {
            if (milliseconds <= 0 || milliseconds >= BellyDuration) return 0;
            double t = milliseconds / 1000;
            return -.6 * Math.Exp(-3.2 * t) * Math.Sin(t * Math.PI * 5) * (1 - Ease(900, BellyDuration, milliseconds));
        }
        public static double RetreatAt(double time)
        { return .5 * Ease(100, 480, time) + .5 * Ease(530, 980, time); }
        public static double StepAt(double time, bool first)
        {
            double t = (time - (first ? 70 : 500)) / (first ? 430 : 500);
            return t <= 0 || t >= 1 ? 0 : Math.Pow(Math.Sin(t * Math.PI), 2);
        }
        public static double FlinchAt(double time)
        { return Ease(0, 110, time) * (1 - Ease(600, RetreatDuration, time)); }
    }

    // Hit-test the registered image pixels, not the transparent window rectangle.
    // The two reference lines divide the visible height into head, belly and
    // lower-body regions. Transparent pixels never count as a click target.
    internal sealed class PetHitMap
    {
        readonly byte[] pixels;
        readonly int width, height;
        readonly Rectangle body;
        internal const double BellyRegionTop = .34, RetreatRegionTop = .73;
        internal Rectangle Bounds { get { return body; } }
        public PetHitMap(Bitmap frame)
        { pixels = AnimationClip.ReadPixels(frame); width = frame.Width; height = frame.Height; body = AnimationClip.VisibleBounds(frame); }
        public bool Contains(PointF point)
        {
            int x = (int)Math.Floor(point.X), y = (int)Math.Floor(point.Y);
            return x >= 0 && y >= 0 && x < width && y < height && pixels[(y * width + x) * 4 + 3] >= 96;
        }
        public MousePetAction Hit(PointF point)
        {
            if (!Contains(point)) return MousePetAction.None;
            double v = ((int)point.Y - body.Top) / (double)body.Height;
            if (v < BellyRegionTop) return MousePetAction.None;
            return v < RetreatRegionTop ? MousePetAction.Belly : MousePetAction.Feet;
        }
    }
}
