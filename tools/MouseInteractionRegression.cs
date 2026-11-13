using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using NaiwaDesktopPet;

public static class MouseInteractionRegression
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(Pet pet, string name) { return (T)typeof(Pet).GetField(name, Fields).GetValue(pet); }
    static void Invoke(Pet pet, string name, params object[] args) { typeof(Pet).GetMethod(name, Fields).Invoke(pet, args); }
    static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    static PointF Find(AnimationClip clip, PetHitMap map, MousePetAction target, int side)
    {
        Rectangle b = AnimationClip.VisibleBounds(clip.Frame);
        PointF best = PointF.Empty; double score = Double.MaxValue;
        double cx = target == MousePetAction.Feet ? (side == 0 ? .27 : .70) : .43;
        double cy = target == MousePetAction.Feet ? .96 : target == MousePetAction.Belly ? .51 : .18;
        for (int y = b.Top; y < b.Bottom; y++)
        for (int x = b.Left; x < b.Right; x++)
        {
            var p = new PointF(x + .5f, y + .5f);
            double u = (x - b.Left) / (double)b.Width, v = (y - b.Top) / (double)b.Height;
            if (!map.Contains(p) || map.Hit(p) != target) continue;
            if (target == MousePetAction.Feet && (side == 0 ? u >= .52 : u < .52)) continue;
            double d = Math.Pow(u - cx, 2) + Math.Pow(v - cy, 2);
            if (d < score) { score = d; best = p; }
        }
        Check(score < Double.MaxValue, "Could not find expected mouse target " + target + " side " + side);
        return best;
    }
    static PointF CheckRegions(AnimationClip clip, PetHitMap map)
    {
        Rectangle b = AnimationClip.VisibleBounds(clip.Frame);
        int upper = (int)Math.Ceiling(b.Top + b.Height * PetHitMap.BellyRegionTop);
        int lower = (int)Math.Ceiling(b.Top + b.Height * PetHitMap.RetreatRegionTop);
        var counts = new int[3]; int transparent = 0;
        PointF leg = PointF.Empty; double legScore = Double.MaxValue;
        for (int y = 0; y < clip.Frame.Height; y++)
        for (int x = 0; x < clip.Frame.Width; x++)
        {
            var p = new PointF(x + .5f, y + .5f);
            if (!map.Contains(p))
            {
                Check(map.Hit(p) == MousePetAction.None, "Transparent pixels triggered a mouse response.");
                transparent++; continue;
            }
            int region = y < upper ? 0 : y < lower ? 1 : 2;
            var expected = region == 0 ? MousePetAction.None : region == 1 ? MousePetAction.Belly : MousePetAction.Feet;
            Check(map.Hit(p) == expected, "Wrong action in region " + (region + 1) + " at " + p);
            counts[region]++;
            double v = (y - b.Top) / (double)b.Height;
            double u = (x - b.Left) / (double)b.Width;
            double score = Math.Pow(u - .40, 2) + Math.Pow(v - .80, 2);
            if (region == 2 && v >= .77 && v <= .83 && score < legScore
                && map.Contains(new PointF(x - 2, y)) && map.Contains(new PointF(x + 2, y)))
            { leg = p; legScore = score; }
        }
        foreach (int row in new[] { upper - 1, upper, lower - 1, lower })
        {
            bool checkedPixel = false;
            for (int x = b.Left; x < b.Right; x++)
            {
                var p = new PointF(x + .5f, row + .5f);
                if (!map.Contains(p)) continue;
                var expected = row < upper ? MousePetAction.None : row < lower ? MousePetAction.Belly : MousePetAction.Feet;
                Check(map.Hit(p) == expected, "Region boundary selected the wrong action."); checkedPixel = true;
            }
            Check(checkedPixel, "Region boundary had no visible test pixels.");
        }
        Check(counts[0] > 0 && counts[1] > 0 && counts[2] > 0 && transparent > 0 && leg != PointF.Empty,
            "Region coverage is incomplete.");
        Check(map.Hit(new PointF(-1, -1)) == MousePetAction.None
            && map.Hit(new PointF(clip.Frame.Width, clip.Frame.Height)) == MousePetAction.None, "Outside-image clicks triggered an action.");
        Console.WriteLine("Regions passed: head inactive; belly-only spring; lower body retreats; both red-line boundaries and transparent pixels checked.");
        return leg;
    }
    static Point Client(Pet pet, PointF spritePoint)
    {
        var clips = Field<Dictionary<string, AnimationClip>>(pet, "clips");
        string state = Field<string>(pet, "animation");
        var frame = clips[state].Frame;
        RectangleF b = (RectangleF)typeof(Pet).GetMethod("SpriteBounds", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { state, Field<long>(pet, "clipTime"), Field<int>(pet, "jumpTime") });
        float scale = Field<float>(pet, "displayScale");
        return new Point((int)Math.Round((b.Left + spritePoint.X * b.Width / frame.Width) * scale),
            (int)Math.Round((b.Top + spritePoint.Y * b.Height / frame.Height) * scale));
    }
    static void Click(Pet pet, Point point, int count)
    {
        var e = new MouseEventArgs(MouseButtons.Left, count, point.X, point.Y, 0);
        Invoke(pet, "OnDown", pet, e); Invoke(pet, "OnUp", pet, e);
    }
    static void CheckFonts(Control root, Font expected)
    {
        Check(root.Font.Equals(expected), "Sound panel font differs from the main menu: " + root.GetType().Name);
        foreach (Control child in root.Controls) CheckFonts(child, expected);
    }
    static void CheckSoundPanel(Pet pet, string output)
    {
        var menu = Field<ContextMenuStrip>(pet, "menu");
        var panel = Field<SoundOptionsPopup>(pet, "audioSettings");
        var content = ((ToolStripControlHost)panel.Items[0]).Control;
        CheckFonts(content, menu.Font);
        Check(panel.Width < 300 * DisplayQuality.Scale, "Sound panel was not narrowed.");
        var slider = panel.Laugh;
        Check(slider.TrackBounds.Left <= 90 * DisplayQuality.Scale, "Sound labels and switches are too far apart.");
        using (var bitmap = new Bitmap(slider.Width, slider.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            typeof(SoundOptionSlider).GetMethod("OnPaint", Fields).Invoke(slider, new object[] { new PaintEventArgs(graphics, slider.ClientRectangle) });
            Color color = bitmap.GetPixel(slider.TrackBounds.Left + (int)(20 * DisplayQuality.Scale), slider.Height / 2);
            Check(color.B > color.G && color.G > color.R, "Selected sound option is not light blue.");
        }
        pet.ShowSoundSettings(); Application.DoEvents();
        using (var preview = new Bitmap(content.Width, content.Height))
        {
            content.DrawToBitmap(preview, new Rectangle(Point.Empty, content.Size));
            preview.Save(Path.Combine(output, "sound-panel-blue.png"), ImageFormat.Png);
        }
        menu.Close();
        Console.WriteLine("Sound panel passed: fonts match; compact label spacing; light-blue selected options.");
    }
    static void PreviewMotion(Pet pet, string output)
    {
        var rig = Field<LaughRig>(pet, "laughRig");
        using (var image = new Bitmap(1000, 650))
        using (var g = Graphics.FromImage(image))
        using (var attributes = new ImageAttributes())
        {
            g.Clear(Color.FromArgb(31, 39, 50));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            int[] times = { 0, 100, 300, 1200, 120, 280, 680, 1100 };
            for (int i = 0; i < times.Length; i++)
            {
                int x = i % 4 * 250, y = i / 4 * 325;
                var action = i < 4 ? MousePetAction.Belly : MousePetAction.Feet;
                g.DrawString((i < 4 ? "Belly " : "Retreat ") + times[i] + " ms", SystemFonts.MenuFont, Brushes.White, x + 10, y + 10);
                rig.DrawMouseReaction(g, action, times[i], 0, new RectangleF(x + 5, y + 65, 240, 240), attributes);
            }
            image.Save(Path.Combine(output, "mouse-motion.png"), ImageFormat.Png);
        }
    }
    public static void Run(string assets, string output)
    {
        Directory.CreateDirectory(output); DisplayQuality.Enable();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); Application.EnableVisualStyles();
        using (var pet = new Pet(assets))
        using (var timer = new Timer { Interval = 50 })
        {
            pet.Enabled = false;
            var clips = Field<Dictionary<string, AnimationClip>>(pet, "clips");
            var maps = Field<Dictionary<string, PetHitMap>>(pet, "hitMaps");
            var idle = clips["idle"]; var map = maps["idle"];
            PointF belly = Find(idle, map, MousePetAction.Belly, 0), left = Find(idle, map, MousePetAction.Feet, 0),
                right = Find(idle, map, MousePetAction.Feet, 1), head = Find(idle, map, MousePetAction.None, 0);
            PointF leg = CheckRegions(idle, map);
            Check(map.Hit(PointF.Empty) == MousePetAction.None, "Transparent padding triggered a reaction.");
            double low = 0, high = 0;
            for (int t = 0; t <= 1200; t += 10) { low = Math.Min(low, MouseMotion.BellyAt(t)); high = Math.Max(high, MouseMotion.BellyAt(t)); }
            Check(low >= -.46 && low <= -.35 && high >= .12 && high <= .25 && MouseMotion.BellyAt(1200) == 0,
                "Reduced belly motion did not compress, rebound and settle within the intended amplitude.");
            int stage = 0, frames = 0; long started = 0; Point before = Point.Empty;
            Exception failure = null;
            pet.Shown += (s, e) => {
                try
                {
                    pet.Location = new Point(Screen.PrimaryScreen.Bounds.Left + 100, Screen.PrimaryScreen.Bounds.Top + 80);
                    CheckSoundPanel(pet, output); PreviewMotion(pet, output);
                    Click(pet, Client(pet, head), 1);
                    Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None && Field<string>(pet, "animation") == "idle",
                        "Region 1 single click triggered an action.");
                    Click(pet, new Point(0, 0), 1);
                    Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None, "Transparent window padding triggered an action.");
                    Click(pet, Client(pet, leg), 1);
                    Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Feet, "Region 3 leg click did not trigger retreat.");
                    Invoke(pet, "FinishAction");
                    Click(pet, Client(pet, belly), 1);
                    Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Belly && Field<string>(pet, "animation") == "idle", "Single click did not start the belly response.");
                    started = Field<System.Diagnostics.Stopwatch>(pet, "clock").ElapsedMilliseconds;
                    frames = pet.RenderCount; timer.Start();
                }
                catch (Exception error) { failure = error; pet.Close(); }
            };
            timer.Tick += (s, e) => {
                try
                {
                    long now = Field<System.Diagnostics.Stopwatch>(pet, "clock").ElapsedMilliseconds;
                    long elapsed = now - started;
                    if (elapsed > 5000) throw new InvalidOperationException("Mouse response test timed out at stage " + stage);
                    if (stage == 0 && elapsed >= 1300)
                    {
                        Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None && pet.RenderCount - frames > 80,
                            "Belly feedback did not settle smoothly.");
                        before = pet.Location; Click(pet, Client(pet, left), 1);
                        Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Feet, "Left foot did not trigger retreat.");
                        started = now; stage = 1;
                    }
                    else if ((stage == 1 || stage == 2) && elapsed >= 1200)
                    {
                        Check(pet.Left > before.X + 40 * DisplayQuality.Scale && pet.Top == before.Y,
                            "Foot response did not retreat horizontally.");
                        Check(Field<string>(pet, "message") == "你干嘛，矮油~", "Retreat caption is missing.");
                        Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None, "Retreat did not settle.");
                        if (stage == 1)
                        {
                            before = pet.Location; Click(pet, Client(pet, right), 1);
                            Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.Feet, "Right foot did not trigger retreat.");
                            started = now; stage = 2;
                        }
                        else
                        {
                            Click(pet, Client(pet, belly), 1); Click(pet, Client(pet, belly), 2);
                            Check(Field<string>(pet, "animation") == "laugh" && Field<int>(pet, "jumpTime") < 0
                                && Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None, "Double click did not replace the single click with laughter.");
                            Invoke(pet, "FinishAction"); before = pet.Location;
                            Click(pet, Client(pet, left), 1); started = now; stage = 3;
                        }
                    }
                    else if (stage == 3 && elapsed >= 300)
                    {
                        Click(pet, Client(pet, left), 2);
                        Check(Field<string>(pet, "animation") == "laugh" && pet.Location == before,
                            "Double-clicking a foot left an unwanted retreat offset.");
                        Invoke(pet, "FinishAction");
                        var point = Client(pet, belly); var down = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
                        Invoke(pet, "OnDown", pet, down);
                        Point mouse = Field<Point>(pet, "mouseStart"); Invoke(pet, "MoveDraggedPet", new Point(mouse.X + 30, mouse.Y + 10));
                        Invoke(pet, "OnUp", pet, down);
                        Check(Field<MousePetAction>(pet, "mouseReaction") == MousePetAction.None && Field<string>(pet, "animation") == "idle",
                            "Dragging triggered a click action.");
                        Console.WriteLine("Mouse interactions passed: region 1 inactive; region 2 springs the belly; region 3 legs and both feet retreat with the updated caption; padding ignored; double click laughs without jumping or retreat offset; dragging stays independent.");
                        stage = 4; timer.Stop(); pet.Close();
                    }
                }
                catch (Exception error) { failure = error; timer.Stop(); pet.Close(); }
            };
            Application.Run(pet);
            if (failure != null) throw failure;
            Check(stage == 4, "Mouse tests ended early.");
        }
    }
}
