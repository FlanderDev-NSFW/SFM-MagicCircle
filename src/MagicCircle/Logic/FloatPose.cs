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
        /// <summary>How far her soles float above the ground (before the tilt).</summary>
        public float Height;
        /// <summary>Each upper arm's angle out from straight up (a narrow X: 20 - 40).</summary>
        public float ArmAngle;
        /// <summary>How far the forearms bend in from the upper arms (0 = straight arms).</summary>
        public float ElbowBend;
        /// <summary>Each leg's angle out to its side from straight down.</summary>
        public float LegAngle;
        /// <summary>The thighs drawn forward from straight down, and how far the shins bend back from the thighs.</summary>
        public float KneeLift, KneeBend;
        /// <summary>Her whole body leaned back about the hips (0 = upright).</summary>
        public float Tilt;
        /// <summary>The gentle up and down while floating: amplitude (m) and period (s).</summary>
        public float BobAmplitude, BobPeriod;
    }

    /// <summary>
    /// The floating narrow X: the body above the spot she stood on, leaned back a little; arms raised and spread with soft
    /// elbows; legs apart with the knees drawn up and the shins bent back, toes pointing down. Directions are in her frame:
    /// X to her right, Y up, Z forward (origin on the ground where she stood); the tilt is included. Index 0 = her left,
    /// 1 = her right. Pure maths.
    /// </summary>
    public static class FloatPose
    {
        /// <summary>The hips bone's position: high enough for the (untilted) legs to end <see cref="FloatSettings.Height"/>
        /// above the ground, plus the bob at <paramref name="time"/> seconds. The body leans back about this point.</summary>
        public static Vector3 Hips(BodyDims b, FloatSettings s, float time)
        {
            var thigh = Leg(s, 0, s.KneeLift);
            var shin = Leg(s, 0, s.KneeLift - s.KneeBend);
            float drop = -(thigh.Y * b.Thigh + shin.Y * b.Shin);
            float bob = s.BobPeriod > 0f ? s.BobAmplitude * (float)Math.Sin(2 * Math.PI * time / s.BobPeriod) : 0f;
            return new Vector3(0f, s.Height + b.AnkleHeight + drop + b.HipDrop + bob, 0f);
        }

        /// <summary>Her spine's direction: up, leaned back by the tilt.</summary>
        public static Vector3 Up(FloatSettings s) => Tilted(s, Vector3.UnitY);

        /// <summary>Thigh joint to knee: down, out to its side, drawn forward.</summary>
        public static Vector3 ThighDir(FloatSettings s, int side) => Tilted(s, Leg(s, side, s.KneeLift));

        /// <summary>Knee to ankle: the thigh's direction bent back by the knee bend.</summary>
        public static Vector3 ShinDir(FloatSettings s, int side) => Tilted(s, Leg(s, side, s.KneeLift - s.KneeBend));

        /// <summary>Shoulder to elbow: up, out to its side, a touch forward.</summary>
        public static Vector3 UpperArmDir(FloatSettings s, int side) => Tilted(s, Arm(side, s.ArmAngle, 0.08f));

        /// <summary>Elbow to wrist: bent in towards the head by the elbow bend, a little more forward.</summary>
        public static Vector3 ForearmDir(FloatSettings s, int side) => Tilted(s, Arm(side, s.ArmAngle - s.ElbowBend, 0.2f));

        /// <summary>Where her toes point: mostly down, a little forward (a dancer's pointed foot), with the tilt.</summary>
        public static Vector3 ToeDir(FloatSettings s) => Tilted(s, Vector3.Normalize(new Vector3(0f, -0.85f, 0.5f)));

        /// <summary>Knee and ankle joints from the thigh joint.</summary>
        public static (Vector3 Knee, Vector3 Foot) LegJoints(BodyDims b, FloatSettings s, int side, Vector3 thighJoint)
        {
            var knee = thighJoint + ThighDir(s, side) * b.Thigh;
            return (knee, knee + ShinDir(s, side) * b.Shin);
        }

        /// <summary>Elbow and wrist joints from the shoulder joint.</summary>
        public static (Vector3 Elbow, Vector3 Hand) ArmJoints(BodyDims b, FloatSettings s, int side, Vector3 shoulder)
        {
            var elbow = shoulder + UpperArmDir(s, side) * b.UpperArm;
            return (elbow, elbow + ForearmDir(s, side) * b.Forearm);
        }

        /// <summary>A leg segment's direction before the tilt: down, <see cref="FloatSettings.LegAngle"/> out to its side,
        /// pitched <paramref name="pitch"/> degrees forward (negative: back).</summary>
        private static Vector3 Leg(FloatSettings s, int side, float pitch)
        {
            float o = Rad(s.LegAngle), p = Rad(pitch), sign = side == 0 ? -1f : 1f;
            return Vector3.Normalize(new Vector3(sign * (float)Math.Sin(o), -(float)(Math.Cos(o) * Math.Cos(p)), (float)(Math.Cos(o) * Math.Sin(p))));
        }

        /// <summary>An arm segment's direction before the tilt: up, <paramref name="angle"/> degrees out to its side (negative:
        /// in past vertical), with a forward part.</summary>
        private static Vector3 Arm(int side, float angle, float forward)
        {
            float a = Rad(angle), sign = side == 0 ? -1f : 1f;
            return Vector3.Normalize(new Vector3(sign * (float)Math.Sin(a), (float)Math.Cos(a), forward));
        }

        /// <summary>Leaned back by <see cref="FloatSettings.Tilt"/>: turned about her right axis so up goes towards her back.</summary>
        public static Vector3 Tilted(FloatSettings s, Vector3 v)
        {
            float t = -Rad(s.Tilt), c = (float)Math.Cos(t), n = (float)Math.Sin(t);
            return new Vector3(v.X, v.Y * c - v.Z * n, v.Y * n + v.Z * c);
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
}
