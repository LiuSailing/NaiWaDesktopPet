using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NaiwaDesktopPet
{
    public struct LaughPose { public double BodyAngle, HeadAngle, Knee, Bounce, Belly, Strength, Poke, LeftStep, RightStep; }

    // A single connected UV surface. Every sample overwrites one buffer;
    // there are no separately clipped/composited body strips or cutout joints.
    public sealed class LaughRig : IDisposable
    {
        public const int Duration = 6000;
        const int Antialias = 2;
        static readonly double[,] Keys = {
            // ms, spine angle, extra head angle, knee flex, expression
            { 0, 0, 0, 0, 0 }, { 240, 1.5, -2, .5, .20 },
            { 620, 4, 12, .4, 1 }, { 1050, -5, -9, 3.2, 1 },
            { 1590, -6, -10, 3.8, 1 }, { 1980, 2, 7, 1, .9 },
            { 2450, -6, -10, 4.2, 1 }, { 3220, -5, -8, 3.2, 1 },
            { 3700, 3, 9, .8, .9 }, { 4250, -6, -10, 3.8, 1 },
            { 4800, -3, -5, 2, .9 }, { 5350, 1, 2, .5, .55 },
            { 6000, 0, 0, 0, 0 }
        };
        struct Vertex { public double X, Y, Body, Head, Upper, Abdomen, Pressure, LeftFoot, RightFoot; }
        readonly Bitmap canvas;
        readonly byte[][] expressions = new byte[25][];
        readonly byte[][] neutralBlinks = new byte[13][];
        readonly byte[] output;
        readonly Vertex[] vertices;
        readonly PointF[] mapped;
        readonly Rectangle bounds;
        readonly int columns, rows, width, height, edge;
        readonly double hipX, hipY, neckX, neckY;
        readonly PointF[] destination = new PointF[3];

        public LaughRig(Bitmap idleFrame, Bitmap peakFrame, Bitmap closedEyes, Func<double, Bitmap> eyelids = null)
        {
            if (idleFrame.Size != peakFrame.Size || idleFrame.Size != closedEyes.Size)
                throw new InvalidDataException("Rig textures must share a canvas.");
            width = idleFrame.Width; height = idleFrame.Height;
            bounds = AnimationClip.VisibleBounds(idleFrame);
            hipX = bounds.Left + bounds.Width * .55; hipY = bounds.Top + bounds.Height * .69;
            neckX = bounds.Left + bounds.Width * .44; neckY = bounds.Top + bounds.Height * .25;
            byte[] neutral = AnimationClip.ReadPixels(idleFrame), blink = AnimationClip.ReadPixels(closedEyes), peak = AnimationClip.ReadPixels(peakFrame);
            for (int i = 0; i < neutralBlinks.Length; i++)
                neutralBlinks[i] = i == 0 ? neutral : i == 12 ? blink : eyelids == null ? neutral : AnimationClip.ReadPixels(eyelids(i / 12.0));
            for (int i = 0; i < expressions.Length; i++)
            {
                expressions[i] = new byte[neutral.Length];
                double phase = i / 24.0;
                if (phase <= .24 && eyelids != null)
                {
                    expressions[i] = AnimationClip.ReadPixels(eyelids(phase / .24));
                    continue;
                }
                byte[] a = phase <= .24 ? neutral : blink, b = phase <= .24 ? blink : peak;
                // Keep a solid expression during each burst and breath. Only
                // the short onset/recovery blends, avoiding a faded-sticker face.
                double t = phase <= .24 ? phase / .24 : (phase - .24) / .28;
                int weight = (int)Math.Round(Smooth01(t) * 256);
                for (int p = 0; p < neutral.Length; p++)
                    expressions[i][p] = (byte)((a[p] * (256 - weight) + b[p] * weight + 128) >> 8);
            }
            columns = (int)Math.Ceiling((bounds.Width + 4) / 4.0) + 1;
            rows = (int)Math.Ceiling((bounds.Height + 4) / 4.0) + 1;
            vertices = new Vertex[columns * rows]; mapped = new PointF[vertices.Length];
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                double x = bounds.Left - 2 + (bounds.Width + 4.0) * col / (columns - 1);
                double y = bounds.Top - 2 + (bounds.Height + 4.0) * row / (rows - 1);
                double v = (y - bounds.Top) / bounds.Height;
                double u = (x - bounds.Left) / bounds.Width;
                vertices[row * columns + col] = new Vertex { X = x, Y = y,
                    Body = 1 - Smooth(.47, .94, v), Head = 1 - Smooth(.20, .40, v),
                    Upper = 1 - Smooth(.57, .94, v),
                    Abdomen = Math.Exp(-Math.Pow((v - .49) / .20, 2)) * (1 - Smooth(.68, .90, v)),
                    // Press the front of the belly; ease into the shoulders and
                    // leave the back contour nearly fixed instead of pinching it.
                    Pressure = Smooth(.31, .48, v) * (1 - Smooth(.63, .82, v)) * (1 - .90 * Smooth(.48, .92, u)),
                    LeftFoot = (1 - Smooth(.33, .65, u)) * Smooth(.76, .96, v),
                    RightFoot = Smooth(.33, .65, u) * Smooth(.76, .96, v) };
            }
            edge = width * Antialias;
            canvas = new Bitmap(edge, height * Antialias, PixelFormat.Format32bppPArgb);
            output = new byte[edge * canvas.Height * 4];
        }

        static double Smooth01(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        static double Smooth(double a, double b, double t) { return Smooth01((t - a) / (b - a)); }
        static double Mix(double a, double b, double t) { return a + (b - a) * t; }
        static double Burst(double t, double start, double end)
        { return Smooth(start, start + .16, t) * (1 - Smooth(end - .18, end, t)); }
        static double Ha(double t)
        {
            double envelope = Math.Max(Burst(t, .72, 1.65), Math.Max(Burst(t, 2.12, 3.36), Burst(t, 3.94, 4.94)));
            double wave = .5 + .5 * Math.Sin(t * Math.PI * 2 * 3.7);
            return envelope * wave * wave;
        }
        public static LaughPose At(double milliseconds)
        {
            double time = Math.Max(0, milliseconds) % Duration, seconds = time / 1000;
            int index = 0;
            while (index < Keys.GetLength(0) - 2 && time >= Keys[index + 1, 0]) index++;
            double t = Smooth01((time - Keys[index, 0]) / (Keys[index + 1, 0] - Keys[index, 0]));
            double pulse = Ha(seconds), shoulder = Ha(seconds - .035);
            return new LaughPose {
                BodyAngle = Mix(Keys[index, 1], Keys[index + 1, 1], t) - .8 * shoulder,
                HeadAngle = Mix(Keys[index, 2], Keys[index + 1, 2], t) - .65 * Ha(seconds - .065),
                Knee = Mix(Keys[index, 3], Keys[index + 1, 3], t) + .65 * pulse,
                Bounce = 1.45 * shoulder,
                Belly = -.014 * pulse + .006 * Math.Sin(seconds * Math.PI * 2 / 2.6) * Math.Sin(Math.PI * time / Duration),
                Strength = Mix(Keys[index, 4], Keys[index + 1, 4], t)
            };
        }

        public static LaughPose DuringPlayback(double milliseconds, int position, int duration)
        {
            // Repeat only the laughing part. The original six-second clip also
            // contains recovery to idle, which must not recur inside one sound.
            double phase = Math.Max(0, milliseconds) % 4800;
            LaughPose pose = At(620 + Math.Min(phase, 4180));
            if (phase > 4180)
            {
                LaughPose next = At(620);
                double blend = Smooth01((phase - 4180) / 620);
                pose = new LaughPose {
                    BodyAngle = Mix(pose.BodyAngle, next.BodyAngle, blend),
                    HeadAngle = Mix(pose.HeadAngle, next.HeadAngle, blend),
                    Knee = Mix(pose.Knee, next.Knee, blend),
                    Bounce = Mix(pose.Bounce, next.Bounce, blend),
                    Belly = Mix(pose.Belly, next.Belly, blend),
                    Strength = Mix(pose.Strength, next.Strength, blend)
                };
            }
            double onset = Smooth(0, 450, milliseconds);
            double release = Smooth(Math.Max(0, duration - 450), duration, position);
            double motion = onset * (1 - .86 * release);
            pose.BodyAngle *= motion; pose.HeadAngle *= motion;
            pose.Knee *= motion; pose.Bounce *= motion; pose.Belly *= motion;
            // Keep the laughing face and a little motion until actual playback
            // completes, including any output buffering beyond the MP3 duration.
            pose.Strength *= onset * (1 - .25 * release);
            return pose;
        }

        PointF Map(Vertex v, LaughPose pose, double bc, double bs, double hc, double hs)
        {
            double scale = width / 240.0;
            // Spine carries shoulders/arms. Knee flex absorbs the torso weight;
            // influence fades to zero before the soles.
            double dx = v.X - hipX, dy = v.Y - hipY;
            double x = v.X + (bc * dx - bs * dy - dx) * v.Body;
            double y = v.Y + (bs * dx + bc * dy - dy) * v.Body;
            double nx = hipX + bc * (neckX - hipX) - bs * (neckY - hipY);
            double ny = hipY + bs * (neckX - hipX) + bc * (neckY - hipY);
            dx = x - nx; dy = y - ny;
            x += (hc * dx - hs * dy - dx) * v.Head;
            y += (hs * dx + hc * dy - dy) * v.Head;
            x += (v.X - hipX) * pose.Belly * v.Abdomen;
            y += scale * (pose.Knee * v.Upper + pose.Bounce * v.Body + pose.Belly * 45 * v.Abdomen);
            x += (v.X - (bounds.Left + bounds.Width * .46)) * pose.Poke * .34 * v.Pressure;
            y -= (v.Y - (bounds.Top + bounds.Height * .50)) * pose.Poke * .14 * v.Pressure;
            double step = pose.LeftStep * v.LeftFoot + pose.RightStep * v.RightFoot;
            x += scale * step * 5; y -= scale * step * 8;
            return new PointF((float)(x * Antialias), (float)(y * Antialias));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Sample(byte[] texture, int destinationIndex, double u, double v)
        {
            int x = (int)u, y = (int)v;
            if (u < 0 || v < 0 || x >= width - 1 || y >= height - 1) return;
            int fx = (int)((u - x) * 256), fy = (int)((v - y) * 256);
            int a = (256 - fx) * (256 - fy), b = fx * (256 - fy), c = (256 - fx) * fy, d = fx * fy;
            int p = (y * width + x) * 4, q = p + width * 4;
            for (int channel = 0; channel < 4; channel++)
                output[destinationIndex + channel] = (byte)((texture[p + channel] * a + texture[p + 4 + channel] * b
                    + texture[q + channel] * c + texture[q + 4 + channel] * d + 32768) >> 16);
        }

        void Triangle(int ai, int bi, int ci, byte[] texture)
        {
            PointF a = mapped[ai], b = mapped[bi], c = mapped[ci];
            double det = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (det <= 0) throw new InvalidOperationException("Laugh deformation folded the character surface.");
            int left = Math.Max(0, (int)Math.Ceiling(Math.Min(a.X, Math.Min(b.X, c.X)) - .5));
            int right = Math.Min(edge - 1, (int)Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) - .5));
            int top = Math.Max(0, (int)Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - .5));
            int bottom = Math.Min(canvas.Height - 1, (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - .5));
            double bx = (c.Y - a.Y) / det, by = -(c.X - a.X) / det;
            double cx = -(b.Y - a.Y) / det, cy = (b.X - a.X) / det;
            Vertex av = vertices[ai], bv = vertices[bi], cv = vertices[ci];
            double du = (bv.X - av.X) * bx + (cv.X - av.X) * cx;
            double dv = (bv.Y - av.Y) * bx + (cv.Y - av.Y) * cx;
            for (int y = top; y <= bottom; y++)
            {
                double wb = (left + .5 - a.X) * bx + (y + .5 - a.Y) * by;
                double wc = (left + .5 - a.X) * cx + (y + .5 - a.Y) * cy;
                double u = av.X + (bv.X - av.X) * wb + (cv.X - av.X) * wc;
                double v = av.Y + (bv.Y - av.Y) * wb + (cv.Y - av.Y) * wc;
                int dst = (y * edge + left) * 4;
                for (int x = left; x <= right; x++, dst += 4, wb += bx, wc += cx, u += du, v += dv)
                    if (wb >= -.000001 && wc >= -.000001 && wb + wc <= 1.000001) Sample(texture, dst, u, v);
            }
        }

        void RenderTexture(LaughPose pose, byte[] texture = null)
        {
            double body = pose.BodyAngle * Math.PI / 180, head = pose.HeadAngle * Math.PI / 180;
            double bc = Math.Cos(body), bs = Math.Sin(body), hc = Math.Cos(head), hs = Math.Sin(head);
            for (int i = 0; i < vertices.Length; i++) mapped[i] = Map(vertices[i], pose, bc, bs, hc, hs);
            Array.Clear(output, 0, output.Length);
            if (texture == null) texture = expressions[(int)Math.Round(Math.Max(0, Math.Min(1, pose.Strength)) * 24)];
            for (int row = 0; row < rows - 1; row++)
            for (int col = 0; col < columns - 1; col++)
            {
                int a = row * columns + col, b = a + 1, c = a + columns, d = c + 1;
                Triangle(a, b, c, texture); Triangle(b, d, c, texture);
            }
            var bits = canvas.LockBits(new Rectangle(0, 0, canvas.Width, canvas.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(output, 0, bits.Scan0, output.Length); }
            finally { canvas.UnlockBits(bits); }
        }

        public void Draw(Graphics graphics, long time, RectangleF target, ImageAttributes attributes)
        { Draw(graphics, At(time), target, attributes); }

        public void DrawPlayback(Graphics graphics, long time, int position, int duration, RectangleF target, ImageAttributes attributes)
        { Draw(graphics, DuringPlayback(time, position, duration), target, attributes); }

        internal void DrawMouseReaction(Graphics graphics, MousePetAction action, long time, double blink, RectangleF target, ImageAttributes attributes)
        {
            var pose = MousePose(action, time);
            Draw(graphics, pose, target, attributes, neutralBlinks[(int)Math.Round(Math.Max(0, Math.Min(1, blink)) * 12)]);
        }
        internal static LaughPose MousePose(MousePetAction action, long time)
        {
            if (action == MousePetAction.Belly) return new LaughPose { Poke = MouseMotion.BellyAt(time) };
            double flinch = MouseMotion.FlinchAt(time), left = MouseMotion.StepAt(time, true), right = MouseMotion.StepAt(time, false);
            return new LaughPose { BodyAngle = 4 * flinch, HeadAngle = 2 * flinch, Knee = Math.Max(left, right), LeftStep = left, RightStep = right };
        }

        void Draw(Graphics graphics, LaughPose pose, RectangleF target, ImageAttributes attributes, byte[] texture = null)
        {
            RenderTexture(pose, texture);
            destination[0] = new PointF(target.Left, target.Top); destination[1] = new PointF(target.Right, target.Top);
            destination[2] = new PointF(target.Left, target.Bottom);
            graphics.DrawImage(canvas, destination, new RectangleF(0, 0, canvas.Width, canvas.Height), GraphicsUnit.Pixel, attributes);
        }

        public string ValidateMotion()
        {
            double minimum = 1000, maximum = -1000, minArea = Double.MaxValue, kneeRange = 0;
            for (int time = 0; time <= Duration; time += 10)
            {
                LaughPose pose = At(time);
                minimum = Math.Min(minimum, pose.BodyAngle + pose.HeadAngle); maximum = Math.Max(maximum, pose.BodyAngle + pose.HeadAngle);
                kneeRange = Math.Max(kneeRange, pose.Knee);
                minArea = Math.Min(minArea, ValidateSurface(pose));
            }
            if (minArea <= 0 || maximum - minimum < 30 || kneeRange < 3) throw new InvalidOperationException("Invalid full-body laugh motion.");
            LaughPose start = At(0), end = At(Duration - .001);
            if (Math.Abs(end.BodyAngle) + Math.Abs(end.HeadAngle) + end.Strength > .001 || start.Strength != 0)
                throw new InvalidOperationException("Laugh must return to idle.");
            // Include a delayed decoder start and a buffered tail. Neither may
            // cause a neutral face, a stopped body, or a hard cycle boundary.
            LaughPose previous = DuringPlayback(0, 0, 13401);
            double tailMotion = 0;
            for (int time = 10; time <= 14400; time += 10)
            {
                int position = Math.Max(0, Math.Min(13401, time - 750));
                LaughPose pose = DuringPlayback(time, position, 13401);
                if (time >= 450 && pose.Strength < .67)
                    throw new InvalidOperationException("Laughing face recovered before the sound ended.");
                if (Math.Abs(pose.BodyAngle - previous.BodyAngle) + Math.Abs(pose.HeadAngle - previous.HeadAngle) > 2)
                    throw new InvalidOperationException("Playback laugh has a discontinuous cycle boundary.");
                if (position == 13401) tailMotion += Math.Abs(pose.BodyAngle - previous.BodyAngle) + Math.Abs(pose.HeadAngle - previous.HeadAngle);
                if (ValidateSurface(pose) <= 0) throw new InvalidOperationException("Playback laugh folded the character surface.");
                previous = pose;
            }
            if (tailMotion < .01) throw new InvalidOperationException("Laugh froze while audio was still active.");
            double maximumBackShift = 0;
            foreach (var action in new[] { MousePetAction.Belly, MousePetAction.Feet })
            for (int time = 0; time <= MouseMotion.BellyDuration; time += 10)
            {
                LaughPose pose = MousePose(action, time);
                if (ValidateSurface(pose, action == MousePetAction.Belly) <= 0)
                    throw new InvalidOperationException("Mouse reaction folded the character surface.");
                if (action == MousePetAction.Belly)
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        if (vertices[i].Head > .999 && (Math.Abs(mapped[i].X - vertices[i].X * Antialias) > .001
                            || Math.Abs(mapped[i].Y - vertices[i].Y * Antialias) > .001))
                            throw new InvalidOperationException("Belly poke moved the head.");
                        if (vertices[i].X >= bounds.Left + bounds.Width * .88)
                            maximumBackShift = Math.Max(maximumBackShift, Math.Abs(mapped[i].X / Antialias - vertices[i].X) * 240 / width);
                    }
            }
            if (maximumBackShift > .9) throw new InvalidOperationException("Belly poke pinched the back contour.");
            return "Laugh passed: connected single-pass UV surface, 2x antialiasing; no folded cells; head/spine/arms/abdomen/knees coordinated in three laughter bursts; feet planted; angle range "
                + (maximum - minimum).ToString("F1") + " degrees; silent clip starts/ends idle; audio performance stays laughing across cycles, decoder delay and buffered tail."
                + " Mouse reactions passed: reduced belly spring, fixed head/feet, back contour shift " + maximumBackShift.ToString("F2") + " logical px.";
        }
        double ValidateSurface(LaughPose pose, bool plantedFeet = true)
        {
            double b = pose.BodyAngle * Math.PI / 180, h = pose.HeadAngle * Math.PI / 180;
            double bc = Math.Cos(b), bs = Math.Sin(b), hc = Math.Cos(h), hs = Math.Sin(h), minArea = Double.MaxValue;
            for (int i = 0; i < vertices.Length; i++) mapped[i] = Map(vertices[i], pose, bc, bs, hc, hs);
            for (int row = 0; row < rows - 1; row++)
            for (int col = 0; col < columns - 1; col++)
            {
                int a = row * columns + col, c = a + columns;
                minArea = Math.Min(minArea, Area(mapped[a], mapped[a + 1], mapped[c]));
                minArea = Math.Min(minArea, Area(mapped[a + 1], mapped[c + 1], mapped[c]));
            }
            for (int i = (rows - 1) * columns; plantedFeet && i < vertices.Length; i++)
                if (Math.Abs(mapped[i].X - vertices[i].X * Antialias) > .001 || Math.Abs(mapped[i].Y - vertices[i].Y * Antialias) > .001)
                    throw new InvalidOperationException("Laugh moved the planted feet.");
            return minArea;
        }
        static double Area(PointF a, PointF b, PointF c)
        { return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X); }
        public void Dispose() { canvas.Dispose(); }
    }
}
