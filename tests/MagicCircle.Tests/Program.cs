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
            var straight = new FloatSettings { Height = 0.15f, ArmAngle = 25f, LegAngle = 8f, BobPeriod = 3f };
            var hips = FloatPose.Hips(b, straight, 0f);
            float leg = 0.78f * (float)Math.Cos(8 * Math.PI / 180);
            Check(Near(hips.Y, 0.15f + 0.07f + leg + 0.05f), "float: straight legs - hips high enough for them to end 15 cm up");

            var s = new FloatSettings { Height = 0.15f, ArmAngle = 32f, ElbowBend = 30f, LegAngle = 14f, KneeLift = 22f, KneeBend = 45f, BobPeriod = 3f };
            hips = FloatPose.Hips(b, s, 0f);
            var thigh = new Vector3(-0.08f, hips.Y - 0.05f, 0f);
            var (knee, foot) = FloatPose.LegJoints(b, s, 0, thigh);
            Check(Near(foot.Y, 0.15f + 0.07f, 1e-3f), "float: bent legs - ankles at the floating height above the soles");
            Check(Near(Vector3.Distance(thigh, knee), 0.4f) && Near(Vector3.Distance(knee, foot), 0.38f), "float: bones keep their lengths");
            Check(knee.Z > 0.1f && foot.Z < knee.Z, "float: knees drawn forward, shins bent back");
            Check(foot.X < thigh.X - 0.1f, "float: the left leg spreads out to her left");
            Check(hips.Y < FloatPose.Hips(b, straight, 0f).Y, "float: bent legs need the hips lower (less stretched)");
            var up = FloatPose.UpperArmDir(s, 1);
            var fore = FloatPose.ForearmDir(s, 1);
            Check(up.X > 0f && up.Y > 0.8f && Near(up.Length(), 1f), "float: the right upper arm points up and out");
            Check(fore.X < up.X && fore.Y > 0.8f, "float: the forearm bends in towards her head");

            var tilted = s;
            tilted.Tilt = 12f;
            var spine = FloatPose.Up(tilted);
            Check(spine.Z < 0f && Near((float)(Math.Acos(spine.Y) * 180 / Math.PI), 12f, 0.1f), "float: tilt leans her back by its angle");
            Check(FloatPose.ThighDir(tilted, 0).Z > FloatPose.ThighDir(s, 0).Z, "float: with the tilt the legs swing forward");
            Check(Near(FloatPose.Tilted(tilted, Vector3.UnitX).X, 1f), "float: the tilt turns about her right axis");
            var bob = s;
            bob.BobAmplitude = 0.01f;
            Check(Near(FloatPose.Hips(b, bob, 0.75f).Y - hips.Y, 0.01f, 1e-4f), "float: the bob at a quarter period is its amplitude");
            Check(FloatPose.ToeDir(s).Y < -0.8f, "float: toes point down");
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
        }
    }
}
