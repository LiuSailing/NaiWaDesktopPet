using System;
using System.Drawing;

namespace NaiwaDesktopPet
{
    public struct FlightPose
    {
        public double WingsOpacity, Spread, Flap, Lift, Tilt, Crouch, Travel;
    }

    // The entire performance has one clock. Values are continuous across
    // emergence, unfolding, take-off, cruise, landing, folding and disappearance.
    public static class FlightMotion
    {
        public const int HoldMilliseconds = 850, Duration = 7600;
        public const int MinimumDuration = 4800, MaximumDuration = 15100;
        const double MaximumLift = 68;

        static double Smooth(double a, double b, double time)
        {
            double t = Math.Max(0, Math.Min(1, (time - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        // Integral of smoothstep, including its constant tail, in milliseconds.
        static double RampIntegral(double time, double start, double length)
        {
            double u = Math.Max(0, (time - start) / length);
            return length * (u >= 1 ? u - .5 : u * u * u - .5 * u * u * u * u);
        }

        static double TravelAt(double time, int shift)
        {
            if (time <= 1600) return 0;
            if (time >= 6300 + shift) return 1;
            // Only the cruise length changes. Take-off acceleration and the
            // planned .9-second brake retain their original timing/shape.
            double travelled = RampIntegral(time, 1600, 1000) - RampIntegral(time, 5400 + shift, 900);
            return Math.Max(0, Math.Min(1, travelled / (3750 + shift)));
        }

        public static FlightPose At(double milliseconds)
        { return At(milliseconds, Duration); }

        public static FlightPose At(double milliseconds, int duration)
        {
            if (Double.IsNaN(milliseconds) || Double.IsInfinity(milliseconds))
                throw new ArgumentOutOfRangeException("milliseconds");
            if (duration < MinimumDuration || duration > MaximumDuration)
                throw new ArgumentOutOfRangeException("duration");
            int shift = duration - Duration;
            double time = Math.Max(0, Math.Min(duration, milliseconds));
            double airborne = Smooth(1600, 2600, time) * (1 - Smooth(5400 + shift, 6300 + shift, time));
            // For a zero-length cruise, do not let a shifted recovery envelope
            // start before take-off has reached its unchanged 2600 ms endpoint.
            double hover = Smooth(2500, 3000, time) * (1 - Smooth(Math.Max(2600, 5000 + shift), 5500 + shift, time));
            double wingBeat = Smooth(900, 1800, time) * (1 - Smooth(5600 + shift, 6700 + shift, time));
            // Phase uses real elapsed time: a longer trip adds wingbeats rather
            // than stretching a recorded flap into slow motion.
            double cycles = 2.2 * (RampIntegral(time, 900, 1100) - RampIntegral(time, 5600 + shift, 1100)) / 1000;
            return new FlightPose {
                WingsOpacity = Smooth(0, 600, time) * (1 - Smooth(7000 + shift, duration, time)),
                Spread = (.15 + .85 * Smooth(600, 1600, time)) * (1 - .88 * Smooth(6300 + shift, 7000 + shift, time)),
                Flap = Math.Sin(cycles * Math.PI * 2) * wingBeat,
                Lift = 65 * airborne + 3 * Math.Sin(time * Math.PI * 2 / 1600) * hover,
                // Tilt is degrees for leftward travel. A renderer may reverse
                // its sign when the available screen space sends the pet right.
                Tilt = -3.2 * Smooth(1550, 2200, time) * (1 - Smooth(Math.Max(2600, 5200 + shift), 6200 + shift, time))
                    + .55 * Math.Sin(time * Math.PI * 2 / 1900) * hover,
                // Crouch is a fractional squash, not a pixel displacement.
                Crouch = .065 * Smooth(800, 1250, time) * (1 - Smooth(1400, 1800, time))
                    + .035 * Smooth(6000 + shift, 6250 + shift, time) * (1 - Smooth(6300 + shift, 6600 + shift, time)),
                Travel = TravelAt(time, shift)
            };
        }

        static Point Clamp(Point location, Rectangle body, Rectangle screen)
        {
            int left = screen.Left - body.Left, top = screen.Top - body.Top;
            int right = Math.Max(left, screen.Right - body.Right);
            int bottom = Math.Max(top, screen.Bottom - body.Bottom);
            return new Point(Math.Max(left, Math.Min(right, location.X)), Math.Max(top, Math.Min(bottom, location.Y)));
        }

        static void CheckBounds(Rectangle visibleBody, Rectangle screen)
        {
            if (visibleBody.Width <= 0 || visibleBody.Height <= 0 || screen.Width <= 0 || screen.Height <= 0)
                throw new ArgumentException("Flight requires a visible body and a nonempty screen.");
        }

        public static Point Destination(Point origin, Rectangle visibleBody, Rectangle screen)
        { return Destination(origin, visibleBody, screen, false); }

        public static Point Destination(Point origin, Rectangle visibleBody, Rectangle screen, bool facingRight)
        {
            CheckBounds(visibleBody, screen);
            origin = Clamp(origin, visibleBody, screen);
            int left = screen.Left - visibleBody.Left;
            int right = Math.Max(left, screen.Right - visibleBody.Right);
            // An interrupted trip retains its heading. Reverse only when the
            // pet has actually reached the edge it was heading toward.
            bool goRight = facingRight;
            if (goRight && right - origin.X <= 1) goRight = false;
            else if (!goRight && origin.X - left <= 1) goRight = true;
            return new Point(goRight ? right : left, origin.Y);
        }

        public static int DurationFor(Point start, Point end, Rectangle screen)
        {
            if (screen.Width <= 0 || screen.Height <= 0)
                throw new ArgumentException("Flight requires a nonempty screen.");
            double referenceSpeed = Math.Max(1, screen.Width / 3.0) / 3750;
            double effectiveTravel = Math.Abs(end.X - (double)start.X) / referenceSpeed;
            return (int)Math.Round(Math.Max(MinimumDuration, Math.Min(MaximumDuration, Duration - 3750 + effectiveTravel)));
        }

        public static Point LocationAt(Point start, Point end, Rectangle visibleBody, Rectangle screen, double milliseconds, double scale)
        { return LocationAt(start, end, visibleBody, screen, milliseconds, scale, Duration); }

        public static Point LocationAt(Point start, Point end, Rectangle visibleBody, Rectangle screen, double milliseconds, double scale, int duration)
        {
            CheckBounds(visibleBody, screen);
            if (Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0)
                throw new ArgumentOutOfRangeException("scale");
            FlightPose pose = At(milliseconds, duration);
            start = Clamp(start, visibleBody, screen); end = Clamp(end, visibleBody, screen);
            double headroom = Math.Max(0, Math.Min(start.Y, end.Y) - (screen.Top - visibleBody.Top));
            // Scale the entire ascent down near the top edge. This preserves
            // smooth take-off/landing instead of hitting a hard vertical clamp.
            double liftScale = Math.Min(1, headroom / (MaximumLift * scale));
            var location = new Point((int)Math.Round(start.X + (end.X - start.X) * pose.Travel),
                (int)Math.Round(start.Y + (end.Y - start.Y) * pose.Travel - pose.Lift * scale * liftScale));
            return Clamp(location, visibleBody, screen);
        }

        static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("Flight: " + reason); }

        static void CheckFinite(FlightPose pose)
        {
            foreach (double value in new[] { pose.WingsOpacity, pose.Spread, pose.Flap, pose.Lift, pose.Tilt, pose.Crouch, pose.Travel })
                Require(!Double.IsNaN(value) && !Double.IsInfinity(value), "non-finite pose");
        }

        static void ValidatePerformance(int duration)
        {
            int shift = duration - Duration, brake = 5400 + shift, landing = 6300 + shift, fold = 7000 + shift;
            FlightPose previous = At(0, duration);
            for (int time = 0; time <= duration; time += 10)
            {
                FlightPose pose = At(time, duration); CheckFinite(pose);
                Require(pose.WingsOpacity >= 0 && pose.WingsOpacity <= 1 && pose.Spread >= 0 && pose.Spread <= 1,
                    "wing envelope outside [0,1]");
                Require(Math.Abs(pose.Flap) <= 1 && pose.Lift >= 0 && pose.Lift <= MaximumLift
                    && Math.Abs(pose.Tilt) <= 5 && pose.Crouch >= 0 && pose.Crouch < .09, "pose range exceeded");
                Require(pose.Travel >= previous.Travel && pose.Travel <= 1, "flight reversed or overshot");
                Require(Math.Abs(pose.WingsOpacity - previous.WingsOpacity) < .03
                    && Math.Abs(pose.Spread - previous.Spread) < .03
                    && Math.Abs(pose.Flap - previous.Flap) < .16
                    && Math.Abs(pose.Lift - previous.Lift) < 1.8
                    && Math.Abs(pose.Tilt - previous.Tilt) < .15
                    && Math.Abs(pose.Crouch - previous.Crouch) < .003
                    && Math.Abs(pose.Travel - previous.Travel) <= 10.0 / (3750 + shift) + .00000001,
                    "discontinuous phase boundary");
                if (time >= 1600 && time <= landing)
                    Require(pose.Spread >= .999999 && pose.WingsOpacity == 1, "wings folded before landing");
                previous = pose;
            }
            FlightPose first = At(0, duration), last = At(duration, duration);
            Require(first.WingsOpacity == 0 && first.Travel == 0 && first.Lift == 0 && first.Crouch == 0,
                "performance does not start at rest");
            Require(last.WingsOpacity == 0 && last.Travel == 1 && last.Lift == 0 && last.Tilt == 0 && last.Crouch == 0,
                "performance does not finish at rest");
            Require(At(1599, duration).Travel == 0 && At(landing, duration).Travel == 1 && At(landing, duration).Lift == 0,
                "travel started before unfold or continued after landing");
            Require(At(landing - 1, duration).Travel < 1 && At(landing, duration).Spread == 1
                && At(landing + 100, duration).Spread < 1 && At(fold, duration).WingsOpacity == 1
                && At(fold + 100, duration).WingsOpacity < 1, "landing/fold/fade order changed");
            double firstBrake = At(brake + 100, duration).Travel - At(brake, duration).Travel;
            double finalBrake = At(landing, duration).Travel - At(landing - 100, duration).Travel;
            Require(firstBrake > 0 && finalBrake < firstBrake * .1, "landing did not decelerate in advance");
            double previousVelocity = Double.MaxValue;
            for (int time = brake; time < landing; time += 10)
            {
                double velocity = At(time + 10, duration).Travel - At(time, duration).Travel;
                Require(velocity <= previousVelocity + .00000001 && velocity >= 0, "braking accelerated or reversed");
                previousVelocity = velocity;
            }
        }

        static int ValidatePath(Point start, Rectangle body, Rectangle screen, double scale)
        {
            Point end = Destination(start, body, screen), previousLocation = start;
            int duration = DurationFor(start, end, screen), shift = duration - Duration, landing = 6300 + shift;
            Require(duration >= MinimumDuration && duration <= MaximumDuration, "duration outside allowed range");
            Require(end.Y == start.Y, "landing point changed the starting height");
            bool leftEdge = start.X + body.Left - screen.Left <= 1;
            Require(leftEdge ? end.X + body.Right == screen.Right : end.X + body.Left == screen.Left,
                "destination is not the planned screen edge");
            double distance = Math.Abs(end.X - (double)start.X);
            int direction = Math.Sign(end.X - start.X);
            int xBudget = (int)Math.Ceiling(distance * 10 / (3750 + shift)) + 1;
            int yBudget = (int)Math.Ceiling((65 * 1.5 / 900 + 3 * Math.PI * 2 / 1600) * 10 * scale) + 1;
            for (int time = 0; time <= duration; time += 10)
            {
                FlightPose pose = At(time, duration);
                Point current = LocationAt(start, end, body, screen, time, scale, duration);
                Require(current.X + body.Left >= screen.Left && current.X + body.Right <= screen.Right
                    && current.Y + body.Top >= screen.Top && current.Y + body.Bottom <= screen.Bottom,
                    "body left its current screen");
                Require(Math.Abs(current.X - previousLocation.X) <= xBudget && Math.Abs(current.Y - previousLocation.Y) <= yBudget,
                    "trajectory exceeded its speed budget");
                Require(direction * (current.X - previousLocation.X) >= 0, "trajectory changed direction");
                Require(current.X == (int)Math.Round(start.X + (end.X - start.X) * pose.Travel),
                    "screen clamp stopped horizontal travel before its planned landing");
                if (current.X == end.X && time < landing)
                    Require(distance * (1 - pose.Travel) <= .500001,
                        "flight reached the wall before its braking phase ended");
                if (time >= landing) Require(current == end, "body moved after landing");
                previousLocation = current;
            }
            Require(LocationAt(start, end, body, screen, 0, scale, duration) == start
                && LocationAt(start, end, body, screen, duration, scale, duration) == end,
                "trajectory endpoint mismatch");
            Point returnPoint = Destination(end, body, screen);
            Require(returnPoint.X != end.X && (leftEdge ? returnPoint.X + body.Left == screen.Left : returnPoint.X + body.Right == screen.Right),
                "a second departure did not select the opposite edge");
            return duration;
        }

        public static string Validate()
        {
            foreach (int duration in new[] { MinimumDuration, MinimumDuration + 1, Duration, MaximumDuration })
                ValidatePerformance(duration);
            var verified = new bool[MaximumDuration - MinimumDuration + 1];
            var screens = new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(-2560, -240, 2560, 1440),
                new Rectangle(1920, 0, 3840, 2160), new Rectangle(0, 0, 800, 600) };
            foreach (Rectangle screen in screens)
            foreach (double scale in new[] { 1.0, 1.25, 2.0 })
            {
                var body = new Rectangle((int)(90 * scale), (int)(80 * scale), (int)(140 * scale), (int)(235 * scale));
                int left = screen.Left - body.Left, right = screen.Right - body.Right;
                int middleY = screen.Top + screen.Height / 2 - body.Top;
                foreach (bool headingRight in new[] { false, true })
                {
                    Point start = Clamp(new Point((left + right) / 2, middleY), body, screen);
                    Point end = Destination(start, body, screen, headingRight);
                    Require(end.X == (headingRight ? right : left), "departure ignored the current heading");
                    int duration = DurationFor(start, end, screen);
                    foreach (int sample in new[] { 2800, 3200, duration - 2300 })
                    {
                        Point interrupted = LocationAt(start, end, body, screen, sample, scale, duration);
                        Point resumed = Destination(interrupted, body, screen, headingRight);
                        Require(resumed.X == end.X && resumed.Y == interrupted.Y,
                            "an interrupted flight resumed toward the wrong edge");
                    }
                    Point returned = Destination(end, body, screen, headingRight);
                    Require(returned.X == (headingRight ? left : right), "a completed flight did not reverse at its destination edge");
                }
                foreach (Point candidate in new[] { new Point(left, screen.Top - body.Top), new Point(left + 1, middleY),
                    new Point(left + 2, middleY), new Point(left + 20, middleY), new Point((left + right) / 2, middleY),
                    new Point(right, screen.Bottom - body.Bottom) })
                {
                    Point start = Clamp(candidate, body, screen);
                    for (int trip = 0; trip < 2; trip++)
                    {
                        int duration = ValidatePath(start, body, screen, scale);
                        if (!verified[duration - MinimumDuration])
                        { ValidatePerformance(duration); verified[duration - MinimumDuration] = true; }
                        start = Destination(start, body, screen);
                    }
                }
            }
            return "Flight passed: planned edge-to-edge destinations; distance-adaptive cruise (4800-15100 ms); unchanged take-off and flap frequency; advance braking, landing before wing fold/fade; monotone bounded paths on three displays, negative coordinates and three DPI scales; interruption preserves heading, reversal only at the destination edge.";
        }
    }
}
