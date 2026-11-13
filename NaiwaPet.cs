using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace NaiwaDesktopPet
{
    public sealed class Pet : Form
    {
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        readonly Dictionary<string, PetHitMap> hitMaps = new Dictionary<string, PetHitMap>();
        FrameClock frameClock;
        LayeredSurface surface, retiringSurface;
        bool canvasPresentationPending, flightCommandPending;
        Point canvasWindowLocation;
        readonly LaughRig laughRig;
        readonly RestRig restRig;
        readonly FlightRig flightRig;
        readonly Rectangle idleImageBounds;
        readonly ActionSound laughSound;
        readonly ActionSound restMaleSound, restFemaleSound, jumpSound, listenSound;
        readonly SoundOptionSlider laughAudioOption, restAudioOption, jumpAudioOption, listenAudioOption;
        readonly SoundOptionsPopup audioSettings;
        readonly ToolStripMenuItem audioMenu;
        ActionSound activeSound;
        bool laughFollowsSound, restCuePending, jumpCuePending;
        long audioPollAt;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Stopwatch clock = new Stopwatch();
        readonly Random random = new Random();
        readonly BlinkMotion blink = new BlinkMotion();

        readonly ToolStripMenuItem pauseItem;
        readonly ToolStripMenuItem fpsItem;
        readonly ToolStripMenuItem flightMenuItem;

        readonly ImageAttributes imageAttributes = new ImageAttributes();
        readonly GraphicsPath bubblePath = Rounded(new Rectangle(38, 8, 184, 42), 15);
        readonly Brush bubbleFill = new SolidBrush(Color.FromArgb(246, 255, 253, 235));
        readonly Pen bubblePen = new Pen(Color.FromArgb(235, 180, 188, 81), 2);
        readonly Font bubbleFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        readonly Brush bubbleText = new SolidBrush(Color.FromArgb(81, 80, 38));
        readonly StringFormat bubbleFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        readonly PointF[] corners = new PointF[3];
        float displayScale;
        long fpsStart;
        int fpsFrames;
        List<double> benchmarkTimes;
        List<double> benchmarkRenderCosts;
        int benchmarkPausedTicks;

        Point mouseStart, windowStart;
        Point retreatStart;
        Rectangle retreatScreen;
        MousePetAction mouseReaction, clickTarget;
        long reactionTime;
        bool flying, holdArmed, holdTriggered, facingRight;
        long holdStarted, flightTime;
        int flightDuration = FlightMotion.Duration;
        Point flightStart, flightEnd, flightCanvasOffset, flightWindowLocation;
        Rectangle flightScreen, flightBody;
        const int FlightPaddingX = 120, FlightPaddingY = 60;
        Rectangle dragFootprint;
        bool dragging, moved, paused, closed, doubleClicked;
        string animation = "idle", message;
        long lastTime, clipTime, actionRemaining, messageRemaining, nextIdle = 10000;
        int jumpTime = -1;
        const int JumpDuration = 1000;
        public int RenderCount { get; private set; }
        public double MeasuredFps { get; private set; }
        public bool OpenAudioSettingsOnStart { get; set; }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080;
                if (benchmarkTimes != null) cp.ExStyle |= 0x20;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public Pet(string assetDirectory)
        {
            try
            {
                foreach (string name in new[] { "idle", "listen", "laugh", "rest" })
                {
                    clips.Add(name, new AnimationClip(assetDirectory, name));
                    hitMaps.Add(name, new PetHitMap(clips[name].Frame));
                }
                laughRig = new LaughRig(clips["idle"].Frame, clips["laugh"].Frame, clips["idle"].WithBlink(1), closure => clips["idle"].WithBlink(closure));
                restRig = new RestRig(clips["rest"]);
                flightRig = new FlightRig(Path.Combine(assetDirectory, "flight-wing.png"));
                idleImageBounds = AnimationClip.VisibleBounds(clips["idle"].Frame);
                laughSound = new ActionSound(Path.Combine(assetDirectory, "..", "audio", "LaughAloud.mp3"));
                restMaleSound = new ActionSound(Path.Combine(assetDirectory, "..", "audio", "sucai999-relax-male.mp3"));
                restFemaleSound = new ActionSound(Path.Combine(assetDirectory, "..", "audio", "sucai999-relax-female.mp3"));
                jumpSound = new ActionSound(Path.Combine(assetDirectory, "..", "audio", "aigei.comspace7642162-huohua.mp3"));
                listenSound = new ActionSound(Path.Combine(assetDirectory, "..", "audio", "giao-ge-en-1.mp3"));
            }
            catch { foreach (var clip in clips.Values) clip.Dispose(); throw; }

            Text = "奶蛙桌宠";
            AccessibleName = "奶蛙桌宠";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            displayScale = DisplayQuality.Scale;
            ClientSize = new Size((int)Math.Round(260 * displayScale), (int)Math.Round(320 * displayScale));
            var work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Right - Width - 24, work.Bottom - Height - 12);
            Cursor = Cursors.Hand;

            menu.Items.Add("捧腹大笑", null, (s, e) => Act("laugh", "哈哈哈哈～", 2, true));
            menu.Items.Add("跳一下", null, (s, e) => Hop());
            menu.Items.Add("躺平奶蛙", null, (s, e) => Act("rest", "躺平啦～", 5, true));
            menu.Items.Add("沉思奶蛙", null, (s, e) => Act("listen", "我在听呢～", 2, true));
            menu.Items.Add(new ToolStripSeparator());
            audioMenu = new SoundOptionsMenuItem("音效设置");
            audioSettings = new SoundOptionsPopup(displayScale, menu.Font) { Renderer = menu.Renderer };
            laughAudioOption = audioSettings.Laugh;
            restAudioOption = audioSettings.Rest;
            jumpAudioOption = audioSettings.Jump;
            listenAudioOption = audioSettings.Listen;
            audioMenu.DropDown = audioSettings;
            // Open only on entry, after selection. MouseMove coordinates may be
            // item-relative, so using them to hit-test the parent closes the
            // submenu again while the native hover timer tries to reopen it.
            audioMenu.MouseEnter += (s, e) => {
                audioMenu.Select();
                if (!audioSettings.Visible) audioMenu.ShowDropDown();
            };
            // Size and position are final before the first paint.
            audioMenu.DropDownOpening += (s, e) => audioSettings.FitHeight(menu.Height);
            laughAudioOption.SelectedIndexChanged += (s, e) => {
                if (laughAudioOption.SelectedIndex == 0 && activeSound == laughSound) FinishAction();
            };
            restAudioOption.SelectedIndexChanged += (s, e) => {
                if (activeSound == restMaleSound || activeSound == restFemaleSound) StopActiveSound();
            };
            jumpAudioOption.SelectedIndexChanged += (s, e) => {
                if (jumpAudioOption.SelectedIndex == 0 && activeSound == jumpSound) StopActiveSound();
            };
            listenAudioOption.SelectedIndexChanged += (s, e) => {
                if (listenAudioOption.SelectedIndex == 0 && activeSound == listenSound) StopActiveSound();
            };
            menu.Items.Add(audioMenu);
            var mouseMenu = new ToolStripMenuItem("鼠标动作");
            mouseMenu.DropDownItems.Add("单击肚子：回弹", null, (s, e) => ReactToMouse(MousePetAction.Belly));
            mouseMenu.DropDownItems.Add("单击腿脚：后退", null, (s, e) => ReactToMouse(MousePetAction.Feet));
            mouseMenu.DropDownItems.Add("双击奶蛙：捧腹大笑", null, (s, e) => Act("laugh", "哈哈哈哈～", 2, true));
            flightMenuItem = new ToolStripMenuItem("长按奶蛙：展翼飞行", null, (s, e) => RequestFlight());
            mouseMenu.DropDownItems.Add(flightMenuItem);
            menu.Items.Add(mouseMenu);
            menu.Items.Add(new ToolStripSeparator());
            fpsItem = new ToolStripMenuItem("绘制目标：100 FPS") { Enabled = false };
            menu.Items.Add(fpsItem);

            pauseItem = new ToolStripMenuItem("暂停动画") { CheckOnClick = true };
            pauseItem.CheckedChanged += (s, e) => paused = pauseItem.Checked;
            menu.Items.Add(pauseItem);
            var topmost = new ToolStripMenuItem("始终置顶") { Checked = true, CheckOnClick = true };
            topmost.CheckedChanged += (s, e) => TopMost = topmost.Checked;
            menu.Items.Add(topmost);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出奶蛙", null, (s, e) => Close());

            imageAttributes.SetWrapMode(WrapMode.TileFlipXY);
            Shown += (s, e) =>
            {
                surface = new LayeredSurface(Width, Height);
                clock.Start(); lastTime = clock.ElapsedMilliseconds; Render();
                frameClock = new FrameClock(Handle);
                if (OpenAudioSettingsOnStart) BeginInvoke((Action)ShowSoundSettings);
            };
            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ActionSound.CompletionMessage)
            {
                if (activeSound != null && (uint)m.LParam.ToInt64() == activeSound.DeviceId
                    && (m.WParam.ToInt64() == 1 || m.WParam.ToInt64() == 8) && activeSound.HasCompleted)
                    CompleteActiveSound();
                return;
            }
            if (m.Msg == FrameClock.RenderMessage)
            {
                // Keep at most one render in flight, including the draw itself.
                // Otherwise a costly frame can starve menu/input/timer messages.
                try { if (!closed) Tick(this, EventArgs.Empty); }
                finally { if (frameClock != null) frameClock.Consumed(); }
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x02E0 && surface != null && !closed)
            {
                StopFlight();
                Point anchor = CurrentBodyAnchor();
                displayScale = (m.WParam.ToInt32() & 0xFFFF) / 96f;
                ResizeFlightCanvas(false, anchor); Render(dragging);
                if (dragging) { mouseStart = Cursor.Position; windowStart = Location; }
            }
        }

        void ResumeAnimation() { pauseItem.Checked = false; }
        public void ShowSoundSettings()
        {
            if (!menu.Visible) menu.Show(this, new Point(Width - (int)(20 * displayScale), (int)(80 * displayScale)));
            audioMenu.Select(); audioMenu.ShowDropDown();
        }
        void StopActiveSound()
        {
            if (activeSound != null) activeSound.Stop();
            activeSound = null; laughFollowsSound = false;
        }
        void PlaySound(ActionSound sound)
        {
            StopActiveSound(); activeSound = sound;
            sound.PlayFromStart(Handle); audioPollAt = clock.ElapsedMilliseconds + 250;
        }
        void CompleteActiveSound()
        {
            bool endLaugh = laughFollowsSound && animation == "laugh";
            if (activeSound != null) activeSound.Complete();
            activeSound = null; laughFollowsSound = false;
            if (endLaugh) { FinishAction(); Render(); }
        }
        void FinishAction()
        {
            StopFlight();
            StopActiveSound(); animation = "idle"; clipTime = 0; actionRemaining = 0;
            mouseReaction = MousePetAction.None; reactionTime = 0;
            restCuePending = false; jumpCuePending = false;
            message = null; messageRemaining = 0; nextIdle = random.Next(12000, 22000);
        }
        void MuteTestSounds()
        { foreach (var sound in new[] { laughSound, restMaleSound, restFemaleSound, jumpSound, listenSound }) sound.SetVolume(0); }
        public void Act(string name, string words, int loops)
        { Act(name, words, loops, false); }

        public void Act(string name, string words, int loops, bool playSound)
        {
            StopFlight(); holdArmed = false;
            StopActiveSound();
            ResumeAnimation();
            animation = name; clipTime = 0;
            mouseReaction = MousePetAction.None; reactionTime = 0;
            actionRemaining = clips[name].Duration * loops;
            if (name == "laugh") actionRemaining = Math.Max(6000, actionRemaining);
            restCuePending = name == "rest" && playSound; jumpCuePending = false;
            if (name == "laugh" && playSound && laughAudioOption.SelectedIndex == 1)
            {
                actionRemaining = Math.Max(actionRemaining, laughSound.DurationMilliseconds);
                PlaySound(laughSound); laughFollowsSound = true;
            }
            if (name == "listen" && playSound && listenAudioOption.SelectedIndex == 1)
                PlaySound(listenSound);
            if (name == "rest") actionRemaining = Math.Max(RestRig.MinimumDuration, actionRemaining);
            message = String.IsNullOrEmpty(words) ? null : words; messageRemaining = name == "laugh" ? 6000 : 2800;
            if (name == "rest") messageRemaining = 5500;
            if (name == "laugh" && playSound) messageRemaining = actionRemaining;
            jumpTime = -1;
            nextIdle = random.Next(12000, 22000);
            Render();
        }
        public void Hop()
        {
            StopFlight(); holdArmed = false;
            StopActiveSound(); restCuePending = false; jumpCuePending = true;
            mouseReaction = MousePetAction.None; reactionTime = 0;
            ResumeAnimation();
            animation = "idle"; clipTime = 0; actionRemaining = 0;
            jumpTime = 0; nextIdle = random.Next(12000, 22000);
            message = "蹦！"; messageRemaining = 1500;
            Render();
        }

        PointF ToSpritePoint(Point clientPoint)
        {
            RectangleF sprite = SpriteBounds(animation, clipTime, jumpTime);
            Bitmap frame = clips[animation].Frame;
            float x = ((clientPoint.X - flightCanvasOffset.X) / displayScale - sprite.Left) * frame.Width / sprite.Width;
            if (facingRight) x = hitMaps[animation].Bounds.Left + hitMaps[animation].Bounds.Right - x;
            return new PointF(x, ((clientPoint.Y - flightCanvasOffset.Y) / displayScale - sprite.Top) * frame.Height / sprite.Height);
        }
        Point CurrentBodyAnchor()
        {
            Point location = canvasPresentationPending ? canvasWindowLocation : Location;
            return new Point(location.X + flightCanvasOffset.X, location.Y + flightCanvasOffset.Y);
        }
        void ResizeFlightCanvas(bool expanded, Point bodyAnchor)
        {
            var size = new Size((int)Math.Round((260 + (expanded ? FlightPaddingX * 2 : 0)) * displayScale),
                (int)Math.Round((320 + (expanded ? FlightPaddingY * 2 : 0)) * displayScale));
            var next = new LayeredSurface(size.Width, size.Height);
            // Keep the displayed buffer until its complete replacement is ready.
            // A second staged transition can discard only the unpresented buffer.
            if (retiringSurface == null) retiringSurface = surface;
            else if (surface != null) surface.Dispose();
            surface = next;
            flightCanvasOffset = expanded ? new Point((int)Math.Round(FlightPaddingX * displayScale), (int)Math.Round(FlightPaddingY * displayScale)) : Point.Empty;
            canvasWindowLocation = new Point(bodyAnchor.X - flightCanvasOffset.X, bodyAnchor.Y - flightCanvasOffset.Y);
            canvasPresentationPending = true;
        }
        void RequestFlight()
        {
            if (flightCommandPending || closed) return;
            flightCommandPending = true;
            // Let the whole menu cascade close before changing its owner window.
            menu.Close(ToolStripDropDownCloseReason.ItemClicked);
            BeginInvoke((Action)(() => {
                flightCommandPending = false;
                if (!closed && !IsDisposed) BeginFlight();
            }));
        }
        void BeginFlight()
        {
            ResumeAnimation();
            if (flying)
            {
                // Arrival precedes the fold/fade tail. A new command at the edge
                // starts the return trip immediately instead of being swallowed.
                if (flightTime < flightDuration - 1300) return;
                StopFlight();
            }
            FinishAction(); jumpTime = -1;
            holdArmed = false; dragging = false; Capture = false;
            Render(true, false); flightStart = CurrentBodyAnchor();
            flightBody = dragFootprint;
            flightScreen = Screen.FromRectangle(new Rectangle(flightStart.X + flightBody.Left, flightStart.Y + flightBody.Top, flightBody.Width, flightBody.Height)).Bounds;
            flightEnd = FlightMotion.Destination(flightStart, flightBody, flightScreen, facingRight);
            facingRight = flightEnd.X > flightStart.X;
            // Measure the final facing before planning the exact edge landing.
            Render(true, false); flightBody = dragFootprint;
            flightEnd = new Point(facingRight ? flightScreen.Right - flightBody.Right : flightScreen.Left - flightBody.Left, flightStart.Y);
            flightDuration = FlightMotion.DurationFor(flightStart, flightEnd, flightScreen);
            flightTime = 0; flying = true;
            ResizeFlightCanvas(true, flightStart);
            flightWindowLocation = canvasWindowLocation;
            Render();
        }
        void StopFlight()
        {
            if (!flying) return;
            Point anchor = flightTime >= flightDuration ? flightEnd : CurrentBodyAnchor();
            flying = false; flightTime = 0;
            ResizeFlightCanvas(false, anchor);
            nextIdle = random.Next(12000, 22000);
        }
        void AdvanceFlight(int delta)
        {
            if (!flying) return;
            flightTime = Math.Min(flightDuration, flightTime + delta);
            Point anchor = FlightMotion.LocationAt(flightStart, flightEnd, flightBody, flightScreen, flightTime, displayScale, flightDuration);
            // UpdateLayeredWindow presents pixels and their position together.
            // Calling Form.Location too would move the same layered window twice.
            flightWindowLocation = new Point(anchor.X - flightCanvasOffset.X, anchor.Y - flightCanvasOffset.Y);
            if (flightTime >= flightDuration) StopFlight();
        }
        void ReactToMouse(MousePetAction action)
        {
            if (action == MousePetAction.None) return;
            holdArmed = false;
            FinishAction(); ResumeAnimation(); jumpTime = -1;
            mouseReaction = action;
            if (action == MousePetAction.Feet)
            {
                message = "你干嘛，矮油~"; messageRemaining = 4500;
                Render(true); retreatStart = Location;
                retreatScreen = Screen.FromRectangle(new Rectangle(Left + dragFootprint.Left, Top + dragFootprint.Top,
                    dragFootprint.Width, dragFootprint.Height)).Bounds;
            }
            Render();
        }
        void AdvanceMouseReaction(int delta)
        {
            if (mouseReaction == MousePetAction.None) return;
            reactionTime += delta;
            if (mouseReaction == MousePetAction.Feet)
            {
                // Step away from the current facing direction.
                int distance = (int)Math.Round(54 * displayScale * MouseMotion.RetreatAt(reactionTime)) * (facingRight ? -1 : 1);
                Location = ClampCharacterLocation(new Point(retreatStart.X + distance, retreatStart.Y), dragFootprint, retreatScreen);
            }
            if (reactionTime >= (mouseReaction == MousePetAction.Belly ? MouseMotion.BellyDuration : MouseMotion.RetreatDuration))
                mouseReaction = MousePetAction.None;
        }

        void Tick(object sender, EventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            int delta = (int)Math.Min(150, now - lastTime);
            lastTime = now;
            if (holdArmed && dragging && !moved && !paused && !menu.Visible && now - holdStarted >= FlightMotion.HoldMilliseconds)
            {
                holdTriggered = true; BeginFlight();
            }
            if (activeSound != null) activeSound.SetPaused(paused || dragging || menu.Visible);
            if (paused || dragging || menu.Visible)
            { if (benchmarkTimes != null) benchmarkPausedTicks++; fpsStart = now; fpsFrames = 0; return; }
            clipTime += delta;
            blink.Advance(delta);
            AdvanceFlight(delta);
            AdvanceMouseReaction(delta);
            if (activeSound != null && now >= audioPollAt)
            {
                audioPollAt = now + 100;
                if (activeSound.HasCompleted) CompleteActiveSound();
            }
            if (animation == "rest" && restCuePending && clipTime >= 1150)
            {
                restCuePending = false;
                if (restAudioOption.SelectedIndex == 1) PlaySound(restMaleSound);
                else if (restAudioOption.SelectedIndex == 2) PlaySound(restFemaleSound);
            }
            if (jumpTime >= 180 && jumpCuePending)
            {
                jumpCuePending = false;
                if (jumpAudioOption.SelectedIndex == 1) PlaySound(jumpSound);
            }
            if (message != null && !laughFollowsSound && (messageRemaining -= delta) <= 0) message = null;
            if (jumpTime >= 0 && (jumpTime += delta) >= JumpDuration) jumpTime = -1;
            if (animation != "idle" && (actionRemaining -= delta) <= 0 && !laughFollowsSound) FinishAction();
            if (animation == "idle" && jumpTime < 0 && !flying && mouseReaction == MousePetAction.None && (nextIdle -= delta) <= 0)
            {
                Act("listen", null, 1);
            }
            Render();
            fpsFrames++;
            if (now - fpsStart >= 1000)
            {
                MeasuredFps = fpsFrames * 1000.0 / (now - fpsStart);
                fpsItem.Text = "绘制：" + MeasuredFps.ToString("F1") + " / 100 FPS";
                fpsFrames = 0; fpsStart = now;
            }
        }

        void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (flying)
                e = new MouseEventArgs(e.Button, e.Clicks, e.X - flightCanvasOffset.X, e.Y - flightCanvasOffset.Y, e.Delta);
            StopFlight();
            dragging = true; moved = false; doubleClicked = e.Clicks > 1; Capture = true;
            holdTriggered = false; holdStarted = clock.ElapsedMilliseconds;
            holdArmed = !doubleClicked && hitMaps[animation].Contains(ToSpritePoint(e.Location));
            clickTarget = hitMaps[animation].Hit(ToSpritePoint(e.Location));
            if (doubleClicked && mouseReaction == MousePetAction.Feet)
            { Location = retreatStart; mouseReaction = MousePetAction.None; reactionTime = 0; }
            mouseStart = Cursor.Position; windowStart = CurrentBodyAnchor();
            Render(true);
        }
        void OnMove(object sender, MouseEventArgs e)
        { MoveDraggedPet(Cursor.Position); }
        void MoveDraggedPet(Point p)
        {
            if (!dragging) return;
            int dx = p.X - mouseStart.X, dy = p.Y - mouseStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4)
            {
                holdArmed = false;
                if (!moved) { mouseReaction = MousePetAction.None; reactionTime = 0; }
                moved = true;
            }
            if (!moved) return;
            var candidate = new Point(windowStart.X + dx, windowStart.Y + dy);
            Location = ClampCharacterLocation(candidate, dragFootprint, Screen.FromPoint(p).Bounds);
        }
        internal static Point ClampCharacterLocation(Point candidate, Rectangle body, Rectangle screen)
        {
            // Transparent canvas margins may leave the display; the visible
            // character can reach its edges, including the taskbar area.
            int left = screen.Left - body.Left, top = screen.Top - body.Top;
            int right = Math.Max(left, screen.Right - body.Right);
            int bottom = Math.Max(top, screen.Bottom - body.Bottom);
            return new Point(Math.Max(left, Math.Min(right, candidate.X)), Math.Max(top, Math.Min(bottom, candidate.Y)));
        }
        void OnUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && holdTriggered)
            { holdTriggered = false; holdArmed = false; dragging = false; Capture = false; doubleClicked = false; return; }
            if (e.Button == MouseButtons.Left && dragging)
            {
                holdArmed = false;
                dragging = false; Capture = false;
                if (!moved)
                {
                    if (doubleClicked && hitMaps[animation].Contains(ToSpritePoint(e.Location)))
                        Act("laugh", "哈哈哈哈～", 2, true);
                    else if (!doubleClicked) ReactToMouse(clickTarget);
                }
                doubleClicked = false;
            }
            else if (e.Button == MouseButtons.Right) { holdArmed = false; menu.Show(this, PointToClient(Cursor.Position)); }
        }

        static RectangleF SpriteBounds(string state, long time, int jump)
        {
            // A constant X origin and width prevent breathing/jump squash from
            // making the whole pet drift sideways, including at fractional DPI.
            float sy = 1, rise = 0;
            if (state == "listen") sy += (float)(.006 * Math.Sin(time * Math.PI * 2 / 2800.0));
            else if (state == "idle") sy += (float)(.004 * Math.Sin(time * Math.PI * 2 / 3200.0));
            if (jump >= 0)
            {
                double t = (double)jump / JumpDuration;
                if (t < .18) sy -= .13f * (float)Math.Sin(t / .18 * Math.PI / 2);
                else if (t < .80)
                { rise = (float)(52 * Math.Sin((t - .18) / .62 * Math.PI)); sy = 1.04f; }
                else sy -= .14f * (float)Math.Sin((t - .80) / .20 * Math.PI);
            }
            float height = 240 * sy;
            return new RectangleF(10, 310 - height - rise, 240, height);
        }

        public void Render(bool measureDragFootprint = false, bool present = true)
        {
            if (!IsHandleCreated || IsDisposed || surface == null) return;
            double renderStarted = clock.Elapsed.TotalMilliseconds;
            {
                var g = surface.Graphics;
                g.ResetTransform(); g.Clear(Color.Transparent); g.ScaleTransform(displayScale, displayScale);
                if (flying) g.TranslateTransform(FlightPaddingX, FlightPaddingY);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                RectangleF bounds = SpriteBounds(animation, clipTime, jumpTime);
                GraphicsState facingState = null;
                if (facingRight)
                {
                    facingState = g.Save();
                    Rectangle silhouette = hitMaps[animation].Bounds;
                    float axis = bounds.Left + (silhouette.Left + silhouette.Right) * bounds.Width / (2f * clips[animation].Frame.Width);
                    g.TranslateTransform(axis * 2, 0); g.ScaleTransform(-1, 1);
                }
                GraphicsState flightState = null;
                if (flying)
                {
                    FlightPose pose = FlightMotion.At(flightTime, flightDuration);
                    float height = bounds.Height * (float)(1 - pose.Crouch);
                    bounds = new RectangleF(bounds.X, bounds.Bottom - height, bounds.Width, height);
                    float fw = clips["idle"].Frame.Width, fh = clips["idle"].Frame.Height;
                    var character = new RectangleF(bounds.X + idleImageBounds.X / fw * bounds.Width,
                        bounds.Y + idleImageBounds.Y / fh * bounds.Height,
                        idleImageBounds.Width / fw * bounds.Width, idleImageBounds.Height / fh * bounds.Height);
                    flightState = g.Save();
                    float centerX = character.Left + character.Width * .5f, centerY = character.Top + character.Height * .55f;
                    g.TranslateTransform(centerX, centerY); g.RotateTransform((float)pose.Tilt); g.TranslateTransform(-centerX, -centerY);
                    flightRig.Draw(g, pose, character);
                }
                corners[0] = new PointF(bounds.Left, bounds.Top); corners[1] = new PointF(bounds.Right, bounds.Top);
                corners[2] = new PointF(bounds.Left, bounds.Bottom);
                if (mouseReaction != MousePetAction.None)
                    laughRig.DrawMouseReaction(g, mouseReaction, reactionTime, blink.Closure, bounds, imageAttributes);
                else if (clips[animation].UsesSharedBodyLaugh)
                {
                    if (laughFollowsSound)
                        laughRig.DrawPlayback(g, clipTime, laughSound.PositionMilliseconds, laughSound.DurationMilliseconds, bounds, imageAttributes);
                    else laughRig.Draw(g, clipTime, bounds, imageAttributes);
                }
                else if (clips[animation].UsesHdRestPose)
                    restRig.Draw(g, clipTime, bounds, blink.Closure, imageAttributes);
                else
                {
                    var clip = clips[animation];
                    var frame = clip.WithBlink(blink.Closure);
                    g.DrawImage(frame, corners, new RectangleF(0, 0, frame.Width, frame.Height), GraphicsUnit.Pixel, imageAttributes);
                }
                if (flightState != null) g.Restore(flightState);
                if (facingState != null) g.Restore(facingState);
                // Read actual body pixels only when a drag begins. Captions
                // and transparent padding must never become a movement wall.
                if (measureDragFootprint) dragFootprint = surface.MeasureVisibleBounds();
                if (message != null)
                {
                    g.FillPath(bubbleFill, bubblePath); g.DrawPath(bubblePen, bubblePath);
                    g.DrawString(animation == "rest" ? RestRig.CaptionAt(clipTime) : message, bubbleFont, bubbleText, new RectangleF(38, 8, 184, 42), bubbleFormat);
                }
                if (!present) return;
                Point location = flying ? flightWindowLocation : canvasPresentationPending ? canvasWindowLocation : Location;
                surface.Present(Handle, location.X, location.Y);
                // Refresh managed bounds after the direct layered-window move;
                // this does not move the native window a second time.
                UpdateBounds();
                canvasPresentationPending = false;
                if (retiringSurface != null) { retiringSurface.Dispose(); retiringSurface = null; }
                RenderCount++;
                if (benchmarkTimes != null)
                {
                    double finished = clock.Elapsed.TotalMilliseconds;
                    benchmarkTimes.Add(finished); benchmarkRenderCosts.Add(finished - renderStarted);
                }
            }
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!closed)
            {
                closed = true;
                if (frameClock != null) frameClock.Dispose();
                clock.Stop(); if (surface != null) surface.Dispose();
                if (retiringSurface != null) retiringSurface.Dispose();
                menu.Dispose(); imageAttributes.Dispose(); bubblePath.Dispose(); bubbleFill.Dispose();
                laughRig.Dispose();
                restRig.Dispose();
                flightRig.Dispose();
                laughSound.Dispose();
                restMaleSound.Dispose(); restFemaleSound.Dispose(); jumpSound.Dispose(); listenSound.Dispose();
                bubblePen.Dispose(); bubbleFont.Dispose(); bubbleText.Dispose(); bubbleFormat.Dispose();
                foreach (var clip in clips.Values) clip.Dispose();
            }
            base.OnFormClosed(e);
        }

        public static string ValidateAssets(string directory)
        {
            var report = new List<string>();
            foreach (string name in new[] { "idle", "listen", "laugh", "rest" })
            using (var clip = new AnimationClip(directory, name))
            {
                if (clip.Frame.GetPixel(0, 0).A != 0) throw new InvalidDataException(name + " has an opaque background.");
                if (Math.Min(clip.SourceWidth, clip.SourceHeight) < 384)
                    throw new InvalidDataException(name + " is still using a low-resolution source.");
                report.Add(name + ": " + clip.SourceWidth + "x" + clip.SourceHeight + " high-resolution source, transparent background");
                if (clip.HasBlink) report.Add(clip.ValidateBlink(name));
            }

            report.Add(AnimationClip.ValidateSharedBody(directory));
            using (var rest = new AnimationClip(directory, "rest"))
            using (var rig = new RestRig(rest)) report.Add(rig.ValidateMotion());
            report.Add(FlightMotion.Validate());
            using (var idle = new AnimationClip(directory, "idle"))
            using (var laugh = new AnimationClip(directory, "laugh"))
            using (var rig = new LaughRig(idle.Frame, laugh.Frame, idle.WithBlink(1), closure => idle.WithBlink(closure))) report.Add(rig.ValidateMotion());
            using (var idle = new AnimationClip(directory, "idle"))
            using (var listen = new AnimationClip(directory, "listen"))
            {
                var standing = AnimationClip.VisibleBounds(idle.Frame);
                var sitting = AnimationClip.VisibleBounds(listen.Frame);
                if (sitting.Width > standing.Width * 1.28 || sitting.Height > standing.Height * .82 || Math.Abs(sitting.Bottom - standing.Bottom) > 1)
                    throw new InvalidDataException("Listening must stay compact and share the idle ground line.");
                report.Add("Listening scale: " + sitting.Width + "x" + sitting.Height + " vs idle " + standing.Width + "x" + standing.Height + "; shared ground line.");
            }
            foreach (string state in new[] { "idle", "listen", "laugh", "rest" })
            for (int time = 0; time <= 6000; time += 10)
            {
                RectangleF pose = SpriteBounds(state, time, -1);
                if (pose.Left != 10 || pose.Width != 240) throw new InvalidOperationException(state + " drifts horizontally.");
            }
            for (int time = 0; time <= JumpDuration; time += 10)
            {
                RectangleF pose = SpriteBounds("idle", time, time);
                if (pose.Left != 10 || pose.Width != 240) throw new InvalidOperationException("Jump drifts horizontally.");
            }
            report.Add("Horizontal anchor check passed: constant X and width throughout breathing, all states and jump.");
            return String.Join(Environment.NewLine, report.ToArray());
        }

        public static void SmokeTest(string directory)
        {
            Console.WriteLine(ValidateAssets(directory));
            using (var pet = new Pet(directory))
            using (var testTimer = new Timer())
            {
                pet.MuteTestSounds();
                if (pet.laughAudioOption.SelectedIndex != 0 || pet.restAudioOption.SelectedIndex != 0 || pet.jumpAudioOption.SelectedIndex != 0 || pet.listenAudioOption.SelectedIndex != 0)
                    throw new InvalidOperationException("All sound sliders must default to off.");
                pet.Act("laugh", null, 2, true);
                if (pet.activeSound != null) throw new InvalidOperationException("Default laugh must be silent.");
                pet.laughAudioOption.SelectedIndex = 1;
                int stage = 0;
                testTimer.Interval = 700;
                testTimer.Tick += (s, e) =>
                {
                    stage++;
                    if (stage == 1)
                    {
                        var click = new MouseEventArgs(MouseButtons.Left, 2, 145, 210, 0);
                        pet.OnDown(pet, click); pet.OnUp(pet, click);
                        if (pet.animation != "laugh" || !pet.clips["laugh"].UsesSharedBodyLaugh)
                            throw new InvalidOperationException("Click did not start the high-resolution laughter.");
                        if (pet.laughSound.HasCompleted || pet.actionRemaining < pet.laughSound.DurationMilliseconds)
                            throw new InvalidOperationException("Click did not queue the sound and matching animation duration.");
                        pet.pauseItem.Checked = true; pet.Tick(pet, EventArgs.Empty);
                        if (pet.laughSound.IsPlaying) throw new InvalidOperationException("Pause did not pause sound.");
                        pet.pauseItem.Checked = false; pet.Tick(pet, EventArgs.Empty);
                        if (pet.laughSound.HasCompleted) throw new InvalidOperationException("Resume lost the queued/playing sound.");
                        pet.Act("laugh", "测试重播", 2, true);
                        if (pet.laughSound.HasCompleted) throw new InvalidOperationException("Repeated laugh did not queue sound.");
                    }
                    else if (stage == 2)
                    {
                        pet.Act("rest", "测试躺平", 2);
                        if (pet.laughSound.IsPlaying) throw new InvalidOperationException("Switching actions did not stop sound.");
                        if (!pet.clips["rest"].UsesHdRestPose || !pet.clips["rest"].HasBlink)
                            throw new InvalidOperationException("Rest did not select the HD supine pose with blinking.");
                    }
                    else if (stage == 3)
                    {
                        pet.Act("listen", "测试倾听", 2);
                        if (pet.animation != "listen" || !pet.clips["listen"].UsesCalmListeningPose)
                            throw new InvalidOperationException("Listening did not select the calm pose.");
                    }
                    else if (stage == 4)
                    {
                        pet.Hop();
                        if (pet.jumpTime != 0 || pet.animation != "idle")
                            throw new InvalidOperationException("Jump menu action did not start a jump.");
                    }
                    else if (stage == 6) { testTimer.Stop(); pet.Close(); }
                };
                testTimer.Start(); Application.Run(pet);
                if (stage != 6 || pet.RenderCount < 20) throw new InvalidOperationException("Animation did not advance.");
                Console.WriteLine("Animation smoke test passed: " + pet.RenderCount + " rendered frames across all four clips and jump.");
                Console.WriteLine("Sound check passed: click plays MP3, matching duration, pause/resume, replay without overlap, action switch stops playback.");
            }
        }

        public static void AudioSmokeTest(string directory)
        {
            using (var pet = new Pet(directory))
            using (var test = new Timer { Interval = 50 })
            {
                // This diagnostic drives actions itself; desktop clicks must
                // not change the pose while it waits for native audio to end.
                pet.Enabled = false;
                pet.MuteTestSounds();
                int stage = -1; bool heardMale = false, heardFemale = false, heardJump = false, heardThinking = false, passed = false;
                long started = 0, lastTick = 0, largestTickGap = 0;
                long lastLaughPlayingAt = 0;
                int jumpFrames = 0, laughSamples = 0, tailLaughSamples = 0, lastLaughPosition = 0;
                Action startLaughTest = () => {
                    pet.Act("laugh", null, 2, true);
                    if (pet.activeSound != null) throw new InvalidOperationException("Disabled laugh sound played.");
                    pet.laughAudioOption.SelectedIndex = 1;
                    pet.Act("laugh", "测试播放完成", 2, true);
                    if (pet.laughSound.DurationMilliseconds < 12000 || pet.laughSound.DurationMilliseconds > 15000)
                        throw new InvalidOperationException("MP3 duration regression: Info frame bitrate was used instead of frame count.");
                    started = pet.clock.ElapsedMilliseconds; stage = 1;
                };
                pet.Shown += (s, e) => {
                    if (pet.laughAudioOption.SelectedIndex != 0 || pet.restAudioOption.SelectedIndex != 0 || pet.jumpAudioOption.SelectedIndex != 0 || pet.listenAudioOption.SelectedIndex != 0)
                        throw new InvalidOperationException("Sound defaults are not all off.");
                    pet.jumpAudioOption.SelectedIndex = 1;
                    var response = Stopwatch.StartNew(); pet.Hop();
                    if (response.ElapsedMilliseconds > 100) throw new InvalidOperationException("Jump input blocked the UI.");
                    started = lastTick = pet.clock.ElapsedMilliseconds; jumpFrames = pet.RenderCount;
                };
                test.Tick += (s, e) => {
                    long elapsed = pet.clock.ElapsedMilliseconds - started;
                    if (elapsed > 35000) throw new InvalidOperationException("Audio completion test timed out.");
                    if (stage == -1)
                    {
                        long now = pet.clock.ElapsedMilliseconds;
                        largestTickGap = Math.Max(largestTickGap, now - lastTick); lastTick = now;
                        if (pet.jumpSound.IsPlaying) heardJump = true;
                        if (elapsed < 2200) return;
                        if (!heardJump || pet.activeSound != null || pet.RenderCount - jumpFrames < 100 || largestTickGap > 200)
                            throw new InvalidOperationException("Jump audio check failed: largest UI timer gap " + largestTickGap
                                + " ms; frames=" + (pet.RenderCount - jumpFrames) + "; heard=" + heardJump + "; active=" + (pet.activeSound != null) + "; elapsed=" + elapsed + " ms.");
                        Console.WriteLine("Jump with audio: " + (pet.RenderCount - jumpFrames) + " frames; largest UI timer gap " + largestTickGap + " ms; playback finished.");
                        pet.jumpAudioOption.SelectedIndex = 0; startLaughTest();
                    }
                    else if (stage == 1)
                    {
                        if (pet.laughSound.IsPlaying)
                        {
                            int position = pet.laughSound.PositionMilliseconds;
                            if (position < lastLaughPosition || position > pet.laughSound.DurationMilliseconds)
                                throw new InvalidOperationException("Native audio playhead did not advance correctly.");
                            lastLaughPosition = position; lastLaughPlayingAt = elapsed;
                            LaughPose pose = LaughRig.DuringPlayback(pet.clipTime, position, pet.laughSound.DurationMilliseconds);
                            if (pet.animation != "laugh" || !pet.laughFollowsSound || pet.message == null
                                || (pet.clipTime >= 450 && pose.Strength < .67))
                                throw new InvalidOperationException("Laugh/caption ended while audio was playing.");
                            laughSamples++;
                            if (position >= pet.laughSound.DurationMilliseconds - 300) tailLaughSamples++;
                        }
                        if (pet.animation != "idle") return;
                        if (pet.activeSound != null || pet.laughSound.IsPlaying || elapsed < 10000 || elapsed > 20000
                            || laughSamples < 100 || tailLaughSamples == 0 || elapsed - lastLaughPlayingAt > 250)
                            throw new InvalidOperationException("Laughter did not end with actual playback.");
                        Console.WriteLine("Continuous laughter: " + laughSamples + " playback samples including " + tailLaughSamples
                            + " tail samples; natural completion at " + elapsed + " ms; duration " + pet.laughSound.DurationMilliseconds
                            + " ms; returned to idle within " + (elapsed - lastLaughPlayingAt) + " ms of the last playing sample.");
                        pet.restAudioOption.SelectedIndex = 1; pet.Act("rest", "测试男声", 5, true); stage = 2;
                    }
                    else if (stage == 2)
                    {
                        if (pet.activeSound == pet.restMaleSound && pet.restMaleSound.IsPlaying) heardMale = true;
                        if (heardMale && pet.activeSound == null)
                        {
                            if (pet.animation != "rest") throw new InvalidOperationException("Male voice ending interrupted rest.");
                            pet.restAudioOption.SelectedIndex = 2; pet.Act("rest", "测试女声", 5, true); stage = 3;
                        }
                    }
                    else if (stage == 3)
                    {
                        if (pet.activeSound == pet.restFemaleSound && pet.restFemaleSound.IsPlaying) heardFemale = true;
                        if (heardFemale && pet.activeSound == null)
                        {
                            if (pet.animation != "rest") throw new InvalidOperationException("Female voice ending interrupted rest: state="
                                + pet.animation + "; clip=" + pet.clipTime + "; remaining=" + pet.actionRemaining + "; elapsed=" + elapsed + " ms.");
                            pet.jumpAudioOption.SelectedIndex = 1; pet.Hop(); stage = 4;
                        }
                    }
                    else if (stage == 4 && pet.activeSound == pet.jumpSound && pet.jumpSound.IsPlaying)
                    {
                        pet.jumpAudioOption.SelectedIndex = 0;
                        if (pet.activeSound != null || pet.jumpSound.IsPlaying) throw new InvalidOperationException("Sliding jump sound off did not stop it.");
                        pet.Act("listen", null, 2, true);
                        if (pet.activeSound != null) throw new InvalidOperationException("Disabled thinking sound played.");
                        pet.listenAudioOption.SelectedIndex = 1;
                        pet.Act("listen", null, 1);
                        if (pet.activeSound != null) throw new InvalidOperationException("Automatic thinking played sound.");
                        pet.Act("listen", null, 2, true); stage = 5;
                    }
                    else if (stage == 5)
                    {
                        if (pet.activeSound == pet.listenSound && pet.listenSound.IsPlaying) heardThinking = true;
                        if (heardThinking && pet.activeSound == null)
                        {
                            if (pet.animation != "listen") throw new InvalidOperationException("Thinking sound completion interrupted the pose.");
                            pet.Act("listen", null, 2, true); stage = 6;
                        }
                    }
                    else if (stage == 6 && pet.activeSound == pet.listenSound && pet.listenSound.IsPlaying)
                    {
                        pet.listenAudioOption.SelectedIndex = 0;
                        if (pet.activeSound != null || pet.listenSound.IsPlaying || pet.animation != "listen")
                            throw new InvalidOperationException("Thinking off must stop audio and keep the pose.");
                        pet.listenAudioOption.SelectedIndex = 1;
                        pet.Act("listen", null, 2, true); pet.Act("idle", null, 1);
                        if (pet.activeSound != null || pet.listenSound.IsPlaying)
                            throw new InvalidOperationException("Switching actions did not cancel thinking sound.");
                        pet.listenAudioOption.SelectedIndex = 0;
                        pet.laughAudioOption.SelectedIndex = 0; pet.restAudioOption.SelectedIndex = 0;
                        pet.Act("laugh", null, 2, true);
                        if (pet.activeSound != null) throw new InvalidOperationException("Laugh off option failed after toggling.");
                        pet.Hop();
                        if (pet.activeSound != null) throw new InvalidOperationException("Jump off option failed.");
                        passed = true; test.Stop(); pet.Close();
                    }
                };
                test.Start(); Application.Run(pet);
                if (!passed) throw new InvalidOperationException("Audio tests ended before completion.");
                Console.WriteLine("Audio options passed: defaults off; actual laugh completion; male/female sigh; jump; manual thinking on/off/replay, silent automatic thinking, ending keeps the pose; action switches stop sound.");
            }
        }

        public static void Benchmark(string directory, string reportPath)
        { Benchmark(directory, reportPath, "idle"); }

        public static void Benchmark(string directory, string reportPath, string state)
        {
            using (var pet = new Pet(directory))
            using (var finish = new Timer())
            {
                pet.benchmarkTimes = new List<double>();
                pet.benchmarkRenderCosts = new List<double>();
                pet.MuteTestSounds();
                if (state == "laugh") pet.laughAudioOption.SelectedIndex = 1;
                pet.Shown += (s, e) => pet.Act(state, state == "listen" ? "我在听呢～" : null, state == "laugh" ? 2 : 10, state == "laugh");
                finish.Interval = 8000; finish.Tick += (s, e) => { finish.Stop(); pet.Close(); };
                finish.Start(); Application.Run(pet);
                var samples = pet.benchmarkTimes.FindAll(t => t >= 1000);
                if (samples.Count < 2) throw new InvalidOperationException("No render samples.");
                double seconds = (samples[samples.Count - 1] - samples[0]) / 1000;
                double fps = (samples.Count - 1) / seconds;
                var intervals = new List<double>();
                for (int i = 1; i < samples.Count; i++) intervals.Add(samples[i] - samples[i - 1]);
                intervals.Sort();
                double p95 = intervals[(int)((intervals.Count - 1) * .95)];
                pet.benchmarkRenderCosts.Sort();
                double costP95 = pet.benchmarkRenderCosts[(int)((pet.benchmarkRenderCosts.Count - 1) * .95)];
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                string source = pet.clips[state].UsesCalmListeningPose
                    ? "Compact high-resolution chin-resting pose with procedural breathing and 13 cached eyelid poses"
                    : pet.clips[state].UsesHdIdlePose ? "High-resolution idle pose with procedural breathing and 13 cached eyelid poses"
                    : pet.clips[state].UsesHdRestPose ? "HD supine pose; one-shot settle/inhale/long-sigh sequence, drifting air marks, abdominal breathing and blinking"
                    : pet.clips[state].UsesSharedBodyLaugh ? "Connected UV surface with 2x antialiasing; coordinated spine/head/abdomen/knees; three laughter bursts; planted feet"
                    : "High-resolution source pose";
                string json = "{\n  \"animation\": \"" + state + "\",\n  \"target_fps\": 100,\n  \"measured_render_fps\": " + fps.ToString("F2", culture)
                    + ",\n  \"frame_interval_p95_ms\": " + p95.ToString("F2", culture)
                    + ",\n  \"max_frame_interval_ms\": " + intervals[intervals.Count - 1].ToString("F2", culture)
                    + ",\n  \"render_time_p95_ms\": " + costP95.ToString("F2", culture)
                    + ",\n  \"render_time_max_ms\": " + pet.benchmarkRenderCosts[pet.benchmarkRenderCosts.Count - 1].ToString("F2", culture)
                    + ",\n  \"paused_ticks\": " + pet.benchmarkPausedTicks
                    + ",\n  \"sample_frames\": " + samples.Count + ",\n  \"sample_seconds\": " + seconds.ToString("F2", culture)
                    + ",\n  \"source_width\": " + pet.clips[state].SourceWidth
                    + ",\n  \"source_height\": " + pet.clips[state].SourceHeight
                    + ",\n  \"audio_playback_tested\": " + (state == "laugh" ? "true" : "false")
                    + ",\n  \"audio_duration_ms\": " + (state == "laugh" ? pet.laughSound.DurationMilliseconds : 0)
                    + ",\n  \"source\": \"" + source + "\"\n}\n";
                File.WriteAllText(reportPath, json);
                Console.WriteLine(json);
                if (fps < 95) throw new InvalidOperationException("Render rate is below 95 FPS; inspect performance before shipping.");
            }
        }
    }
}
