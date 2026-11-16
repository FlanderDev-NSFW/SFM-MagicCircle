using System;
using System.Numerics;

namespace MagicCircle.Logic
{
    /// <summary>Her bone lengths (metres), measured from the bones.</summary>
    public struct BodyDims
    {
        public float Thigh, Shin, UpperArm, Forearm, Foot;
        /// <summary>Half the distance between the thigh joints; the hips bone's height above their middle.</summary>
        public float HipHalf, HipDrop;
        /// <summary>The ankle joint's height above the sole, standing.</summary>
        public float AnkleHeight;
    }

    /// <summary>The floating pose's settings (angles in degrees, lengths in metres).</summary>
    public struct FloatSettings
    {
        /// <summary>How far her soles float above the ground.</summary>
        public float Height;
        /// <summary>Each arm's angle out from straight up (a narrow X: 20 - 35).</summary>
        public float ArmAngle;
        /// <summary>Each leg's angle out from straight down.</summary>
        public float LegAngle;
        /// <summary>The gentle up and down while floating: amplitude (m) and period (s).</summary>
        public float BobAmplitude, BobPeriod;
    }

    /// <summary>
    /// The floating narrow X: body upright above the spot she stood on, arms raised and spread a little, legs straight and
    /// slightly apart, toes pointing down. Points in her frame: X to her right, Y up, Z forward, origin on the ground where she
    /// stood. Index 0 = her left, 1 = her right. Pure maths.
    /// </summary>
    public static class FloatPose
    {
        /// <summary>The hips bone's position: high enough for straight legs at <see cref="FloatSettings.LegAngle"/> to end
        /// <see cref="FloatSettings.Height"/> above the ground, plus the bob at <paramref name="time"/> seconds.</summary>
        public static Vector3 Hips(BodyDims b, FloatSettings s, float time)
        {
            float leg = (b.Thigh + b.Shin) * (float)Math.Cos(Rad(s.LegAngle));
            float bob = s.BobPeriod > 0f ? s.BobAmplitude * (float)Math.Sin(2 * Math.PI * time / s.BobPeriod) : 0f;
            return new Vector3(0f, s.Height + b.AnkleHeight + leg + b.HipDrop + bob, 0f);
        }

        /// <summary>Unit direction of a leg (thigh joint to foot): down, swung out to its side.</summary>
        public static Vector3 LegDir(FloatSettings s, int side)
        {
            float a = Rad(s.LegAngle), out_ = side == 0 ? -1f : 1f;
            return new Vector3(out_ * (float)Math.Sin(a), -(float)Math.Cos(a), 0f);
        }

        /// <summary>Unit direction of an arm (shoulder to hand): up, swung out to its side, a touch forward.</summary>
        public static Vector3 ArmDir(FloatSettings s, int side)
        {
            float a = Rad(s.ArmAngle), out_ = side == 0 ? -1f : 1f;
            return Vector3.Normalize(new Vector3(out_ * (float)Math.Sin(a), (float)Math.Cos(a), 0.08f));
        }

        /// <summary>Where her toes point: mostly down, a little forward (a dancer's pointed foot).</summary>
        public static Vector3 ToeDir() => Vector3.Normalize(new Vector3(0f, -0.85f, 0.5f));

        /// <summary>Knee and foot joints from the thigh joint.</summary>
        public static (Vector3 Knee, Vector3 Foot) Leg(BodyDims b, FloatSettings s, int side, Vector3 thighJoint)
        {
            var d = LegDir(s, side);
            return (thighJoint + d * b.Thigh, thighJoint + d * (b.Thigh + b.Shin));
        }

        /// <summary>Elbow and hand joints from the shoulder joint.</summary>
        public static (Vector3 Elbow, Vector3 Hand) Arm(BodyDims b, FloatSettings s, int side, Vector3 shoulder)
        {
            var d = ArmDir(s, side);
            return (shoulder + d * b.UpperArm, shoulder + d * (b.UpperArm + b.Forearm));
        }

        private static float Rad(float deg) => deg * (float)Math.PI / 180f;
    }

