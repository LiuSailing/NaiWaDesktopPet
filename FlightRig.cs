using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NaiwaDesktopPet
{
    // Both wings reuse one complete feather texture. Each wing is drawn in a
    // single affine pass around its attachment root, without strip seams.
    public sealed class FlightRig : IDisposable
    {
        readonly Bitmap wing, wingSmall, farWing, farWingSmall;
        readonly ImageAttributes attributes = new ImageAttributes();
        readonly ColorMatrix opacity = new ColorMatrix();
        readonly PointF[] corners = new PointF[3];
        readonly RectangleF source, sourceSmall;
        readonly float rootX, rootY;

        public FlightRig(string wingFile)
        {
            using (var original = new Bitmap(wingFile))
            {
                wing = MakeCache(original, 512, 1);
                wingSmall = MakeCache(original, 256, 1);
                farWing = MakeCache(original, 512, .91f);
                farWingSmall = MakeCache(original, 256, .91f);
            }
            // The generated attachment root is at approximately (100,790) on
            // the original 1254-square canvas, behind the shoulder feather fan.
            rootX = wing.Width * (100f / 1254); rootY = wing.Height * (790f / 1254);
            source = new RectangleF(0, 0, wing.Width, wing.Height);
            sourceSmall = new RectangleF(0, 0, wingSmall.Width, wingSmall.Height);
            attributes.SetWrapMode(WrapMode.TileFlipXY);
        }

        static Bitmap MakeCache(Bitmap original, int maximumEdge, float alpha)
        {
            int edge = Math.Min(maximumEdge, Math.Max(original.Width, original.Height));
            int width = Math.Max(1, (int)Math.Round(original.Width * edge / (double)Math.Max(original.Width, original.Height)));
            int height = Math.Max(1, (int)Math.Round(original.Height * edge / (double)Math.Max(original.Width, original.Height)));
            var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(result))
            using (var wrap = new ImageAttributes())
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                wrap.SetWrapMode(WrapMode.TileFlipXY);
                if (alpha < 1) { var matrix = new ColorMatrix(); matrix.Matrix33 = alpha; wrap.SetColorMatrix(matrix); }
                graphics.DrawImage(original, new Rectangle(0, 0, width, height), 0, 0,
                    original.Width, original.Height, GraphicsUnit.Pixel, wrap);
            }
            return result;
        }

        public void Draw(Graphics graphics, FlightPose pose, RectangleF character)
        {
            double alpha = Clamp(pose.WingsOpacity), spread = Clamp(pose.Spread);
            if (alpha <= .001 || character.Height <= 0) return;
            double flap = Math.Max(-1, Math.Min(1, pose.Flap));
            double length = Math.Min(140, character.Height * .62);
            double deviceScale;
            using (var transform = graphics.Transform)
            { var elements = transform.Elements; deviceScale = Math.Sqrt(elements[0] * elements[0] + elements[1] * elements[1]); }
            // Match the source resolution to the actual on-screen feather fan.
            // High-quality bilinear minification from 512px is expensive in GDI+.
            bool small = length * deviceScale <= 210;
            RectangleF sample = small ? sourceSmall : source;
            InterpolationMode previous = graphics.InterpolationMode;
            // The LOD is already high-quality bicubic sampled at load time.
            // Bilinear affine sampling keeps the moving feather fan within the
            // 10 ms frame budget without changing the body's drawing quality.
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            try
            {
                DrawWing(graphics, new PointF(character.Left + character.Width * .55f, character.Top + character.Height * .35f),
                    -1, length * .80, spread, flap * .94 - .04 * spread, alpha, small ? farWingSmall : farWing, sample);
                DrawWing(graphics, new PointF(character.Left + character.Width * .78f, character.Top + character.Height * .37f),
                    1, length, spread, flap, alpha, small ? wingSmall : wing, sample);
            }
            finally { graphics.InterpolationMode = previous; }
        }

        void DrawWing(Graphics graphics, PointF root, int side, double length, double spread, double flap, double alpha, Bitmap texture, RectangleF sample)
        {
            // Folded wings are narrow and tucked; opening gradually restores
            // the feather fan. A little perspective compression follows flaps.
            double unit = length / (wing.Width * .90);
            double sx = unit * (.13 + .87 * spread) * (.82 + .18 * Math.Sqrt(Math.Max(0, 1 - flap * flap)));
            double sy = unit * (.40 + .60 * spread);
            double angle = side * (45 * (1 - spread) + flap * 20 * spread) * Math.PI / 180;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            corners[0] = Map(0, 0, root, side, sx, sy, cosine, sine);
            corners[1] = Map(wing.Width, 0, root, side, sx, sy, cosine, sine);
            corners[2] = Map(0, wing.Height, root, side, sx, sy, cosine, sine);
            if (alpha >= .999)
                graphics.DrawImage(texture, corners, sample, GraphicsUnit.Pixel);
            else
            {
                opacity.Matrix33 = (float)alpha;
                attributes.SetColorMatrix(opacity, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(texture, corners, sample, GraphicsUnit.Pixel, attributes);
            }
        }

        PointF Map(float x, float y, PointF root, int side, double sx, double sy, double cosine, double sine)
        {
            double dx = (x - rootX) * sx * side, dy = (y - rootY) * sy;
            return new PointF((float)(root.X + dx * cosine - dy * sine), (float)(root.Y + dx * sine + dy * cosine));
        }

        static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }

        public void Dispose() { attributes.Dispose(); wing.Dispose(); wingSmall.Dispose(); farWing.Dispose(); farWingSmall.Dispose(); }
    }
}
