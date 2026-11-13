using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NaiwaDesktopPet
{
    public struct RestPose
    {
        public double Rise, Angle, Lift, Inflation, Eyes, Exhale, Mouth;
    }

    // A one-shot release of tension followed by quiet abdominal breathing.
    // The inverse warp samples one intact bitmap, without body-piece seams.
    public sealed class RestRig : IDisposable
    {
        public const int MinimumDuration = 8000;
        readonly Bitmap canvas;
        readonly byte[][] textures = new byte[13][];
        readonly byte[] output;
        readonly double[] weights;
        readonly int width, height;
        readonly Rectangle belly;
        readonly PointF mouth;
        readonly ExhaleMouthRig mouthRig;
        readonly double centerX;
        readonly PointF[] corners = new PointF[3];
        readonly Pen air = new Pen(Color.White, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        readonly Pen airEdge = new Pen(Color.Gray, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        public RestRig(AnimationClip clip)
        {
            if (!clip.UsesHdRestPose || !clip.HasBlink) throw new InvalidDataException("A blinking HD supine pose is required.");
            width = clip.Frame.Width; height = clip.Frame.Height;
            for (int i = 0; i < textures.Length; i++) textures[i] = AnimationClip.ReadPixels(clip.WithBlink(i / 12.0));
            output = new byte[width * height * 4]; weights = new double[width * height];
            canvas = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            Rectangle bounds = AnimationClip.VisibleBounds(clip.Frame);
            centerX = bounds.Left + bounds.Width * .50;
            double cy = bounds.Top + bounds.Height * .39, rx = bounds.Width * .32, ry = bounds.Height * .31;
            belly = Rectangle.Intersect(new Rectangle(0, 0, width, height),
                Rectangle.FromLTRB((int)Math.Floor(centerX - rx), (int)Math.Floor(cy - ry), (int)Math.Ceiling(centerX + rx), (int)Math.Ceiling(cy + ry)));
            for (int y = belly.Top; y < belly.Bottom; y++)
            for (int x = belly.Left; x < belly.Right; x++)
            {
                double r = Math.Pow((x - centerX) / rx, 2) + Math.Pow((y - cy) / ry, 2);
                double w = Math.Max(0, 1 - r);
                weights[y * width + x] = w * w * w;
            }
            mouthRig = new ExhaleMouthRig(clip.Frame, clip.RestExhaleFrame);
            mouth = mouthRig.Origin;
        }

        static double Smooth(double a, double b, double time)
        { double t = Math.Max(0, Math.Min(1, (time - a) / (b - a))); return t * t * t * (t * (t * 6 - 15) + 10); }

        public static RestPose At(double milliseconds)
        {
            double t = Math.Max(0, milliseconds);
            double settle = 1 - Smooth(0, 700, t);
            double inhale = Smooth(450, 1150, t), release = 1 - Smooth(1150, 3350, t);
            double deepBreath = inhale * release;
            double quiet = t <= 3350 ? 0 : .5 - .5 * Math.Cos((t - 3350) * Math.PI * 2 / 4000);
            double landing = t < 650 || t > 1050 ? 0 : .7 * Math.Pow(Math.Sin((t - 650) * Math.PI / 400), 2);
            return new RestPose {
                Rise = 12 * settle - landing, Angle = -3.5 * settle,
                Lift = 3.0 * deepBreath + .38 * quiet,
                Inflation = .026 * deepBreath + .004 * quiet,
                Eyes = Smooth(300, 850, t) * (1 - Smooth(3350, 3950, t)),
                Exhale = Smooth(1100, 1450, t) * (1 - Smooth(2900, 3350, t)),
                Mouth = Smooth(950, 1400, t) * (1 - Smooth(2780, 3400, t))
            };
        }

        public static string CaptionAt(long time)
        {
            if (time < 1150) return "忙完了，躺会儿～";
            if (time < 3350) return "呼——";
            return "舒服了，歇会儿～";
        }

        double WeightAt(double x, double y)
        {
            int ix = (int)x, iy = (int)y;
            if (x < 0 || y < 0 || ix >= width - 1 || iy >= height - 1) return 0;
            double fx = x - ix, fy = y - iy;
            int p = iy * width + ix;
            return (weights[p] * (1 - fx) + weights[p + 1] * fx) * (1 - fy)
                + (weights[p + width] * (1 - fx) + weights[p + width + 1] * fx) * fy;
        }

        void RenderTexture(RestPose pose, double blink)
        {
            int index = (int)Math.Round(Math.Max(pose.Eyes, Math.Max(0, Math.Min(1, blink))) * 12);
            byte[] texture = textures[index];
            Buffer.BlockCopy(texture, 0, output, 0, output.Length);
            double lift = pose.Lift * width / 240.0;
            for (int y = belly.Top; y < belly.Bottom; y++)
            for (int x = belly.Left; x < belly.Right; x++)
            {
                double weight = weights[y * width + x];
                if (weight == 0) continue;
                double u = x, v = y;
                // Small displacements converge rapidly; both alpha and colors
                // are sampled together in premultiplied space.
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    u = x - (u - centerX) * pose.Inflation * weight;
                    v = y + lift * weight;
                    weight = WeightAt(u, v);
                }
                int sx = (int)u, sy = (int)v;
                if (sx < 0 || sy < 0 || sx >= width - 1 || sy >= height - 1) continue;
                int fx = (int)((u - sx) * 256), fy = (int)((v - sy) * 256);
                int a = (256 - fx) * (256 - fy), b = fx * (256 - fy), c = (256 - fx) * fy, d = fx * fy;
                int p = (sy * width + sx) * 4, q = p + width * 4, destination = (y * width + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                    output[destination + channel] = (byte)((texture[p + channel] * a + texture[p + 4 + channel] * b
                        + texture[q + channel] * c + texture[q + 4 + channel] * d + 32768) >> 16);
            }
            mouthRig.Apply(output, pose.Mouth);
            var bits = canvas.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(output, 0, bits.Scan0, output.Length); }
            finally { canvas.UnlockBits(bits); }
        }

        public void Draw(Graphics graphics, long time, RectangleF target, double blink, ImageAttributes attributes)
        {
            RestPose pose = At(time); RenderTexture(pose, blink);
            GraphicsState state = graphics.Save();
            try
            {
                float pivotX = target.Left + target.Width * .53f, pivotY = target.Top + target.Height * .78f;
                graphics.TranslateTransform(pivotX, pivotY - (float)pose.Rise);
                graphics.RotateTransform((float)pose.Angle); graphics.TranslateTransform(-pivotX, -pivotY);
                corners[0] = new PointF(target.Left, target.Top); corners[1] = new PointF(target.Right, target.Top);
                corners[2] = new PointF(target.Left, target.Bottom);
                graphics.DrawImage(canvas, corners, new RectangleF(0, 0, width, height), GraphicsUnit.Pixel, attributes);
                if (pose.Exhale > .001) DrawAir(graphics, time, target, pose.Exhale * Math.Min(1, pose.Mouth * 1.8));
            }
            finally { graphics.Restore(state); }
        }

        void DrawAir(Graphics graphics, long time, RectangleF target, double amount)
        {
            float mx = target.Left + mouth.X * target.Width / width, my = target.Top + mouth.Y * target.Height / height;
            for (int i = 0; i < 4; i++)
            {
                double age = (time - 1150 - i * 210) / 1500.0;
                if (age <= 0 || age >= 1) continue;
                float distance = (float)(4 + 36 * age), radius = (float)(2.5 + 4 * age);
                float x = mx + distance * .76f, y = my - distance * .65f;
                int alpha = (int)(95 * Math.Sin(age * Math.PI) * amount);
                air.Color = Color.FromArgb(alpha, 228, 245, 239);
                airEdge.Color = Color.FromArgb(alpha / 5, 80, 111, 111);
                // Thin, drifting breath marks rather than an opaque smoke cloud.
                graphics.DrawBezier(airEdge, x - radius, y - radius * .8f, x + radius * 1.5f, y - radius, x - radius * .3f, y + radius, x + radius, y + radius * .7f);
                graphics.DrawBezier(air, x - radius, y - radius * .8f, x + radius * 1.5f, y - radius, x - radius * .3f, y + radius, x + radius, y + radius * .7f);
            }
        }

        public string ValidateMotion()
        {
            if (At(0).Rise < 10 || Math.Abs(At(1100).Rise) > .001 || At(1150).Lift < 2.9)
                throw new InvalidOperationException("Rest must settle before its deep breath.");
            double previous = At(1150).Lift;
            for (int t = 1160; t <= 3350; t += 10)
            {
                RestPose pose = At(t);
                if (pose.Lift > previous + .00001 || pose.Eyes < .8) throw new InvalidOperationException("The sigh must release slowly with relaxed eyes.");
                previous = pose.Lift;
            }
            if (At(8000).Exhale != 0 || At(20000).Exhale != 0 || At(8000).Eyes != 0)
                throw new InvalidOperationException("The sigh must not loop during quiet rest.");
            if (At(900).Mouth != 0 || At(1800).Mouth < .99 || At(3500).Mouth != 0)
                throw new InvalidOperationException("Mouth must open for exhalation and close afterwards.");
            mouthRig.Validate();
            RenderTexture(new RestPose(), 0);
            byte[] original = textures[0];
            for (int p = 0; p < output.Length; p++)
                if (output[p] != original[p]) throw new InvalidOperationException("Neutral rest warp changed the source texture.");
            return "Rest sigh passed: settle, deep inhale, 2.2-second release with closed eyes, contracting lips and O mouth, air from the mouth, then closed-mouth quiet breathing; intact body/neutral texture.";
        }

        public void Dispose() { canvas.Dispose(); air.Dispose(); airEdge.Dispose(); }
    }

    // Only a feathered mouth patch is used from the AI-edited image. Its body,
    // head silhouette and eyes can never overwrite the established pet.
    internal sealed class ExhaleMouthRig
    {
        const int Steps = 40;
        readonly byte[][] patches = new byte[Steps + 1][];
        readonly double[] mask;
        readonly Rectangle region;
        readonly int width, height;
        readonly byte[] neutral;
        readonly double cos = Math.Cos(-27 * Math.PI / 180), sin = Math.Sin(-27 * Math.PI / 180);
        readonly PointF origin;
        public PointF Origin { get { return origin; } }

        public ExhaleMouthRig(Bitmap basis, Bitmap exhale)
        {
            if (exhale == null || exhale.Size != basis.Size) throw new InvalidDataException("Aligned exhale mouth texture is missing.");
            width = basis.Width; height = basis.Height;
            neutral = AnimationClip.ReadPixels(basis);
            byte[] peak = AnimationClip.ReadPixels(exhale);
            Rectangle bounds = AnimationClip.VisibleBounds(basis);
            Rectangle query = Rectangle.FromLTRB(bounds.Left + (int)(bounds.Width * .20), bounds.Top + (int)(bounds.Height * .085),
                bounds.Left + (int)(bounds.Width * .33), bounds.Top + (int)(bounds.Height * .20));
            Rectangle closed = FindMouth(neutral, query), opened = FindMouth(peak, query);
            origin = new PointF(closed.Left + (closed.Width - 1) / 2f, closed.Top + (closed.Height - 1) / 2f);
            PointF peakOrigin = new PointF(opened.Left + (opened.Width - 1) / 2f, opened.Top + (opened.Height - 1) / 2f);
            double closedSpan = Math.Max(5, closed.Width * cos + closed.Height * Math.Abs(sin));
            double openSpan = Math.Max(3, opened.Width * cos + opened.Height * Math.Abs(sin));
            double rx = closedSpan * .72 + 2, ry = Math.Max(5, closedSpan * .33);
            int radius = (int)Math.Ceiling(rx + ry);
            region = Rectangle.Intersect(new Rectangle(0, 0, width, height), Rectangle.FromLTRB((int)origin.X - radius,
                (int)origin.Y - radius, (int)origin.X + radius + 1, (int)origin.Y + radius + 1));
            mask = new double[region.Width * region.Height];
            for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
            {
                double dx = x - origin.X, dy = y - origin.Y;
                double u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
                double r = Math.Sqrt(u * u / (rx * rx) + v * v / (ry * ry));
                mask[(y - region.Top) * region.Width + x - region.Left] = Ease((1 - r) / .27);
            }
            for (int step = 0; step <= Steps; step++)
            {
                double t = step / (double)Steps;
                double closedScale = 1 + (openSpan * 1.25 / closedSpan - 1) * t;
                double peakScaleX = closedSpan / openSpan + (1.25 - closedSpan / openSpan) * t;
                double peakScaleY = .10 + 1.15 * t;
                double blend = Ease(t);
                byte[] patch = patches[step] = new byte[mask.Length * 4];
                for (int y = region.Top; y < region.Bottom; y++)
                for (int x = region.Left; x < region.Right; x++)
                {
                    int p = (y * width + x) * 4, q = ((y - region.Top) * region.Width + x - region.Left) * 4;
                    double feather = mask[q / 4];
                    double dx = x - origin.X, dy = y - origin.Y;
                    double u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
                    double oldX = origin.X + u / closedScale * cos - v * sin;
                    double oldY = origin.Y + u / closedScale * sin + v * cos;
                    // Outside the lip edge, extend nearby yellow skin rather
                    // than pulling an eye into the patch as the aperture closes.
                    double safeX = Math.Max(6, openSpan * 1.2), safeY = Math.Max(4, opened.Height * 1.1 + 1);
                    double newU = Math.Max(-safeX, Math.Min(safeX, u / peakScaleX));
                    double newV = Math.Max(-safeY, Math.Min(safeY, v / peakScaleY));
                    double newX = peakOrigin.X + newU * cos - newV * sin;
                    double newY = peakOrigin.Y + newU * sin + newV * cos;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double oldColor = Sample(neutral, oldX, oldY, channel), newColor = Sample(peak, newX, newY, channel);
                        double color = oldColor + (newColor - oldColor) * blend;
                        patch[q + channel] = (byte)Math.Round(neutral[p + channel] * (1 - feather) + color * feather);
                    }
                    patch[q + 3] = neutral[p + 3];
                }
            }
        }

        static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        Rectangle FindMouth(byte[] pixels, Rectangle query)
        {
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = query.Top; y < query.Bottom; y++)
            for (int x = query.Left; x < query.Right; x++)
            {
                int p = (y * width + x) * 4;
                if (pixels[p + 3] > 230 && pixels[p + 2] < 170 && pixels[p + 1] < 110 && pixels[p] < 100)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            }
            if (right < left) throw new InvalidDataException("Cannot locate the mouth for exhalation.");
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        double Sample(byte[] texture, double x, double y, int channel)
        {
            x = Math.Max(0, Math.Min(width - 1.001, x)); y = Math.Max(0, Math.Min(height - 1.001, y));
            int ix = (int)x, iy = (int)y, p = (iy * width + ix) * 4;
            double fx = x - ix, fy = y - iy;
            return (texture[p + channel] * (1 - fx) + texture[p + 4 + channel] * fx) * (1 - fy)
                + (texture[p + width * 4 + channel] * (1 - fx) + texture[p + width * 4 + 4 + channel] * fx) * fy;
        }

        public void Apply(byte[] pixels, double amount)
        {
            if (amount <= .00001) return;
            byte[] patch = patches[(int)Math.Round(Math.Max(0, Math.Min(1, amount)) * Steps)];
            for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
            {
                int q = ((y - region.Top) * region.Width + x - region.Left) * 4;
                if (mask[q / 4] <= 0) continue;
                int p = (y * width + x) * 4;
                pixels[p] = patch[q]; pixels[p + 1] = patch[q + 1]; pixels[p + 2] = patch[q + 2];
            }
        }

        public void Validate()
        {
            byte[] opened = (byte[])neutral.Clone(); Apply(opened, 1);
            int changed = 0;
            for (int p = 0; p < neutral.Length; p += 4)
            {
                if (opened[p + 3] != neutral[p + 3]) throw new InvalidOperationException("Mouth changed the character silhouette.");
                if (opened[p] != neutral[p] || opened[p + 1] != neutral[p + 1] || opened[p + 2] != neutral[p + 2])
                {
                    if (!region.Contains(p / 4 % width, p / 4 / width)) throw new InvalidOperationException("Mouth changed pixels outside its patch.");
                    changed++;
                }
            }
            if (changed < 12) throw new InvalidOperationException("Exhale mouth is not visibly animated.");
        }
    }
}