    /// <summary>Where a circle sits on a limb segment and how big it is.</summary>
    public static class CircleSpot
    {
        /// <summary>A point <paramref name="before"/> metres back from <paramref name="joint"/> towards <paramref name="parent"/>
        /// (never past the middle of the segment).</summary>
        public static Vector3 Before(Vector3 parent, Vector3 joint, float before)
        {
            var seg = joint - parent;
            float len = seg.Length();
            if (len < 1e-5f) return joint;
            return joint - seg / len * Math.Min(Math.Max(before, 0f), len / 2f);
        }

        /// <summary>The circle's outer radius for a limb of <paramref name="limbRadius"/>: the hole (inside
        /// <see cref="CirclePattern.ClearRadius"/>) <paramref name="room"/> times as wide as the limb.</summary>
        public static float OuterRadius(float limbRadius, float room) => limbRadius * room / CirclePattern.ClearRadius;
    }

    /// <summary>
    /// How far the shrunk limb's stump reaches past its collapse point: the skin around a joint is partly bound to the upper bone,
    /// so it does not all collapse into the point. The circle (and the portal surface filling it) goes just beyond the farthest
    /// skin, so no part of the stump shows past it.
    /// </summary>
    public static class Stump
    {
        /// <summary>The largest distance along <paramref name="axis"/> (unit, from the upper bone towards the joint) of the points
        /// within <paramref name="radius"/> of the axis line through <paramref name="collapse"/> and at most
        /// <paramref name="maxReach"/> past it (farther points belong to something else: hair, the other limb); 0 if none is past.</summary>
        /// <summary>Which limb a vertex belongs to: the segment (lower bone to its child, with the bones at full size) nearest
        /// to its full-size position. A radius test alone mixes limbs up: walking, the knees pass a few cm from each other.</summary>
        public static int Owner(Vector3 grown, System.Collections.Generic.IReadOnlyList<(Vector3 From, Vector3 To)> segments)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < segments.Count; i++)
            {
                float d = SegmentDistanceSquared(grown, segments[i].From, segments[i].To);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static float SegmentDistanceSquared(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f);
            return (p - (a + t * ab)).LengthSquared();
        }

        public static float Reach(System.Collections.Generic.IReadOnlyList<Vector3> points, Vector3 collapse, Vector3 axis, float radius, float maxReach)
        {
            float best = 0f, r2 = radius * radius;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 d = points[i] - collapse;
                float along = Vector3.Dot(d, axis);
                if (along <= best || along > maxReach) continue;
                if ((d - along * axis).LengthSquared() > r2) continue;
                best = along;
            }
            return best;
        }
    }

    /// <summary>
    /// Steadies a measured stump reach so the circle does not follow every step: the target is the largest measurement of the last
    /// <see cref="HoldSeconds"/>; the shown value rises to it fast (the stump must stay hidden) and sinks slowly. Walking moves the
    /// knee's skin 2.5 - 6.6 cm past the joint with each step [observed 2026-11-16]: the circle settles once, then stays.
    /// </summary>
    public sealed class ReachHold
    {
        public float HoldSeconds = 20f, RiseSpeed = 0.25f, SinkSpeed = 0.01f; // s, m/s, m/s

        private readonly System.Collections.Generic.List<(float Time, float Value)> _seen = new System.Collections.Generic.List<(float, float)>();
        private bool _started;

        public float Shown { get; private set; }

        public void Add(float time, float value)
        {
            _seen.Add((time, value));
            if (!_started) { Shown = value; _started = true; }
        }

        public float Target(float time)
        {
            _seen.RemoveAll(s => time - s.Time > HoldSeconds);
            float best = 0f;
            foreach (var s in _seen) best = Math.Max(best, s.Value);
            return best;
        }

        /// <summary>Advance the shown value by <paramref name="dt"/> seconds towards the target at <paramref name="time"/>.</summary>
        public float Step(float time, float dt)
        {
            if (!_started) return 0f;
            float target = Target(time);
            Shown = target > Shown ? Math.Min(target, Shown + RiseSpeed * dt) : Math.Max(target, Shown - SinkSpeed * dt);
            return Shown;
        }

        public void Reset() { _seen.Clear(); _started = false; Shown = 0f; }
    }
}
