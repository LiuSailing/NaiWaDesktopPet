using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NaiwaDesktopPet;

public static class FlightRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(Pet pet, string name) { return (T)typeof(Pet).GetField(name, Private).GetValue(pet); }
    static void Set(Pet pet, string name, object value) { typeof(Pet).GetField(name, Private).SetValue(pet, value); }
    static void Invoke(Pet pet, string name, params object[] args) { typeof(Pet).GetMethod(name, Private).Invoke(pet, args); }
    static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NativeWindowPos
    { public IntPtr Handle, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    static Rectangle NativeBounds(Pet pet)
    {
        NativeRect r; Check(GetWindowRect(pet.Handle, out r), "Could not read the native pet window bounds.");
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }
    static Bitmap SurfaceBitmap(object surface)
    { return (Bitmap)surface.GetType().GetField("bitmap", Private).GetValue(surface); }
    static Rectangle AlphaBounds(byte[] pixels, Size size)
    {
        int left = size.Width, top = size.Height, right = -1, bottom = -1;
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
            if (pixels[(y * size.Width + x) * 4 + 3] >= 8)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
    sealed class SurfaceFrame : IDisposable
    {
        public readonly Bitmap Bitmap;
        public readonly byte[] Pixels;
        public readonly Rectangle Window, Visible;
        public SurfaceFrame(Pet pet)
        {
            Bitmap source = SurfaceBitmap(Field<object>(pet, "surface"));
            Bitmap = source.Clone(new Rectangle(Point.Empty, source.Size), PixelFormat.Format32bppPArgb);
            Pixels = AnimationClip.ReadPixels(Bitmap); Window = NativeBounds(pet);
            Check(Window.Size == Bitmap.Size, "Submitted framebuffer size does not match the native window.");
            Rectangle alpha = AlphaBounds(Pixels, Bitmap.Size);
            Check(!alpha.IsEmpty, "Submitted framebuffer is blank.");
            Visible = new Rectangle(Window.X + alpha.X, Window.Y + alpha.Y, alpha.Width, alpha.Height);
        }
        public int ChannelAt(int x, int y, int channel)
        {
            if (!Window.Contains(x, y)) return 0;
            return Pixels[((y - Window.Y) * Bitmap.Width + x - Window.X) * 4 + channel];
        }
        public void Dispose() { Bitmap.Dispose(); }
    }
    // ULW need not emit SetWindowPos notifications. Check native bounds at
    // presentation checkpoints, and inspect any messages Windows does deliver.
    // These are submitted framebuffer checks, not desktop screenshots.
    sealed class CanvasProbe : NativeWindow, IDisposable
    {
        readonly Pet pet;
        Rectangle previous;
        public int Changes, Resizes, WindowMessages;
        public string Failure;
        public CanvasProbe(Pet pet) { this.pet = pet; previous = NativeBounds(pet); AssignHandle(pet.Handle); }
        void CheckFrame(Rectangle target)
        {
            Bitmap bitmap = SurfaceBitmap(Field<object>(pet, "surface"));
            Check(bitmap.Size == target.Size, "Native canvas changed before its matching framebuffer was ready.");
            Check(!AlphaBounds(AnimationClip.ReadPixels(bitmap), bitmap.Size).IsEmpty,
                "Native canvas exposed a blank intermediate framebuffer.");
        }
        void Observe()
        {
            Rectangle actual = NativeBounds(pet);
            if (actual == previous) return;
            Changes++; if (actual.Size != previous.Size) Resizes++;
            previous = actual; CheckFrame(actual);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0046 && Failure == null)
            {
                try
                {
                    WindowMessages++;
                    var position = (NativeWindowPos)Marshal.PtrToStructure(m.LParam, typeof(NativeWindowPos));
                    Rectangle current = NativeBounds(pet);
                    var target = new Rectangle((position.Flags & 2) == 0 ? position.X : current.X,
                        (position.Flags & 2) == 0 ? position.Y : current.Y,
                        (position.Flags & 1) == 0 ? position.Width : current.Width,
                        (position.Flags & 1) == 0 ? position.Height : current.Height);
                    if (target != current) CheckFrame(target);
                }
                catch (Exception error) { Failure = error.Message; }
            }
            base.WndProc(ref m);
            if ((m.Msg != 0x0047 && m.Msg != 0x0005 && m.Msg != 0x0003) || Failure != null) return;
            try
            {
                WindowMessages++; Observe();
            }
            catch (Exception error) { Failure = error.Message; }
        }
        public void AssertValid()
        {
            Check(Failure == null, "Canvas publication regression: " + Failure);
            Observe();
        }
        public void Dispose() { ReleaseHandle(); }
    }
    static void CheckSameScreenPixels(SurfaceFrame before, SurfaceFrame after, string phase)
    {
        Check(before.Visible == after.Visible, phase + " moved the raw framebuffer silhouette: before=" + before.Visible + "; after=" + after.Visible + ".");
        Rectangle area = Rectangle.Union(before.Visible, after.Visible);
        int maximum = 0; long difference = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        for (int x = area.Left; x < area.Right; x++)
        for (int channel = 0; channel < 4; channel++)
        {
            int delta = Math.Abs(before.ChannelAt(x, y, channel) - after.ChannelAt(x, y, channel));
            maximum = Math.Max(maximum, delta); difference += delta;
        }
        Check(maximum <= 3 && difference < area.Width * (long)area.Height,
            phase + " changed stationary screen pixels during the canvas transition: max=" + maximum + "; sum=" + difference + ".");
    }
    static Point BodyPoint(Pet pet) { return ActionPoint(pet, MousePetAction.Belly); }
    static Point ActionPoint(Pet pet, MousePetAction action)
    {
        var clip = Field<Dictionary<string, AnimationClip>>(pet, "clips")["idle"];
        var map = Field<Dictionary<string, PetHitMap>>(pet, "hitMaps")["idle"];
        Rectangle visible = map.Bounds;
        PointF found = PointF.Empty; double score = Double.MaxValue;
        for (int y = visible.Top; y < visible.Bottom; y++)
        for (int x = visible.Left; x < visible.Right; x++)
        {
            var p = new PointF(x + .5f, y + .5f);
            if (!map.Contains(p) || map.Hit(p) != action) continue;
            double d = Math.Pow((x - visible.Left) / (double)visible.Width - .43, 2)
                + Math.Pow((y - visible.Top) / (double)visible.Height
                    - (action == MousePetAction.None ? .18 : action == MousePetAction.Belly ? .52 : .84), 2);
            if (d < score) { score = d; found = p; }
        }
        Check(score < Double.MaxValue, "No visible " + action + " point found for flight input.");
        RectangleF bounds = (RectangleF)typeof(Pet).GetMethod("SpriteBounds", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { "idle", Field<long>(pet, "clipTime"), -1 });
        float scale = Field<float>(pet, "displayScale");
        Point canvasOffset = Field<Point>(pet, "flightCanvasOffset");
        if (Field<bool>(pet, "facingRight")) found.X = visible.Left + visible.Right - found.X;
        var result = new Point(canvasOffset.X + (int)Math.Round((bounds.X + found.X * bounds.Width / clip.Frame.Width) * scale),
            canvasOffset.Y + (int)Math.Round((bounds.Y + found.Y * bounds.Height / clip.Frame.Height) * scale));
        var mapped = (PointF)typeof(Pet).GetMethod("ToSpritePoint", Private).Invoke(pet, new object[] { result });
        Check(map.Contains(mapped) && map.Hit(mapped) == action,
            "Displayed " + action + " point did not map back to the correct sprite region while facing "
                + (Field<bool>(pet, "facingRight") ? "right." : "left."));
        return result;
    }
    static void Click(Pet pet, Point p, int count)
    {
        var e = new MouseEventArgs(MouseButtons.Left, count, p.X, p.Y, 0);
        Invoke(pet, "OnDown", pet, e); Invoke(pet, "OnUp", pet, e);
    }
    static Point Anchor(Pet pet)
    {
        Point offset = Field<Point>(pet, "flightCanvasOffset");
        return new Point(pet.Left + offset.X, pet.Top + offset.Y);
    }
    static void Diagnostics(Pet pet, long started, int frames, int callbacks, long maximumCallbackGap, long maximumObservedStep)
    {
        long elapsed = Math.Max(1, Field<Stopwatch>(pet, "clock").ElapsedMilliseconds - started);
        int rendered = Math.Max(0, pet.RenderCount - frames);
        var costs = new List<double>(Field<List<double>>(pet, "benchmarkRenderCosts"));
        var times = Field<List<double>>(pet, "benchmarkTimes");
        var gaps = new List<double>();
        for (int i = 1; i < times.Count; i++) gaps.Add(times[i] - times[i - 1]);
        costs.Sort(); gaps.Sort();
        Console.WriteLine("Flight diagnostics: elapsed=" + elapsed + " ms; total renders=" + pet.RenderCount
            + "; flight renders=" + rendered + "; average FPS=" + (rendered * 1000.0 / elapsed).ToString("F2")
            + "; timer callbacks=" + callbacks + "; maximum timer gap=" + maximumCallbackGap
            + " ms; maximum observed phase step=" + maximumObservedStep + " ms.");
        if (costs.Count > 0)
            Console.WriteLine("Render costs: p50=" + costs[(costs.Count - 1) / 2].ToString("F2")
                + " ms; p95=" + costs[(int)((costs.Count - 1) * .95)].ToString("F2")
                + " ms; maximum=" + costs[costs.Count - 1].ToString("F2") + " ms; samples=" + costs.Count + ".");
        if (gaps.Count > 0)
            Console.WriteLine("Render intervals: p95=" + gaps[(int)((gaps.Count - 1) * .95)].ToString("F2")
                + " ms; maximum=" + gaps[gaps.Count - 1].ToString("F2") + " ms.");
    }
    static void CheckVisibleBody(Pet pet)
    {
        Rectangle body = Field<Rectangle>(pet, "flightBody"), screen = Field<Rectangle>(pet, "flightScreen");
        Point anchor = Anchor(pet);
        var actual = new Rectangle(anchor.X + body.X, anchor.Y + body.Y, body.Width, body.Height);
        Check(actual.Left >= screen.Left && actual.Right <= screen.Right && actual.Top >= screen.Top && actual.Bottom <= screen.Bottom,
            "Flight body left screen bounds: body=" + actual + "; screen=" + screen + "; time=" + Field<long>(pet, "flightTime"));
    }
    static void CheckEdgeTrajectory(Pet pet, Point start, Point end, int duration)
    {
        Rectangle body = Field<Rectangle>(pet, "flightBody"), screen = Field<Rectangle>(pet, "flightScreen");
        float scale = Field<float>(pet, "displayScale");
        int shift = duration - FlightMotion.Duration, braking = 5400 + shift, landing = 6300 + shift;
        Check(end.X < start.X && end.X + body.Left == screen.Left && end.Y == start.Y,
            "Flight destination did not place the visible body's left edge exactly at the screen edge.");
        Point next = FlightMotion.Destination(end, body, screen);
        Check(next.X + body.Right == screen.Right && next.Y == end.Y && next.X > end.X,
            "The next flight from the left edge did not head to the right edge.");
        Check(duration == FlightMotion.DurationFor(start, end, screen), "Flight did not use its distance-dependent duration.");
        double distance = Math.Abs(end.X - start.X);
        double cruisingSpeed = distance * (FlightMotion.At(braking, duration).Travel
            - FlightMotion.At(braking - 200, duration).Travel) / 200;
        double brakingSpeed = distance * (FlightMotion.At(landing, duration).Travel
            - FlightMotion.At(landing - 300, duration).Travel) / 300;
        Check(cruisingSpeed > 0 && brakingSpeed > 0 && brakingSpeed < cruisingSpeed * .35,
            "Flight did not slow down substantially before reaching the screen edge.");
        Check(FlightMotion.At(braking, duration).Lift > 50 && FlightMotion.At(landing - 300, duration).Lift > 0
            && FlightMotion.At(landing - 300, duration).Lift < 30 && FlightMotion.At(landing, duration).Lift == 0,
            "Flight did not descend smoothly while braking before landing.");
        Check(FlightMotion.LocationAt(start, end, body, screen, landing - 200, scale, duration).X != end.X,
            "Flight reached the screen edge before finishing its braking approach.");
        FlightPose settled = FlightMotion.At(landing, duration), folding = FlightMotion.At(landing + 300, duration);
        Check(settled.Travel == 1 && settled.Lift == 0 && settled.WingsOpacity == 1 && settled.Spread > .99,
            "Wings folded or disappeared before the body landed.");
        Check(folding.Travel == 1 && folding.Lift == 0 && folding.WingsOpacity == 1
            && folding.Spread > .12 && folding.Spread < settled.Spread,
            "Wings did not remain visible while folding after landing.");
        Check(FlightMotion.LocationAt(start, end, body, screen, landing, scale, duration) == end
            && FlightMotion.LocationAt(start, end, body, screen, duration, scale, duration) == end,
            "Flight did not remain still at the edge throughout its wing-folding finish.");
    }
    static void SavePreview(Pet pet, string output)
    {
        var clip = Field<Dictionary<string, AnimationClip>>(pet, "clips")["idle"];
        var rig = Field<FlightRig>(pet, "flightRig");
        Rectangle visible = AnimationClip.VisibleBounds(clip.Frame);
        int[] times = { 0, 400, 1200, 2300, 4000, 6100, 7300, 7600 };
        string[] phases = { "待机", "羽翼渐现", "展翼蓄力", "起飞", "稳定飞行", "落稳", "收翼淡出", "恢复待机" };
        using (var sheet = new Bitmap(2000, 880, PixelFormat.Format32bppPArgb))
        using (var sg = Graphics.FromImage(sheet))
        using (var attr = new ImageAttributes())
        {
            sg.Clear(Color.FromArgb(31, 39, 50));
            attr.SetWrapMode(WrapMode.TileFlipXY);
            for (int i = 0; i < times.Length; i++)
            using (var frame = new Bitmap(500, 440, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(frame))
            {
                var pose = FlightMotion.At(times[i]);
                g.Clear(Color.Transparent); g.TranslateTransform(120, 60);
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float h = 240 * (float)(1 - pose.Crouch);
                var b = new RectangleF(10, 310 - h, 240, h);
                var character = new RectangleF(b.X + visible.X / (float)clip.Frame.Width * b.Width,
                    b.Y + visible.Y / (float)clip.Frame.Height * b.Height,
                    visible.Width / (float)clip.Frame.Width * b.Width, visible.Height / (float)clip.Frame.Height * b.Height);
                float cx = character.Left + character.Width * .5f, cy = character.Top + character.Height * .55f;
                g.TranslateTransform(cx, cy); g.RotateTransform((float)pose.Tilt); g.TranslateTransform(-cx, -cy);
                rig.Draw(g, pose, character);
                var corners = new[] { new PointF(b.Left, b.Top), new PointF(b.Right, b.Top), new PointF(b.Left, b.Bottom) };
                g.DrawImage(clip.Frame, corners, new RectangleF(0, 0, clip.Frame.Width, clip.Frame.Height), GraphicsUnit.Pixel, attr);
                g.Flush(FlushIntention.Sync);
                for (int x = 0; x < frame.Width; x++)
                    Check(frame.GetPixel(x, 0).A < 40 && frame.GetPixel(x, frame.Height - 1).A < 40, "Wing clipped vertically in phase " + phases[i]);
                for (int y = 0; y < frame.Height; y++)
                    Check(frame.GetPixel(0, y).A < 40 && frame.GetPixel(frame.Width - 1, y).A < 40, "Wing clipped horizontally in phase " + phases[i]);
                int left = i % 4 * 500, top = i / 4 * 440;
                sg.DrawImageUnscaled(frame, left, top);
                sg.DrawString(phases[i] + "  " + times[i] + " ms", SystemFonts.MenuFont, Brushes.White, left + 12, top + 12);
            }
            sheet.Save(Path.Combine(output, "flight-stages.png"), ImageFormat.Png);
        }
        Console.WriteLine("Flight preview passed: eight phases rendered; wing edges fit the expanded transparent canvas.");
    }
    static Bitmap RenderedFrame(Pet pet)
    {
        pet.Render();
        object surface = Field<object>(pet, "surface");
        var bitmap = (Bitmap)surface.GetType().GetField("bitmap", Private).GetValue(surface);
        return bitmap.Clone(new Rectangle(Point.Empty, bitmap.Size), PixelFormat.Format32bppPArgb);
    }
    static void CheckFacingPixels(Pet pet, string output, string phase)
    {
        Check(Field<bool>(pet, "facingRight"), "Facing comparison did not begin with the rightward orientation.");
        Set(pet, "message", null);
        Set(pet, "facingRight", false);
        using (Bitmap left = RenderedFrame(pet))
        {
            Set(pet, "facingRight", true);
            using (Bitmap right = RenderedFrame(pet))
            {
                var clip = Field<Dictionary<string, AnimationClip>>(pet, "clips")["idle"];
                Rectangle visible = Field<Dictionary<string, PetHitMap>>(pet, "hitMaps")["idle"].Bounds;
                RectangleF bounds = (RectangleF)typeof(Pet).GetMethod("SpriteBounds", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { "idle", Field<long>(pet, "clipTime"), -1 });
                double mirrorSum = Field<Point>(pet, "flightCanvasOffset").X * 2
                    + Field<float>(pet, "displayScale") * (bounds.Left * 2
                    + (visible.Left + visible.Right) * bounds.Width / clip.Frame.Width);
                byte[] source = AnimationClip.ReadPixels(left), actual = AnimationClip.ReadPixels(right);
                double mirroredError = 0, unchangedError = 0; int changedPixels = 0;
                for (int y = 0; y < right.Height; y++)
                for (int x = 0; x < right.Width; x++)
                {
                    double reflected = mirrorSum - x - 1;
                    int a = (int)Math.Floor(reflected), b = a + 1, pixel = (y * right.Width + x) * 4;
                    double weight = reflected - a, pixelDifference = 0;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        double av = a < 0 || a >= left.Width ? 0 : source[(y * left.Width + a) * 4 + channel];
                        double bv = b < 0 || b >= left.Width ? 0 : source[(y * left.Width + b) * 4 + channel];
                        mirroredError += Math.Abs(actual[pixel + channel] - av * (1 - weight) - bv * weight);
                        pixelDifference += Math.Abs(actual[pixel + channel] - source[pixel + channel]);
                    }
                    unchangedError += pixelDifference;
                    if (pixelDifference > 30) changedPixels++;
                }
                Check(changedPixels > 1000 && mirroredError < unchangedError * .35,
                    "Rightward " + phase + " did not mirror the visible body and wings together: changed=" + changedPixels
                        + "; mirror error=" + mirroredError.ToString("F0") + "; unchanged error=" + unchangedError.ToString("F0") + ".");
                using (var sheet = new Bitmap(left.Width * 2, left.Height + 38, PixelFormat.Format32bppPArgb))
                using (var g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(31, 39, 50));
                    g.DrawImageUnscaled(left, 0, 38); g.DrawImageUnscaled(right, left.Width, 38);
                    g.DrawString("左向参考 / " + phase, SystemFonts.MenuFont, Brushes.White, 12, 10);
                    g.DrawString("右向实际 / " + phase, SystemFonts.MenuFont, Brushes.White, left.Width + 12, 10);
                    sheet.Save(Path.Combine(output, "flight-facing-" + phase + ".png"), ImageFormat.Png);
                }
            }
        }
        Check(Field<bool>(pet, "facingRight"), "Facing comparison failed to restore rightward orientation.");
    }
    static void CheckRightwardFlight(Pet pet, Size regularSize, string output)
    {
        Check(!Field<bool>(pet, "facingRight"), "The completed leftward flight lost its leftward orientation.");
        Point leftLanding = pet.Location;
        Invoke(pet, "BeginFlight");
        Point end = Field<Point>(pet, "flightEnd");
        Rectangle body = Field<Rectangle>(pet, "flightBody"), screen = Field<Rectangle>(pet, "flightScreen");
        int duration = Field<int>(pet, "flightDuration");
        Check(Field<bool>(pet, "flying") && Field<bool>(pet, "facingRight")
            && Field<long>(pet, "flightTime") == 0 && Anchor(pet) == leftLanding
            && end.X > leftLanding.X && end.X + body.Right == screen.Right,
            "Flight from the left edge did not turn right before take-off and target the right edge.");
        Invoke(pet, "AdvanceFlight", 1000); pet.Render();
        Check(Field<bool>(pet, "facingRight"), "Rightward orientation was lost during wing unfolding.");
        CheckFacingPixels(pet, output, "unfold");
        Invoke(pet, "AdvanceFlight", 3000); pet.Render();
        Check(Field<bool>(pet, "facingRight"), "Rightward orientation was lost during flight.");
        CheckVisibleBody(pet); CheckFacingPixels(pet, output, "cruise");
        Invoke(pet, "AdvanceFlight", duration - 4000); pet.Render();
        Check(!Field<bool>(pet, "flying") && Field<bool>(pet, "facingRight") && pet.Location == end
            && pet.ClientSize == regularSize && Field<Point>(pet, "flightCanvasOffset") == Point.Empty
            && Field<string>(pet, "animation") == "idle" && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None,
            "Rightward landing did not keep the right-facing idle body at the right edge.");
        CheckFacingPixels(pet, output, "idle");
        pet.Act("idle", null, 1); Click(pet, ActionPoint(pet, MousePetAction.None), 1);
        Check(Field<bool>(pet, "facingRight") && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None
            && Field<string>(pet, "animation") == "idle", "Right-facing head click triggered a body action.");
        pet.Act("idle", null, 1); Click(pet, ActionPoint(pet, MousePetAction.Feet), 1);
        Check(Field<bool>(pet, "facingRight") && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Feet,
            "Right-facing feet click missed its retreat response.");
        Point retreat = pet.Location;
        Invoke(pet, "AdvanceMouseReaction", 480); pet.Render();
        Check(pet.Left < retreat.X && pet.Top == retreat.Y && Field<bool>(pet, "facingRight"),
            "Right-facing feet response did not step backward to the left.");
        pet.Act("idle", null, 1);
        Console.WriteLine("Facing regressions passed: turned right before take-off; body and wings mirrored; right-facing idle persisted after landing; head/feet hit regions and leftward retreat passed.");
    }
    static void CheckExistingInputs(Pet pet, Size regularSize)
    {
        pet.Act("idle", null, 1); Point p = BodyPoint(pet); Click(pet, p, 1);
        Check(!Field<bool>(pet, "flying") && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Belly,
            "Short press no longer triggers the belly response.");
        Click(pet, p, 2);
        Check(!Field<bool>(pet, "flying") && Field<string>(pet, "animation") == "laugh" && Field<int>(pet, "jumpTime") < 0,
            "Double click did not keep its laughter action.");
        pet.Act("idle", null, 1); p = BodyPoint(pet);
        var e = new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0);
        Invoke(pet, "OnDown", pet, e);
        Point mouse = Field<Point>(pet, "mouseStart");
        Invoke(pet, "MoveDraggedPet", new Point(mouse.X + 30, mouse.Y + 12));
        Set(pet, "holdStarted", Field<Stopwatch>(pet, "clock").ElapsedMilliseconds - FlightMotion.HoldMilliseconds - 1);
        Invoke(pet, "Tick", pet, EventArgs.Empty);
        Check(!Field<bool>(pet, "holdArmed") && !Field<bool>(pet, "flying"), "Dragging armed a flight.");
        Invoke(pet, "OnUp", pet, e);
        Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None, "Dragging released a click action.");

        Invoke(pet, "BeginFlight"); Invoke(pet, "AdvanceFlight", 3500); pet.Render();
        Check(Field<bool>(pet, "flying"), "Cancellation check did not start a flight.");
        Point before = Anchor(pet); bool facingRight = Field<bool>(pet, "facingRight");
        pet.Act("idle", null, 1);
        Check(!Field<bool>(pet, "flying") && pet.ClientSize == regularSize && pet.Location == before
            && Field<bool>(pet, "facingRight") == facingRight,
            "Interrupting flight jumped the body or left the canvas expanded.");
        Console.WriteLine("Input regressions passed: short click, double click, drag cancellation and interruption without a position jump.");
    }
    static void BeginContinuingFlight(Pet pet, bool facingRight, Point target)
    {
        Point before = pet.Location;
        Invoke(pet, "BeginFlight");
        Point expectedTarget = new Point(target.X, before.Y), actualStart = Field<Point>(pet, "flightStart"), actualEnd = Field<Point>(pet, "flightEnd");
        Check(Field<bool>(pet, "flying") && Field<bool>(pet, "facingRight") == facingRight
            && actualStart == before && Anchor(pet) == before && actualEnd == expectedTarget,
            "Restarted flight did not preserve its direction, screen-edge X and current height: expectedFacingRight=" + facingRight
                + "; actualFacingRight=" + Field<bool>(pet, "facingRight") + "; flying=" + Field<bool>(pet, "flying")
                + "; before=" + before + "; start=" + actualStart + "; anchor=" + Anchor(pet)
                + "; expectedEnd=" + expectedTarget + "; actualEnd=" + actualEnd + ".");
    }
    static Point AdvanceInteriorFlight(Pet pet)
    {
        Invoke(pet, "AdvanceFlight", 3000); pet.Render();
        Point start = Field<Point>(pet, "flightStart"), end = Field<Point>(pet, "flightEnd"), current = Anchor(pet);
        Check(Field<bool>(pet, "flying") && current.X > Math.Min(start.X, end.X) && current.X < Math.Max(start.X, end.X),
            "Direction-continuation test did not stop strictly between its flight endpoints: start=" + start + "; end=" + end
                + "; anchor=" + current + "; time=" + Field<long>(pet, "flightTime") + "; duration=" + Field<int>(pet, "flightDuration") + ".");
        return current;
    }
    static void CheckStoppedDirection(Pet pet, Point before, bool facingRight, Size regularSize, string input)
    {
        Check(!Field<bool>(pet, "flying") && Field<bool>(pet, "facingRight") == facingRight
            && pet.Location == before && pet.ClientSize == regularSize && Field<Point>(pet, "flightCanvasOffset") == Point.Empty,
            input + " changed the interrupted flight's position or remembered direction: expected=" + before + "; actual=" + pet.Location
                + "; expectedFacingRight=" + facingRight + "; actualFacingRight=" + Field<bool>(pet, "facingRight")
                + "; flying=" + Field<bool>(pet, "flying") + "; size=" + pet.ClientSize
                + "; expectedSize=" + regularSize + "; canvasOffset=" + Field<Point>(pet, "flightCanvasOffset") + ".");
    }
    static void CompleteDirectedFlight(Pet pet, Point target, bool facingRight, Size regularSize)
    {
        Point expectedTarget = new Point(target.X, Field<Point>(pet, "flightStart").Y);
        Invoke(pet, "AdvanceFlight", Field<int>(pet, "flightDuration") - (int)Field<long>(pet, "flightTime")); pet.Render();
        CheckStoppedDirection(pet, expectedTarget, facingRight, regularSize, "Landing");
        Check(Field<string>(pet, "animation") == "idle" && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None,
            "Directed flight did not return to idle at its screen edge.");
    }
    static void CheckDirectionContinuation(Pet pet, Size regularSize)
    {
        pet.Act("idle", null, 1);
        Rectangle body = Field<Rectangle>(pet, "flightBody"), screen = Field<Rectangle>(pet, "flightScreen");
        Point left = new Point(screen.Left - body.Left, pet.Top), right = new Point(screen.Right - body.Right, pet.Top);
        Set(pet, "facingRight", false); pet.Location = left; pet.Render();
        BeginContinuingFlight(pet, true, right);
        Point stopped = AdvanceInteriorFlight(pet);
        pet.Act("idle", null, 1);
        CheckStoppedDirection(pet, stopped, true, regularSize, "Idle interruption");
        BeginContinuingFlight(pet, true, right);

        stopped = AdvanceInteriorFlight(pet); Click(pet, BodyPoint(pet), 1);
        CheckStoppedDirection(pet, stopped, true, regularSize, "Body-click interruption");
        Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Belly,
            "Body-click interruption no longer triggered the normal belly response.");
        BeginContinuingFlight(pet, true, right);

        AdvanceInteriorFlight(pet);
        Point p = BodyPoint(pet); var down = new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0);
        Invoke(pet, "OnDown", pet, down);
        Point mouse = Field<Point>(pet, "mouseStart"), beforeDrag = pet.Location;
        Invoke(pet, "MoveDraggedPet", new Point(mouse.X + 20, mouse.Y));
        Invoke(pet, "OnUp", pet, down);
        Check(!Field<bool>(pet, "flying") && Field<bool>(pet, "facingRight") && pet.Left > beforeDrag.X
            && pet.Top == beforeDrag.Y && pet.ClientSize == regularSize && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None,
            "Short-drag interruption changed direction or triggered a click response.");
        BeginContinuingFlight(pet, true, right); CompleteDirectedFlight(pet, right, true, regularSize);

        BeginContinuingFlight(pet, false, left);
        stopped = AdvanceInteriorFlight(pet); pet.Act("idle", null, 1);
        CheckStoppedDirection(pet, stopped, false, regularSize, "Leftward idle interruption");
        BeginContinuingFlight(pet, false, left); CompleteDirectedFlight(pet, left, false, regularSize);
        Point leftLanding = pet.Location;
        BeginContinuingFlight(pet, true, right); pet.Act("idle", null, 1);
        CheckStoppedDirection(pet, leftLanding, true, regularSize, "Left-edge restart interruption");
        Console.WriteLine("Direction continuation passed: rightward and leftward interruptions retained their edge targets; body clicks and short drags retained direction; only completed edge arrivals reversed the next flight.");
    }
    static void CheckCanvasPublication(Pet pet, Size regularSize, string output)
    {
        pet.Act("idle", null, 1); Set(pet, "facingRight", false); Set(pet, "clipTime", 0L);
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        pet.Location = new Point(screen.Left + (screen.Width - regularSize.Width) / 2,
            screen.Bottom - regularSize.Height - 80); pet.Render();
        using (var probe = new CanvasProbe(pet))
        using (var idle = new SurfaceFrame(pet))
        {
            object oldSurface = Field<object>(pet, "surface");
            Bitmap oldBitmap = SurfaceBitmap(oldSurface);
            Invoke(pet, "ResizeFlightCanvas", true, idle.Window.Location);
            Check(NativeBounds(pet) == idle.Window && pet.Bounds == idle.Window && pet.ClientSize == regularSize,
                "Reserving the expanded canvas changed the visible native/managed bounds before drawing.");
            Check(!Object.ReferenceEquals(oldSurface, Field<object>(pet, "surface")), "Canvas resize did not reserve a new framebuffer.");
            pet.Render(false, false);
            Check(NativeBounds(pet) == idle.Window && pet.Bounds == idle.Window,
                "Measuring a pending canvas published it before a complete presentation.");
            byte[] retained = AnimationClip.ReadPixels(oldBitmap);
            Check(retained.Length == idle.Pixels.Length, "The currently displayed framebuffer was released before replacement.");
            for (int i = 0; i < retained.Length; i++)
                Check(retained[i] == idle.Pixels[i], "A pending resize changed the retained displayed framebuffer.");
            Check(probe.Changes == 0, "A pending canvas resize emitted an intermediate native window change.");
            Invoke(pet, "ResizeFlightCanvas", false, idle.Window.Location); pet.Render(); probe.AssertValid();
            using (var restored = new SurfaceFrame(pet)) CheckSameScreenPixels(idle, restored, "Cancelled canvas reservation");

            int takeoffRenders = pet.RenderCount;
            Invoke(pet, "BeginFlight"); probe.AssertValid();
            Check(pet.RenderCount == takeoffRenders + 1,
                "Take-off published an intermediate measurement/facing frame instead of one complete expanded frame.");
            using (var takeoff = new SurfaceFrame(pet))
            {
                Check(takeoff.Window.Size.Width > regularSize.Width && takeoff.Window.Size.Height > regularSize.Height,
                    "Take-off did not submit the expanded native canvas.");
                CheckSameScreenPixels(idle, takeoff, "Take-off at 0 ms");
                idle.Bitmap.Save(Path.Combine(output, "flight-canvas-idle-before.png"), ImageFormat.Png);
                takeoff.Bitmap.Save(Path.Combine(output, "flight-canvas-takeoff.png"), ImageFormat.Png);
            }
            int duration = Field<int>(pet, "flightDuration");
            Invoke(pet, "AdvanceFlight", duration - 1); pet.Render(); probe.AssertValid();
            using (var landing = new SurfaceFrame(pet))
            {
                int landingRenders = pet.RenderCount;
                Invoke(pet, "AdvanceFlight", 1);
                Check(!Field<bool>(pet, "flying") && NativeBounds(pet) == landing.Window && pet.Bounds == landing.Window,
                    "Staging StopFlight changed the displayed canvas before its complete idle replacement was rendered.");
                Check(pet.RenderCount == landingRenders, "Staging StopFlight published an intermediate canvas frame.");
                pet.Render(); probe.AssertValid();
                Check(pet.RenderCount == landingRenders + 1, "Landing did not publish exactly one complete idle replacement.");
                using (var settled = new SurfaceFrame(pet))
                {
                    Check(!Field<bool>(pet, "flying") && settled.Window.Size == regularSize,
                        "Landing did not submit the regular native canvas.");
                    CheckSameScreenPixels(landing, settled, "Landing at the final millisecond");
                    landing.Bitmap.Save(Path.Combine(output, "flight-canvas-landing.png"), ImageFormat.Png);
                    settled.Bitmap.Save(Path.Combine(output, "flight-canvas-idle-after.png"), ImageFormat.Png);
                }
            }
            Check(probe.Resizes >= 2, "Native checkpoints missed the expanded and restored canvas presentations.");
            Console.WriteLine("Canvas publication passed: deferred resize and measurement kept native bounds/old pixels; "
                + probe.Resizes + " native resize checkpoints had complete nonblank matching framebuffers; "
                + probe.WindowMessages + " window geometry notifications; one presentation at each transition; "
                + "take-off/landing screen pixels stayed continuous. PNGs are raw submitted framebuffers.");
        }
    }
    static void ClickVisibleFlightMenu(Pet pet)
    {
        var root = Field<ContextMenuStrip>(pet, "menu");
        var command = Field<ToolStripMenuItem>(pet, "flightMenuItem");
        var child = (ToolStripDropDown)command.Owner;
        var parent = (ToolStripMenuItem)child.OwnerItem;
        root.Show(pet, new Point(pet.Width / 2, pet.Height / 2));
        parent.Select(); parent.ShowDropDown();
        Check(root.Visible && child.Visible && command.Available,
            "Flight command test did not open the real root and mouse-action menus.");
        command.PerformClick();
        Check(!root.Visible && !child.Visible, "One flight-menu click left the root or mouse-action submenu open.");
        Application.DoEvents();
        Check(!root.Visible && !child.Visible && !Field<bool>(pet, "paused") && !Field<ToolStripMenuItem>(pet, "pauseItem").Checked,
            "Flight menu command did not close both menus and clear animation pause.");
    }
    static void CheckMenuFlightRestarts(Pet pet, Size regularSize)
    {
        pet.Act("idle", null, 1); Set(pet, "facingRight", false);
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        pet.Location = new Point(screen.Left + (screen.Width - regularSize.Width) / 2,
            screen.Bottom - regularSize.Height - 80); pet.Render();
        Field<ToolStripMenuItem>(pet, "pauseItem").Checked = true;
        ClickVisibleFlightMenu(pet);
        Check(Field<bool>(pet, "flying") && Field<long>(pet, "flightTime") == 0,
            "A single flight-menu click did not start flight from paused idle.");
        int[] offsets = { 0, 500, 1299, 1299 };
        for (int round = 0; round < offsets.Length; round++)
        {
            int duration = Field<int>(pet, "flightDuration"), targetTime = duration - 1300 + offsets[round];
            Invoke(pet, "AdvanceFlight", targetTime - (int)Field<long>(pet, "flightTime")); pet.Render();
            Point arrived = Field<Point>(pet, "flightEnd");
            bool oldDirection = Field<bool>(pet, "facingRight");
            Check(Field<bool>(pet, "flying") && Anchor(pet) == arrived
                && FlightMotion.At(targetTime, duration).Travel == 1 && FlightMotion.At(targetTime, duration).Lift == 0,
                "Menu restart was not tested inside the landed wing-fold/fade tail at round " + round + ".");
            if (round % 2 == 0) Field<ToolStripMenuItem>(pet, "pauseItem").Checked = true;
            ClickVisibleFlightMenu(pet);
            Point next = Field<Point>(pet, "flightEnd");
            Rectangle body = Field<Rectangle>(pet, "flightBody"), flightScreen = Field<Rectangle>(pet, "flightScreen");
            Check(Field<bool>(pet, "flying") && Field<long>(pet, "flightTime") == 0
                && Field<bool>(pet, "facingRight") != oldDirection && Field<Point>(pet, "flightStart") == arrived
                && Anchor(pet) == arrived && (oldDirection ? next.X < arrived.X : next.X > arrived.X)
                && (oldDirection ? next.X + body.Left == flightScreen.Left : next.X + body.Right == flightScreen.Right),
                "One real menu click was swallowed during landed tail round " + round + " at " + targetTime
                    + "/" + duration + " ms, or did not reverse toward the opposite edge.");
            Set(pet, "lastTime", Field<Stopwatch>(pet, "clock").ElapsedMilliseconds - 40);
            Invoke(pet, "Tick", pet, EventArgs.Empty);
            Check(Field<long>(pet, "flightTime") >= 40,
                "Restarted menu flight stayed frozen after its menu closed at round " + round + ".");
        }
        Invoke(pet, "AdvanceFlight", 3000 - (int)Field<long>(pet, "flightTime")); pet.Render();
        long airborneTime = Field<long>(pet, "flightTime");
        Point start = Field<Point>(pet, "flightStart"), end = Field<Point>(pet, "flightEnd"), anchor = Anchor(pet);
        bool direction = Field<bool>(pet, "facingRight");
        Field<ToolStripMenuItem>(pet, "pauseItem").Checked = true;
        ClickVisibleFlightMenu(pet);
        Check(Field<bool>(pet, "flying") && Field<long>(pet, "flightTime") == airborneTime
            && Field<Point>(pet, "flightStart") == start && Field<Point>(pet, "flightEnd") == end
            && Field<bool>(pet, "facingRight") == direction && Anchor(pet) == anchor,
            "An airborne menu command restarted or moved the current flight instead of resuming it.");
        Set(pet, "lastTime", Field<Stopwatch>(pet, "clock").ElapsedMilliseconds - 40);
        Invoke(pet, "Tick", pet, EventArgs.Empty);
        Check(Field<long>(pet, "flightTime") > airborneTime, "Paused airborne flight did not resume after one menu click.");
        pet.Act("idle", null, 1);
        Check(!Field<bool>(pet, "flying") && pet.ClientSize == regularSize, "Menu regression cleanup did not restore the regular canvas.");
        Console.WriteLine("Flight menu regressions passed: one click from paused idle; four alternating left/right tail departures including the final fade millisecond; root/submenus closed; paused airborne flight resumed without restarting.");
    }
    public static void Run(string assets, string output)
    {
        Directory.CreateDirectory(output); DisplayQuality.Enable();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); Application.EnableVisualStyles();
        Console.WriteLine(FlightMotion.Validate());
        using (var pet = new Pet(assets))
        using (var timer = new Timer { Interval = 40 })
        {
            pet.Enabled = false;
            int frames = 0, callbacks = 0; long started = 0, pauseStarted = 0, pauseTime = 0;
            long lastCallback = 0, maximumCallbackGap = 0, previousFlightTime = 0, maximumObservedStep = 0;
            bool paused = false, resumed = false, passed = false;
            bool sawAppearance = false, sawUnfold = false, sawCruise = false, sawLanding = false, sawSettled = false, sawFade = false;
            int flightDuration = FlightMotion.Duration, shift = 0;
            Size regularSize = pet.ClientSize; Point expectedEnd = Point.Empty, previousAnchor = Point.Empty;
            Exception failure = null;
            pet.Shown += (s, e) => {
                try
                {
                    SavePreview(pet, output);
                    Set(pet, "benchmarkTimes", new List<double>());
                    Set(pet, "benchmarkRenderCosts", new List<double>());
                    Rectangle screen = Screen.PrimaryScreen.Bounds;
                    pet.Location = new Point(screen.Left + (screen.Width - pet.Width) / 2, screen.Bottom - pet.Height - 80);
                    Point before = pet.Location; Point p = BodyPoint(pet);
                    var down = new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0);
                    Invoke(pet, "OnDown", pet, down);
                    Check(Field<bool>(pet, "holdArmed"), "Body press did not arm a long press.");
                    Set(pet, "holdStarted", Field<Stopwatch>(pet, "clock").ElapsedMilliseconds - FlightMotion.HoldMilliseconds - 1);
                    Invoke(pet, "Tick", pet, EventArgs.Empty);
                    Check(Field<bool>(pet, "flying") && Field<bool>(pet, "holdTriggered")
                        && !Field<bool>(pet, "dragging") && !pet.Capture && !Field<bool>(pet, "facingRight"),
                        "Long press did not enter leftward flight cleanly.");
                    Check(Field<Point>(pet, "flightStart") == before && Anchor(pet) == before,
                        "Expanding the canvas moved the body at take-off.");
                    Check(pet.ClientSize.Width > regularSize.Width && pet.ClientSize.Height > regularSize.Height,
                        "Flight canvas did not expand.");
                    expectedEnd = Field<Point>(pet, "flightEnd");
                    flightDuration = Field<int>(pet, "flightDuration"); shift = flightDuration - FlightMotion.Duration;
                    CheckEdgeTrajectory(pet, before, expectedEnd, flightDuration); previousAnchor = before;
                    Invoke(pet, "OnUp", pet, down);
                    Check(Field<bool>(pet, "flying") && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None
                        && Field<string>(pet, "animation") == "idle", "Releasing a long press triggered a click action.");
                    frames = pet.RenderCount; started = Field<Stopwatch>(pet, "clock").ElapsedMilliseconds; timer.Start();
                }
                catch (Exception error) { failure = error; pet.Close(); }
            };
            timer.Tick += (s, e) => {
                try
                {
                    long now = Field<Stopwatch>(pet, "clock").ElapsedMilliseconds;
                    callbacks++;
                    if (lastCallback != 0) maximumCallbackGap = Math.Max(maximumCallbackGap, now - lastCallback);
                    lastCallback = now;
                    if (now - started > flightDuration + 3000) throw new InvalidOperationException("Flight did not complete within its expected duration.");
                    bool flying = Field<bool>(pet, "flying"); long time = Field<long>(pet, "flightTime");
                    if (flying)
                    {
                        Check(!Field<bool>(pet, "facingRight"), "Leftward flight turned its face away from its travel direction.");
                        maximumObservedStep = Math.Max(maximumObservedStep, time - previousFlightTime); previousFlightTime = time;
                        CheckVisibleBody(pet); var pose = FlightMotion.At(time, flightDuration);
                        Point anchor = Anchor(pet);
                        Point expected = FlightMotion.LocationAt(Field<Point>(pet, "flightStart"), expectedEnd,
                            Field<Rectangle>(pet, "flightBody"), Field<Rectangle>(pet, "flightScreen"), time,
                            Field<float>(pet, "displayScale"), flightDuration);
                        Check(anchor == expected, "Displayed flight position diverged from its duration-aware trajectory at " + time
                            + " ms: actual=" + anchor + "; planned=" + expected + "; duration=" + flightDuration + ".");
                        Check(anchor.X <= previousAnchor.X && anchor.X >= expectedEnd.X,
                            "Flight reversed direction or overshot its destination.");
                        if (time < 6100 + shift)
                            Check(anchor.X > expectedEnd.X, "Flight touched the screen edge before its final braking approach.");
                        if (time >= 6300 + shift)
                            Check(anchor == expectedEnd && pose.Lift == 0 && pose.Travel == 1,
                                "The landed body kept moving while its wings folded.");
                        previousAnchor = anchor;
                        if (time > 100 && time < 550 && pose.WingsOpacity > 0 && pose.Spread < .25) sawAppearance = true;
                        if (time > 850 && time < 1500 && pose.Spread > .2 && pose.Spread < 1) sawUnfold = true;
                        if (time > 2600 + (2800 + shift) * .1 && time < 5400 + shift - (2800 + shift) * .1
                            && pose.Lift > 50 && pose.Spread > .99) sawCruise = true;
                        if (time > 5600 + shift && time < 6300 + shift && pose.Lift > 0 && pose.Lift < 65) sawLanding = true;
                        if (time > 6500 + shift && time < 7000 + shift && pose.WingsOpacity == 1
                            && pose.Spread > .12 && pose.Spread < 1 && anchor == expectedEnd) sawSettled = true;
                        if (time > 7100 + shift && pose.WingsOpacity > 0 && pose.WingsOpacity < 1 && pose.Spread < .2) sawFade = true;
                        if (!paused && time >= 3200)
                        {
                            Field<ToolStripMenuItem>(pet, "pauseItem").Checked = true;
                            Invoke(pet, "Tick", pet, EventArgs.Empty);
                            pauseTime = Field<long>(pet, "flightTime"); pauseStarted = now; paused = true;
                        }
                        else if (paused && !resumed && now - pauseStarted >= 300)
                        {
                            Check(Field<long>(pet, "flightTime") == pauseTime, "Paused flight continued moving.");
                            Field<ToolStripMenuItem>(pet, "pauseItem").Checked = false; resumed = true;
                        }
                        return;
                    }
                    Check(resumed && sawAppearance && sawUnfold && sawCruise && sawLanding && sawSettled && sawFade,
                        "One or more flight transitions were skipped: appearance=" + sawAppearance + "; unfold=" + sawUnfold
                        + "; cruise=" + sawCruise + "; landing=" + sawLanding + "; settled=" + sawSettled + "; fade=" + sawFade);
                    Check(pet.Location == expectedEnd && pet.ClientSize == regularSize && Field<Point>(pet, "flightCanvasOffset") == Point.Empty,
                        "Landing did not restore the regular canvas at the destination.");
                    Check(Field<string>(pet, "animation") == "idle" && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None,
                        "Landing did not restore idle.");
                    Check(pet.RenderCount - frames > 400, "Flight did not render continuously.");
                    Console.WriteLine("Complete flight passed: " + (now - started) + " ms including a 300 ms pause; "
                        + (pet.RenderCount - frames) + " frames; " + flightDuration + " ms planned; exact screen-edge landing; all phases observed; idle landing.");
                    Diagnostics(pet, started, frames, callbacks, maximumCallbackGap, maximumObservedStep);
                    timer.Stop();
                    CheckRightwardFlight(pet, regularSize, output);
                    CheckExistingInputs(pet, regularSize);
                    CheckDirectionContinuation(pet, regularSize);
                    ((IDisposable)Field<object>(pet, "frameClock")).Dispose(); Set(pet, "frameClock", null);
                    Application.DoEvents();
                    CheckCanvasPublication(pet, regularSize, output);
                    CheckMenuFlightRestarts(pet, regularSize);
                    passed = true; timer.Stop(); pet.Close();
                }
                catch (Exception error)
                {
                    Console.WriteLine("Flight failure: " + error.Message);
                    Diagnostics(pet, started, frames, callbacks, maximumCallbackGap, maximumObservedStep);
                    failure = error; timer.Stop(); pet.Close();
                }
            };
            Application.Run(pet);
            if (failure != null) throw failure;
            Check(passed, "Flight tests ended before completion.");
        }
    }
}
