using System;
using MagicCircle.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace MagicCircle
{
    /// <summary>
    /// Holds her in the floating narrow X (<see cref="FloatPose"/>) on top of the animation, in <c>LateUpdate</c> (after the
    /// animator, before skinning): hips placed, then each bone turned so its child points along the pose, from the hips
    /// outwards (as Hogtie / WallRestraint, docs/knowledge/patterns.md section 16). A weight blends between the animation and
    /// the pose (rising into it, sinking out of it). Also hides the limbs past the portal circles by shrinking the forearm and
    /// lower-leg bones into their joints.
    /// </summary>
    internal static class FloatPoser
    {
        private static readonly HumanBodyBones[] Bones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm };
        private static readonly Quaternion[] Saved = new Quaternion[Bones.Length];

        public static bool Measure(Animator anim, out BodyDims b, out string problem)
        {
            b = default;
            problem = null;
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform hips = B(HumanBodyBones.Hips), ulL = B(HumanBodyBones.LeftUpperLeg), ulR = B(HumanBodyBones.RightUpperLeg);
            Transform llL = B(HumanBodyBones.LeftLowerLeg), llR = B(HumanBodyBones.RightLowerLeg);
            Transform ftL = B(HumanBodyBones.LeftFoot), ftR = B(HumanBodyBones.RightFoot);
            Transform uaL = B(HumanBodyBones.LeftUpperArm), uaR = B(HumanBodyBones.RightUpperArm);
            Transform laL = B(HumanBodyBones.LeftLowerArm), laR = B(HumanBodyBones.RightLowerArm);
            Transform hdL = B(HumanBodyBones.LeftHand), hdR = B(HumanBodyBones.RightHand);
            if (hips == null || ulL == null || ulR == null || llL == null || llR == null || ftL == null || ftR == null
                || uaL == null || uaR == null || laL == null || laR == null || hdL == null || hdR == null)
            {
                problem = "her skeleton is missing a bone";
                return false;
            }
            float D(Transform a, Transform c) => Vector3.Distance(a.position, c.position);
            b.Thigh = (D(ulL, llL) + D(ulR, llR)) / 2f;
            b.Shin = (D(llL, ftL) + D(llR, ftR)) / 2f;
            b.UpperArm = (D(uaL, laL) + D(uaR, laR)) / 2f;
            b.Forearm = (D(laL, hdL) + D(laR, hdR)) / 2f;
            b.HipHalf = D(ulL, ulR) / 2f;
            b.HipDrop = Vector3.Distance(hips.position, (ulL.position + ulR.position) / 2f);
            Transform toes = B(HumanBodyBones.LeftToes);
            b.Foot = toes != null ? D(ftL, toes) : 0.5f * b.Shin * 0.35f;
            b.AnkleHeight = 0.18f * b.Shin; // ~7 cm standing on the first save's proportions
            if (b.Thigh < 0.05f || b.Shin < 0.05f || b.UpperArm < 0.05f || b.Forearm < 0.03f)
            {
                problem = "her proportions could not be measured";
                return false;
            }
            return true;
        }

        /// <summary>LateUpdate: the pose at <paramref name="weight"/> (0 = the animation as is, 1 = fully floating) in the frame
        /// at <paramref name="origin"/> turned by <paramref name="frame"/>.</summary>
        public static void Apply(Animator anim, BodyDims b, FloatSettings s, Vector3 origin, Quaternion frame, float weight, float time)
        {
            if (weight <= 0f) return;
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            for (int i = 0; i < Bones.Length; i++)
            {
                var t = B(Bones[i]);
                if (t != null) Saved[i] = t.localRotation;
            }
            Transform hips = B(HumanBodyBones.Hips);
            Vector3 savedHips = hips.localPosition;
            Vector3 W(NVector3 p) => origin + frame * U(p);
            Vector3 Dir(NVector3 d) => frame * U(d);
            Vector3 up = frame * Vector3.up, right = frame * Vector3.right;

            // Hips: upright, thigh joints level across her, at the floating height.
            Transform spine = B(HumanBodyBones.Spine), chest = B(HumanBodyBones.Chest), upperChest = B(HumanBodyBones.UpperChest);
            Transform neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            Transform ulL = B(HumanBodyBones.LeftUpperLeg), ulR = B(HumanBodyBones.RightUpperLeg);
            Transform top = chest != null ? chest : neck;
            hips.rotation = Align(top.position - hips.position, ulR.position - ulL.position, up, right) * hips.rotation;
            hips.position = W(FloatPose.Hips(b, s, time));

            // The spine straight up, bone by bone.
            Transform prev = spine;
            foreach (var next in new[] { chest, upperChest, neck })
            {
                if (next == null || prev == null) continue;
                Aim(prev, next, prev.position + up);
                prev = next;
            }
            if (head != null) Aim(neck, head, neck.position + up);

            for (int side = 0; side < 2; side++)
            {
                Transform ul = side == 0 ? ulL : ulR;
                Transform ll = B(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform ft = B(side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                Transform toes = B(side == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                Vector3 legDir = Dir(FloatPose.LegDir(s, side));
                Aim(ul, ll, ul.position + legDir * b.Thigh);
                Aim(ll, ft, ll.position + legDir * b.Shin);
                if (toes != null) Aim(ft, toes, ft.position + Dir(FloatPose.ToeDir()));

                Transform ua = B(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                Transform la = B(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hd = B(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                Vector3 armDir = Dir(FloatPose.ArmDir(s, side));
                Aim(ua, la, ua.position + armDir * b.UpperArm);
                Aim(la, hd, la.position + armDir * b.Forearm);
            }

            if (weight >= 1f) return;
            // Between the animation and the pose: every bone's local rotation part of the way, the hips' place too.
            for (int i = 0; i < Bones.Length; i++)
            {
                var t = B(Bones[i]);
                if (t != null) t.localRotation = Quaternion.Slerp(Saved[i], t.localRotation, weight);
            }
            hips.localPosition = Vector3.Lerp(savedHips, hips.localPosition, weight);
        }

        // --- the portal: forearms and lower legs shrunk into their joints -------------------------------------------------

        private static readonly HumanBodyBones[] Hidden = {
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
        private static readonly HumanBodyBones[] HiddenParent = {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
        private static readonly Vector3[] HiddenScale = new Vector3[Hidden.Length], HiddenPos = new Vector3[Hidden.Length];
        private static bool _hidden;
        private const float Tiny = 1e-4f;

        /// <summary>Shrink the limbs past the elbows and knees into a point <paramref name="pullBack"/> metres before the joint (the
        /// joint is moved there, so the skin around it ends inside the circle), at the end of LateUpdate: skinning uses it.</summary>
        public static void Hide(Animator anim, float pullBack)
        {
            for (int i = 0; i < Hidden.Length; i++)
            {
                var t = anim.GetBoneTransform(Hidden[i]);
                if (t == null) continue;
                HiddenScale[i] = t.localScale;
                HiddenPos[i] = t.localPosition;
                var parent = anim.GetBoneTransform(HiddenParent[i]);
                if (parent != null && pullBack > 0f) t.position = U(CircleSpot.Before(N(parent.position), N(t.position), pullBack));
                t.localScale = HiddenScale[i] * Tiny;
            }
            _hidden = true;
        }

        /// <summary>Their scale and place back (start of LateUpdate, before posing; and when the circle comes off).</summary>
        public static void Unhide(Animator anim)
        {
            if (!_hidden) return;
            _hidden = false;
            if (anim == null) return;
            for (int i = 0; i < Hidden.Length; i++)
            {
                var t = anim.GetBoneTransform(Hidden[i]);
                if (t == null) continue;
                t.localScale = HiddenScale[i];
                t.localPosition = HiddenPos[i];
                UndoCompensation(t);
            }
        }

        /// <summary>
        /// The game's <c>PlayerBoneScaleAdjuster</c> nodes under a bone rescale themselves against their parent when her body
        /// settings apply (a load, the closet): with the bone shrunk they grow by 1 / <see cref="Tiny"/> (10000), so the foot or
        /// hand would come back that much too big [observed 2026-11-16]. No real adjuster is ever 100x: undo it.
        /// </summary>
        private static void UndoCompensation(Transform bone)
        {
            for (int c = 0; c < bone.childCount; c++)
            {
                var child = bone.GetChild(c);
                Vector3 s = child.localScale;
                if (Math.Abs(s.x) < 100f && Math.Abs(s.y) < 100f && Math.Abs(s.z) < 100f) continue;
                child.localScale = s * Tiny;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] portal: undid the game's scale compensation on " + bone.name + "/" + child.name + " (" + s.x + ")");
            }
        }

        public static bool IsHidden => _hidden;

        /// <summary>Run <paramref name="action"/> with the hidden bones at full scale (their place unchanged), then shrink them
        /// again - for telling the limb's own skin from everything else in a bake (only its skin moves).</summary>
        public static void WithLimbsGrown(Animator anim, Action action)
        {
            if (!_hidden) { action(); return; }
            var saved = new Vector3[Hidden.Length];
            for (int i = 0; i < Hidden.Length; i++)
            {
                var t = anim.GetBoneTransform(Hidden[i]);
                if (t == null) continue;
                saved[i] = t.localScale;
                t.localScale = saved[i] / Tiny;
            }
            try { action(); }
            finally
            {
                for (int i = 0; i < Hidden.Length; i++)
                {
                    var t = anim.GetBoneTransform(Hidden[i]);
                    if (t != null) t.localScale = saved[i];
                }
            }
        }

        // --- helpers -------------------------------------------------------------------------------------------------------

        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);

        /// <summary>Turn <paramref name="bone"/> so that <paramref name="child"/> lies in the direction of <paramref name="target"/>.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 target)
        {
            if (bone == null || child == null) return;
            Vector3 cur = child.position - bone.position, want = target - bone.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>The rotation taking direction a1 to b1 exactly and a2 as near to b2 as possible.</summary>
        private static Quaternion Align(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2) =>
            Quaternion.LookRotation(b1, Vector3.Cross(b1, b2)) * Quaternion.Inverse(Quaternion.LookRotation(a1, Vector3.Cross(a1, a2)));
    }
}
