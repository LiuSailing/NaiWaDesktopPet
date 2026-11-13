using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NaiwaDesktopPet
{
    // Cache eyelids from the existing texture; never redraw the body or alpha.
    internal sealed class BlinkRig : IDisposable
    {
        const int Steps = 12;
        readonly Bitmap[] frames = new Bitmap[Steps + 1];
        struct Eye
        {
            public double X, Y, Rx, Ry, Angle;
            public Eye(double x, double y, double rx, double ry, double angle)
            { X = x; Y = y; Rx = rx; Ry = ry; Angle = angle * Math.PI / 180; }
        }
        struct SkinSample { public double X, Y; public Color Color; }

        public BlinkRig(Image source, Bitmap openFrame, string state, Func<Image, Bitmap> register, string assetDirectory = null)
        {
            frames[0] = openFrame;
            Eye[] eyes = state == "rest"
                ? new[] { new Eye(315, 160, 53, 35, -40), new Eye(480, 75, 49, 23, 23) }
                : state == "listen"
                ? new[] { new Eye(337, 324, 58, 77, 9), new Eye(648, 258, 75, 77, -10) }
                : new[] { new Eye(414, 132, 17, 36, 17), new Eye(517, 125, 43, 40, 0) };
            using (var original = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(original))
                { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(source, 0, 0); }
                byte[] basis = AnimationClip.ReadPixels(original);
                string cacheFile = BlinkCache.PathFor(assetDirectory, state, basis, original.Size, openFrame);
                if (BlinkCache.TryLoad(cacheFile, frames)) return;
                var samples = new List<SkinSample>[eyes.Length];
                for (int e = 0; e < eyes.Length; e++) samples[e] = SampleSkin(original, eyes[e]);
                for (int step = 1; step <= Steps; step++)
                using (var closed = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb))
                {
                    byte[] pixels = (byte[])basis.Clone();
                    for (int e = 0; e < eyes.Length; e++) CloseEye(pixels, original.Width, original.Height, eyes[e], samples[e], (double)step / Steps);
                    var data = closed.LockBits(new Rectangle(0, 0, closed.Width, closed.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                    try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
                    finally { closed.UnlockBits(data); }
                    frames[step] = register(closed);
                }
                BlinkCache.Save(cacheFile, frames);
            }
        }

        static List<SkinSample> SampleSkin(Bitmap image, Eye eye)
        {
            var samples = new List<SkinSample>();
            for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < 24; i++)
            {
                double angle = i * Math.PI / 12, radius = 1.35 + ring * .35;
                int x = (int)Math.Round(eye.X + Math.Cos(angle) * eye.Rx * radius);
                int y = (int)Math.Round(eye.Y + Math.Sin(angle) * eye.Ry * radius);
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
                Color c = image.GetPixel(x, y);
                if (c.A > 240 && c.R > 150 && c.G > 85 && c.R > c.G * 1.06 && c.B < c.R * .65)
                    samples.Add(new SkinSample { X = x, Y = y, Color = c });
            }
            if (samples.Count < 3) throw new InvalidDataException("Cannot sample the skin around an eye.");
            return samples;
        }

        static double Smooth(double value)
        { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }

        static void CloseEye(byte[] pixels, int width, int height, Eye eye, List<SkinSample> skin, double closure)
        {
            int radius = (int)Math.Ceiling(Math.Max(eye.Rx, eye.Ry) * 1.15);
            double cos = Math.Cos(eye.Angle), sin = Math.Sin(eye.Angle);
            for (int y = Math.Max(0, (int)eye.Y - radius); y <= Math.Min(height - 1, (int)eye.Y + radius); y++)
            for (int x = Math.Max(0, (int)eye.X - radius); x <= Math.Min(width - 1, (int)eye.X + radius); x++)
            {
                double dx = x - eye.X, dy = y - eye.Y;
                double u = (dx * cos + dy * sin) / eye.Rx, v = (-dx * sin + dy * cos) / eye.Ry;
                double r = Math.Sqrt(u * u + v * v);
                if (r > 1.04 || Math.Abs(u) >= 1.04) continue;
                double edge = Math.Sqrt(Math.Max(0, 1 - u * u));
                double upper = -edge + closure * 1.20 * edge, lower = edge * (1 - closure * .80);
                double covered = Math.Max(Smooth((upper - v) * eye.Ry / 1.5 + .5), Smooth((v - lower) * eye.Ry / 1.5 + .5));
                double mask = Smooth((1.04 - r) / .055) * covered;
                if (closure >= .999) mask = Smooth((1.04 - r) / .055);
                if (mask <= 0) continue;
                int pixel = (y * width + x) * 4, alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                double red = 0, green = 0, blue = 0, total = 0;
                foreach (var sample in skin)
                {
                    double distance = Math.Pow(x - sample.X, 2) + Math.Pow(y - sample.Y, 2) + 64;
                    double weight = 1 / (distance * distance);
                    total += weight; red += sample.Color.R * weight; green += sample.Color.G * weight; blue += sample.Color.B * weight;
                }
                red /= total; green /= total; blue /= total;
                // The yellow lids meet at a shallow shaded crease.
                double seam = .20 * edge;
                double crease = Math.Exp(-Math.Pow((v - seam) * eye.Ry / 1.5, 2)) * Smooth((closure - .75) / .25) * Math.Max(0, 1 - Math.Pow(Math.Abs(u), 6));
                red *= 1 - .45 * crease; green *= 1 - .56 * crease; blue *= 1 - .45 * crease;
                double a = alpha / 255.0;
                pixels[pixel] = (byte)Math.Round(pixels[pixel] * (1 - mask) + blue * a * mask);
                pixels[pixel + 1] = (byte)Math.Round(pixels[pixel + 1] * (1 - mask) + green * a * mask);
                pixels[pixel + 2] = (byte)Math.Round(pixels[pixel + 2] * (1 - mask) + red * a * mask);
            }
        }

        public Bitmap At(double closure) { return frames[(int)Math.Round(Math.Max(0, Math.Min(1, closure)) * Steps)]; }
        public string Validate(string state)
        {
            byte[] open = AnimationClip.ReadPixels(frames[0]), shut = AnimationClip.ReadPixels(frames[Steps]);
            Rectangle visible = AnimationClip.VisibleBounds(frames[0]);
            int changes = 0;
            for (int p = 0; p < open.Length; p += 4)
            {
                if (open[p + 3] != shut[p + 3]) throw new InvalidDataException(state + " blink changed the silhouette.");
                if (open[p] != shut[p] || open[p + 1] != shut[p + 1] || open[p + 2] != shut[p + 2])
                {
                    if (p / 4 / frames[0].Width > visible.Top + visible.Height * .36) throw new InvalidDataException(state + " blink changed the body.");
                    changes++;
                }
            }
            if (changes < 10) throw new InvalidDataException(state + " blink is missing.");
            if (BlinkMotion.ClosureAt(0) != 0 || BlinkMotion.ClosureAt(95) != 1 || BlinkMotion.ClosureAt(260) != 0)
                throw new InvalidDataException("Blink must close fully and reopen.");
            return state + ": 13 cached eyelid poses, full close/reopen in 260 ms; silhouette and body preserved (" + changes + " changed face pixels).";
        }
        public void Dispose() { for (int i = 1; i < frames.Length; i++) if (frames[i] != null) frames[i].Dispose(); }
    }

    internal sealed class BlinkMotion
    {
        readonly Random random = new Random();
        long elapsed, start = 2200;
        public double Closure { get { return ClosureAt(elapsed - start); } }
        public static double ClosureAt(long time)
        {
            if (time < 0 || time >= 260) return 0;
            double t;
            if (time < 75) { t = time / 75.0; return t * t * (3 - 2 * t); }
            if (time < 110) return 1;
            t = (time - 110) / 150.0; return 1 - t * t * (3 - 2 * t);
        }
        public void Advance(int delta)
        {
            elapsed += delta;
            if (elapsed >= start + 260) start = elapsed + random.Next(2800, 5200);
        }
    }
}
