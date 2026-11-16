using System;

namespace MagicCircle.Logic
{
    /// <summary>Where the circles form: around wrists and ankles, or just before elbows and knees (the limbs past them hidden).</summary>
    public enum Placement
    {
        WristsAndAnkles = 0,
        ElbowsAndKnees = 1,
    }

    /// <summary>The stages + / - step through: 0 only shows the circles, the others hold her in a pose.</summary>
    public static class Stages
    {
        public const int Visual = 0, Float = 1;
        public const int Last = Float;

        public static string Name(int stage) => stage switch
        {
            Visual => "circles only",
            Float => "floating",
            _ => "stage " + stage,
        };

        /// <summary>The stage holds her (arms, legs and her place) and refuses what a pose refuses.</summary>
        public static bool Holds(int stage) => stage != Visual;

        public static int Clamp(int stage) => Math.Clamp(stage, Visual, Last);

        public static string PlacementName(Placement p) => p == Placement.ElbowsAndKnees ? "elbows and knees" : "wrists and ankles";

        public static Placement Other(Placement p) => p == Placement.ElbowsAndKnees ? Placement.WristsAndAnkles : Placement.ElbowsAndKnees;

        /// <summary>A smooth 0..1 ramp (ease in and out) for blending into / out of a pose.</summary>
        public static float Smooth(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
