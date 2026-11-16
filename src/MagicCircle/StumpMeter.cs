using System;
using System.Collections.Generic;
using MagicCircle.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace MagicCircle
{
    /// <summary>
    /// Measures how far each shrunk limb's stump reaches past its collapse point (<see cref="Stump.Reach"/>), from a bake of every
    /// skinned mesh on her (body and clothes) as drawn - in onPreCull, where the shrunk bones are what is rendered. Baked every
    /// <see cref="Interval"/> seconds while the limbs are hidden; each result is the larger of the last two measurements, so a
    /// bend that grows between bakes is still covered (<see cref="ReachHold"/>: the largest of the last 20 s, rising fast, sinking slowly).
    /// <para><b>Only the limb's own skin counts.</b> A radius test around the limb also catches whatever else is near it - her
    /// hips, the other leg, the coat - and only in some animations: at idle the knees measured 2.5 cm, walking 3.8 / 6.7 cm and the
    /// circles jumped [observed 2026-11-16]. The meshes are not readable (no bone weights), so each bake is done twice, once with the
    /// hidden bones grown back (<see cref="FloatPoser.WithLimbsGrown"/>): only vertices that move belong to the shrunk bones.</para>
    /// </summary>
    internal sealed class StumpMeter
    {
        public const float Interval = 0.5f, MaxReach = 0.10f;

        private readonly List<int> _owner = new List<int>();
        private readonly List<NVector3>[] _own = { new List<NVector3>(), new List<NVector3>(), new List<NVector3>(), new List<NVector3>() };
        private readonly List<int>[] _ownRenderer = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        private readonly (NVector3 From, NVector3 To)[] _segments = new (NVector3, NVector3)[4];
        private static readonly HumanBodyBones[] Lower = { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
        private static readonly HumanBodyBones[] End = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

        private static (NVector3, NVector3) Segment(Animator anim, int i)
        {
            Transform a = anim.GetBoneTransform(Lower[i]), b = anim.GetBoneTransform(End[i]);
            if (a == null || b == null) return (new NVector3(1e6f), new NVector3(1e6f));
            return (FloatPoser.N(a.position), FloatPoser.N(b.position));
        }
        /// <summary>Diagnostics: per limb, the renderer and the off-axis distance of the farthest point.</summary>
        public static string Farthest = "";
        private readonly List<NVector3> _points = new List<NVector3>(), _grown = new List<NVector3>();
        /// <summary>A vertex that moves less than this when the bones grow back is not the limb's (m).</summary>
        private const float Moves = 0.002f;
        private readonly ReachHold[] _hold = { new ReachHold(), new ReachHold(), new ReachHold(), new ReachHold() };
        private Mesh _bake;
        private float _next;
        private bool _valid;
        private SkinnedMeshRenderer[] _renderers;
        private float _renderersAt = -100f;

        /// <summary>The stump reach (m) for limb <paramref name="i"/> (0 L arm, 1 R arm, 2 L leg, 3 R leg).</summary>
        public float Reach(int i) => _valid ? _hold[i].Shown : 0f;

        /// <summary>Every frame (onPreCull): the shown reaches move towards their held targets.</summary>
        public void Step(float dt) { if (_valid) foreach (var h in _hold) h.Step(Time.unscaledTime, dt); }

        public bool Valid => _valid;

        /// <summary>Forget (the limbs are shown again): the next hide measures at once.</summary>
        public void Reset()
        {
            _valid = false;
            _next = 0f;
            foreach (var h in _hold) h.Reset();
        }

        /// <summary>Bake and measure when due. <paramref name="limbs"/>: per limb the collapse point, the unit axis (upper bone
        /// towards the joint) and the radius around the axis that counts.</summary>
        public void Tick(Animator anim, (NVector3 Collapse, NVector3 Axis, float Radius)[] limbs)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            bool grownOk = false;
            FloatPoser.WithLimbsGrown(anim, () =>
            {
                grownOk = Bake(anim, _grown);
                for (int i = 0; i < 4; i++) _segments[i] = Segment(anim, i);
            });
            if (!grownOk || !Bake(anim, _points) || _points.Count != _grown.Count) return;
            // Each limb's own skin: vertices that move when the bones grow back, given to the limb whose full-size lower segment
            // is nearest (so the other knee's stump, passing close while walking, does not count).
            for (int i = 0; i < 4; i++) { _own[i].Clear(); _ownRenderer[i].Clear(); }
            int moving = 0;
            for (int k = 0; k < _points.Count; k++)
            {
                if (NVector3.DistanceSquared(_points[k], _grown[k]) <= Moves * Moves) continue;
                moving++;
                int limb = Stump.Owner(_grown[k], _segments);
                if (limb < 0) continue;
                _own[limb].Add(_points[k]);
                _ownRenderer[limb].Add(k); // vertex index (diagnostics)
            }
            var far = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                float best = 0f; int who = -1; float off = 0f;
                for (int k = 0; k < _own[i].Count; k++)
                {
                    var d = _own[i][k] - limbs[i].Collapse;
                    float along = NVector3.Dot(d, limbs[i].Axis);
                    float radial = (d - along * limbs[i].Axis).Length();
                    if (along <= best || along > MaxReach || radial > limbs[i].Radius) continue;
                    best = along; who = _ownRenderer[i][k]; off = radial;
                }
                if (who < 0) { far.Append(i).Append(": -; "); continue; }
                var hp = _points[who]; var gp = _grown[who];
                far.Append(i).Append(": ").Append(_renderers[_owner[who]].name).Append(" v").Append(who).Append(" ").Append(Cm(best)).Append(" along, ").Append(Cm(off))
                   .Append(" off, moved ").Append(Cm(NVector3.Distance(hp, gp))).Append(", grown ").Append(Cm((float)Math.Sqrt(Stump.SegmentDistanceSquared(gp, _segments[i].From, _segments[i].To))))
                   .Append(" from its segment, ").Append(Cm(hp.Y - limbs[i].Collapse.Y)).Append(" above the knee/elbow; ");
            }
            Farthest = far.ToString();
            for (int i = 0; i < 4; i++)
            {
                float now = Stump.Reach(_own[i], limbs[i].Collapse, limbs[i].Axis, limbs[i].Radius, MaxReach);
                _hold[i].Add(Time.unscaledTime, now);
            }
            if (!_valid) Plugin.Logger.LogInfo("[SFM-ACCEPT] stump: reach " + Cm(_hold[0].Shown) + " / " + Cm(_hold[1].Shown) + " / " + Cm(_hold[2].Shown) + " / " + Cm(_hold[3].Shown)
                                              + " cm (arm L/R, leg L/R) from " + moving + " of " + _points.Count + " skin points (the limbs' own)");
            _valid = true;
        }

        public string Describe() => _valid ? Cm(_hold[0].Shown) + "/" + Cm(_hold[1].Shown) + "/" + Cm(_hold[2].Shown) + "/" + Cm(_hold[3].Shown) + " cm" : "-";

        private static string Cm(float m) => (m * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Every enabled skinned mesh under her animator, in world space (BakeMesh with the renderer's scale applied:
        /// docs/knowledge/api-notes/player-and-movement.md "Bake to world").</summary>
        private bool Bake(Animator anim, List<NVector3> points)
        {
            if (_renderers == null || Time.unscaledTime - _renderersAt > 5f) // clothes change: look again now and then
            {
                _renderers = anim.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                _renderersAt = Time.unscaledTime;
            }
            if (_bake == null) _bake = new Mesh { name = "MagicCircle.Bake", hideFlags = HideFlags.DontUnloadUnusedAsset };
            points.Clear();
            if (points == _points) _owner.Clear();
            for (int ri = 0; ri < _renderers.Length; ri++)
            {
                var r = _renderers[ri];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh == null) continue;
                r.BakeMesh(_bake, true);
                var verts = _bake.vertices;
                Transform t = r.transform;
                var np = FloatPoser.N(t.position);
                var ns = FloatPoser.N(t.lossyScale);
                var q = t.rotation;
                var nr = new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 v = verts[i];
                    points.Add(np + NVector3.Transform(new NVector3(v.x, v.y, v.z) * ns, nr));
                    if (points == _points) _owner.Add(ri);
                }
            }
            return points.Count > 0;
        }

        public void Destroy()
        {
            if (_bake != null) UnityEngine.Object.Destroy(_bake);
            _bake = null;
        }
    }
}
