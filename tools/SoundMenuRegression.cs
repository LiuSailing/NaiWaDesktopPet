using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NaiwaDesktopPet;

// Run the real menu mouse tracking and its delayed cascade timer in a UI loop.
// Repeated hover must not move, rebuild, close or repaint the open popup.
public static class SoundMenuRegression
{
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    static readonly MethodInfo dispatch = typeof(ToolStrip).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo sliderDown = typeof(SoundOptionSlider).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo sliderMove = typeof(SoundOptionSlider).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo sliderUp = typeof(SoundOptionSlider).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);

    static T Field<T>(Pet pet, string name)
    { return (T)typeof(Pet).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pet); }
    static void SetField(Pet pet, string name, object value)
    { typeof(Pet).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pet, value); }
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static void Hover(ContextMenuStrip menu, ToolStripItem item, int offset)
    {
        var point = new Point(item.Bounds.Left + item.Width / 2 + offset, item.Bounds.Top + item.Height / 2);
        // Dispatch the actual control message path, including hit-test selection.
        item.Select();
        Cursor.Position = menu.PointToScreen(point);
        var message = Message.Create(menu.Handle, 0x0200, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xFFFF)));
        dispatch.Invoke(menu, new object[] { message });
    }
    static bool Above(IntPtr upper, IntPtr lower)
    {
        for (IntPtr w = GetWindow(lower, 3); w != IntPtr.Zero; w = GetWindow(w, 3)) if (w == upper) return true;
        return false;
    }

    static void CheckDragEdges(Pet pet)
    {
        Point originalLocation = pet.Location;
        string originalAnimation = Field<string>(pet, "animation"), originalMessage = Field<string>(pet, "message");
        long originalTime = Field<long>(pet, "clipTime");
        var move = typeof(Pet).GetMethod("MoveDraggedPet", BindingFlags.Instance | BindingFlags.NonPublic);
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        try
        {
            foreach (string state in new[] { "idle", "listen", "laugh", "rest" })
            {
                SetField(pet, "animation", state); SetField(pet, "clipTime", state == "laugh" ? 1700L : 0L);
                SetField(pet, "message", "edge test");
                pet.Render(true);
                Rectangle body = Field<Rectangle>(pet, "dragFootprint");
                Check(body.Left > 0 && body.Top > 0, "Caption or transparent canvas was included in the body footprint.");
                foreach (bool bottomRight in new[] { false, true })
                {
                    pet.Location = originalLocation;
                    SetField(pet, "windowStart", originalLocation);
                    SetField(pet, "mouseStart", new Point(originalLocation.X + body.Left + body.Width / 2, originalLocation.Y + body.Top + body.Height / 2));
                    SetField(pet, "dragging", true); SetField(pet, "moved", false);
                    Point cursor = bottomRight ? new Point(screen.Right - 1, screen.Bottom - 1) : new Point(screen.Left + 1, screen.Top + 1);
                    move.Invoke(pet, new object[] { cursor });
                    Rectangle placed = body; placed.Offset(pet.Location);
                    Check(bottomRight ? placed.Right == screen.Right && placed.Bottom == screen.Bottom
                        : placed.Left == screen.Left && placed.Top == screen.Top, state + " could not reach the physical display edge.");
                    Check(screen.Contains(placed), "Character moved entirely or partly outside the display.");
                }
                var negativeScreen = new Rectangle(-1920, -300, 1920, 1080);
                Point negative = Pet.ClampCharacterLocation(new Point(-100000, -100000), body, negativeScreen);
                Check(negative.X + body.Left == negativeScreen.Left && negative.Y + body.Top == negativeScreen.Top,
                    "Dragging broke on a display with negative desktop coordinates.");
            }
            Console.WriteLine("Drag passed: all four visible poses reach every screen edge, including the taskbar; transparent padding/captions ignored; negative coordinates supported.");
        }
        finally
        {
            SetField(pet, "dragging", false); SetField(pet, "moved", false);
            SetField(pet, "animation", originalAnimation); SetField(pet, "clipTime", originalTime); SetField(pet, "message", originalMessage);
            pet.Location = originalLocation; pet.Render();
        }
    }

    public static void Run(string assets)
    {
        DisplayQuality.Enable();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        using (var pet = new Pet(assets))
        using (var timer = new Timer { Interval = 50 })
        {
            var menu = Field<ContextMenuStrip>(pet, "menu");
            var item = Field<ToolStripMenuItem>(pet, "audioMenu");
            var child = Field<SoundOptionsPopup>(pet, "audioSettings");
            int opened = 0, closed = 0, moved = 0, resized = 0;
            int round = 0, stage = 0, frames = 0;
            Rectangle stable = Rectangle.Empty;
            IntPtr stableHandle = IntPtr.Zero;
            Exception failure = null;
            child.Opened += (s, e) => opened++;
            child.Closed += (s, e) => closed++;
            child.LocationChanged += (s, e) => { if (child.Visible) moved++; };
            child.SizeChanged += (s, e) => { if (child.Visible) resized++; };
            var work = Screen.PrimaryScreen.WorkingArea;
            Point[] locations = {
                new Point(work.Left + 100, work.Top + 70),
                new Point(work.Right - 250, work.Top + 300),
                new Point(work.Right - 40, work.Bottom - 40)
            };
            pet.Shown += (s, e) => { pet.Activate(); CheckDragEdges(pet); timer.Start(); };
            timer.Tick += (s, e) => {
                try
                {
                    if (stage == 0)
                    {
                        pet.Activate();
                        menu.Show(pet, pet.PointToClient(locations[round]));
                        Check(menu.Visible && !child.Visible, "Child must be collapsed initially.");
                        opened = closed = moved = resized = 0;
                        Hover(menu, item, 0); frames = 0; stage = 1;
                    }
                    else if (stage == 1)
                    {
                        if (++frames < 20) return;
                        Check(child.Visible, "Native hover did not open the sound submenu. root=" + menu.Visible + "; selected=" + item.Selected + "; opened=" + opened + "; closed=" + closed + "; cursor=" + Cursor.Position + "; menu=" + menu.Bounds);
                        Check(opened == 1 && closed == 0, "Hover reopened or closed the submenu.");
                        Check(child.Top == menu.Top && child.Bottom == menu.Bottom, "Menu heights/top edges differ.");
                        Check(child.Right == menu.Left || child.Left == menu.Right, "Menus must touch without overlap.");
                        Check(Above(menu.Handle, pet.Handle) && Above(child.Handle, pet.Handle), "Menus must be above the pet.");
                        var rows = new[] { child.Laugh, child.Jump, child.Rest, child.Listen };
                        for (int row = 0; row < rows.Length; row++)
                        {
                            var option = rows[row];
                            Check(option.AccessibleName == menu.Items[row].Text, "Sound settings order differs from the action menu.");
                            Check(row == 0 || option.Top >= rows[row - 1].Bottom, "Sound settings rows are in the wrong visual order.");
                            var track = (RectangleF)typeof(SoundOptionSlider).GetProperty("Track", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(option, null);
                            Check(option.Top >= 0 && option.Bottom <= option.Parent.ClientSize.Height, "Sound row was clipped.");
                            Check(track.Top >= 0 && track.Bottom <= option.Height, "Sound slider was clipped.");
                        }
                        stable = child.Bounds; stableHandle = child.Handle;
                        moved = resized = 0; frames = 0; stage = 2;
                    }
                    else if (stage == 2)
                    {
                        Hover(menu, item, frames % 3 - 1);
                        Check(menu.Visible && child.Visible, "Repeated hover closed a popup.");
                        Check(opened == 1 && closed == 0 && child.Handle == stableHandle, "Repeated hover rebuilt the popup.");
                        Check(child.Bounds == stable && moved == 0 && resized == 0, "Visible popup moved or resized during hover.");
                        if (++frames < 30) return;
                        using (var image = new Bitmap(child.Width, child.Height))
                        {
                            child.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                            Check(image.GetPixel(image.Width - 10, 10).GetBrightness() > .7f, "Popup background was not painted.");
                        }
                        Hover(menu, menu.Items[0], 0); frames = 0; stage = 3;
                    }
                    else if (stage == 3)
                    {
                        if (++frames < 20) return;
                        Check(menu.Visible && !child.Visible, "Moving to a different menu row must close the submenu.");
                        Hover(menu, item, 0); frames = 0; stage = 4;
                    }
                    else if (stage == 4)
                    {
                        if (++frames < 20) return;
                        Check(child.Visible, "Second hover did not reopen the submenu. cursor=" + Cursor.Position
                            + "; expected=" + menu.PointToScreen(new Point(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2))
                            + "; selected=" + item.Selected + "; opened=" + opened + "; closed=" + closed);
                        var slider = child.Rest;
                        float scale = DisplayQuality.Scale;
                        int x1 = slider.TrackBounds.Left + 5, x2 = slider.TrackBounds.Right - 5, y = slider.Height / 2;
                        sliderDown.Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x1, y, 0) });
                        sliderMove.Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 0, x2, y, 0) });
                        sliderUp.Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x2, y, 0) });
                        Check(slider.SelectedIndex == 2 && menu.Visible && child.Visible, "Slider drag closed the menu or failed.");
                        slider.SelectedIndex = 0;
                        slider = child.Listen; y = slider.Height / 2;
                        sliderDown.Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x1, y, 0) });
                        sliderMove.Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 0, x2, y, 0) });
                        sliderUp.Invoke(slider, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x2, y, 0) });
                        Check(slider.SelectedIndex == 1 && menu.Visible && child.Visible, "Thinking slider drag failed or closed the menu.");
                        slider.SelectedIndex = 0;
                        menu.Close();
                        Check(!menu.Visible && !child.Visible, "Closing the main menu left the child open.");
                        Console.WriteLine("Position {0}: native hover stable; no visible move/resize; heights aligned; sliders and close linkage passed.", round + 1);
                        round++; stage = 0;
                        if (round == locations.Length) { timer.Stop(); pet.Close(); }
                    }
                }
                catch (Exception error) { failure = error; timer.Stop(); pet.Close(); }
            };
            Application.Run(pet);
            if (failure != null) throw failure;
        }
    }
}
