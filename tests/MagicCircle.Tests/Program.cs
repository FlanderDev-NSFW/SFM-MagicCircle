using System;
using System.Numerics;
using MagicCircle.Logic;

namespace MagicCircle.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;

        private static int Main()
        {
            PatternTests();
            PoseTests();
            StageTests();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void PatternTests()
        {
            const int n = 128;
            var c = CirclePattern.Circle(n, 7);
            Check(c.Length == n * n, "pattern: one byte per pixel");
            bool clear = true;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (CirclePattern.Radius(x, y, n) < CirclePattern.ClearRadius - 0.02f && c[y * n + x] != 0) clear = false;
            Check(clear, "pattern: the middle is empty (the limb's hole)");
            // The outer ring at r 0.95: the pixel on the +X axis nearest that radius is solid.
            int ringX = n / 2 + (int)(0.95f * n / 2f);
            Check(c[(n / 2) * n + ringX] > 200, "pattern: the outer ring is drawn");
            Check(c[0] == 0 && c[n * n - 1] == 0, "pattern: the corners outside the circle are empty");
            int lit = 0;
            foreach (byte b in c) if (b > 128) lit++;
            Check(lit > n * n / 20 && lit < n * n / 2, "pattern: a fair part is drawn (" + lit + " of " + n * n + ")");
            var again = CirclePattern.Circle(n, 7);
            bool same = true;
            for (int i = 0; i < c.Length; i++) if (c[i] != again[i]) same = false;
            Check(same, "pattern: the same seed draws the same runes");
            var h = CirclePattern.Halo(64);
            Check(h[32 * 64 + 32] == 0 && h[32 * 64 + 32 + 25] > 60, "halo: empty middle, glow at the runes");
        }

        private static void PoseTests()
        {
            var b = new BodyDims { Thigh = 0.4f, Shin = 0.38f, UpperArm = 0.25f, Forearm = 0.22f, HipHalf = 0.08f, HipDrop = 0.05f, AnkleHeight = 0.07f };
            var s = new FloatSettings { Height = 0.15f, ArmAngle = 25f, LegAngle = 8f, BobAmplitude = 0f, BobPeriod = 3f };
            var hips = FloatPose.Hips(b, s, 0f);
            float leg = 0.78f * (float)Math.Cos(8 * Math.PI / 180);
            Check(Near(hips.Y, 0.15f + 0.07f + leg + 0.05f), "float: hips high enough for straight legs to end 15 cm up");
            var thigh = new Vector3(-0.08f, hips.Y - 0.05f, 0f);
            var (knee, foot) = FloatPose.Leg(b, s, 0, thigh);
            Check(Near(foot.Y, 0.15f + 0.07f, 1e-3f), "float: soles at the floating height (ankle above them)");
            Check(foot.X < thigh.X && knee.X < thigh.X, "float: the left leg swings out to her left");
            Check(Near(Vector3.Distance(thigh, knee), 0.4f) && Near(Vector3.Distance(knee, foot), 0.38f), "float: bones keep their lengths");
            var r = FloatPose.ArmDir(s, 1);
            Check(r.X > 0f && r.Y > 0.85f && Near(r.Length(), 1f), "float: the right arm points up and out to her right");
            float angle = (float)(Math.Acos(Vector3.Dot(Vector3.Normalize(new Vector3(r.X, r.Y, 0f)), Vector3.UnitY)) * 180 / Math.PI);
            Check(Near(angle, 25f, 0.5f), "float: arm at the set angle from straight up (" + angle.ToString("0.0") + ")");
            var bob = s;
            bob.BobAmplitude = 0.01f;
            Check(Near(FloatPose.Hips(b, bob, 0.75f).Y - hips.Y, 0.01f, 1e-4f), "float: the bob at a quarter period is its amplitude");
            Check(FloatPose.ToeDir().Y < -0.8f, "float: toes point down");
        }

        private static void StageTests()
        {
            Check(Stages.Clamp(-1) == Stages.Visual && Stages.Clamp(9) == Stages.Last, "stages: clamped to the range");
            Check(!Stages.Holds(Stages.Visual) && Stages.Holds(Stages.Float), "stages: only the pose stage holds her");
            Check(Stages.Other(Placement.WristsAndAnkles) == Placement.ElbowsAndKnees && Stages.Other(Placement.ElbowsAndKnees) == Placement.WristsAndAnkles,
                  "placement: the tap switches between the two");
            Check(Stages.Smooth(0f) == 0f && Stages.Smooth(1f) == 1f && Near(Stages.Smooth(0.5f), 0.5f) && Stages.Smooth(2f) == 1f, "smooth ramp");
            var p = CircleSpot.Before(new Vector3(0, 0, 0), new Vector3(0, -0.3f, 0), 0.02f);
            Check(Near(p.Y, -0.28f), "circle spot: 2 cm before the joint towards its parent");
            Check(Near(CircleSpot.Before(Vector3.Zero, new Vector3(0, 0.1f, 0), 1f).Y, 0.05f), "circle spot: never past the segment's middle");
            Check(Near(CircleSpot.OuterRadius(0.03f, 1.4f) * CirclePattern.ClearRadius, 0.042f), "circle size: the hole 1.4x the limb");
            var axis = new Vector3(0, -1, 0);
            var pts = new[] { new Vector3(0, 0.05f, 0), new Vector3(0.01f, -0.02f, 0), new Vector3(0.2f, -0.06f, 0), new Vector3(0, -0.3f, 0), new Vector3(0.005f, -0.035f, 0.01f) };
            Check(Near(Stump.Reach(pts, Vector3.Zero, axis, 0.04f, 0.1f), 0.035f), "stump: farthest skin near the axis; far off the axis or too far along ignored");
            Check(Stump.Reach(new[] { new Vector3(0, 0.1f, 0) }, Vector3.Zero, axis, 0.04f, 0.1f) == 0f, "stump: nothing past the point -> 0");
            var segs = new[] { (new Vector3(-0.1f, 0.5f, 0), new Vector3(-0.1f, 0.1f, 0)), (new Vector3(0.1f, 0.5f, 0), new Vector3(0.1f, 0.1f, 0)) };
            Check(Stump.Owner(new Vector3(0.09f, 0.3f, 0.02f), segs) == 1 && Stump.Owner(new Vector3(-0.12f, 0.52f, 0), segs) == 0,
                  "stump: a vertex belongs to the limb whose full-size segment is nearest");
            var hold = new ReachHold();
            hold.Add(0f, 0.025f);
            Check(Near(hold.Shown, 0.025f), "reach hold: the first measurement at once");
            hold.Add(1f, 0.066f);
            hold.Step(1.05f, 0.05f);
            Check(hold.Shown > 0.025f && hold.Shown < 0.066f, "reach hold: rises towards a larger one (fast, not a jump)");
            for (int k = 0; k < 20; k++) hold.Step(1.1f + 0.05f * k, 0.05f);
            Check(Near(hold.Shown, 0.066f), "reach hold: reaches it within a fraction of a second");
            hold.Add(2f, 0.025f);
            hold.Step(5f, 1f);
            Check(Near(hold.Shown, 0.066f), "reach hold: a smaller step later does not pull it back (held 20 s)");
            hold.Step(22f, 1f);
            Check(hold.Shown < 0.066f && hold.Shown > 0.05f, "reach hold: after the hold it sinks slowly");
            var portal = CirclePattern.Portal(64);
            Check(portal[32 * 64 + 32] < portal[32 * 64 + 62], "portal surface: dark middle, brighter rim");
        }
    }
}
