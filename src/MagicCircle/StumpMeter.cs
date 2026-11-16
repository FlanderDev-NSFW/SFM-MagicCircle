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
    /// bend that grows between bakes is still covered.
    /// </summary>
    internal sealed class StumpMeter
    {
        public const float Interval = 0.5f, MaxReach = 0.10f;

        private readonly List<NVector3> _points = new List<NVector3>();
        private readonly float[] _reach = new float[4], _last = new float[4];
        private Mesh _bake;
        private float _next;
        private bool _valid;
        private SkinnedMeshRenderer[] _renderers;
        private float _renderersAt = -100f;

        /// <summary>The stump reach (m) for limb <paramref name="i"/> (0 L arm, 1 R arm, 2 L leg, 3 R leg).</summary>
        public float Reach(int i) => _valid ? _reach[i] : 0f;

        public bool Valid => _valid;

        /// <summary>Forget (the limbs are shown again): the next hide measures at once.</summary>
        public void Reset()
        {
            _valid = false;
            _next = 0f;
            Array.Clear(_reach, 0, 4);
            Array.Clear(_last, 0, 4);
        }

        /// <summary>Bake and measure when due. <paramref name="limbs"/>: per limb the collapse point, the unit axis (upper bone
        /// towards the joint) and the radius around the axis that counts.</summary>
        public void Tick(Animator anim, (NVector3 Collapse, NVector3 Axis, float Radius)[] limbs)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            if (!Bake(anim)) return;
            for (int i = 0; i < 4; i++)
            {
                float now = Stump.Reach(_points, limbs[i].Collapse, limbs[i].Axis, limbs[i].Radius, MaxReach);
                _reach[i] = _valid ? Math.Max(now, _last[i]) : now;
                _last[i] = now;
            }
            if (!_valid) Plugin.Logger.LogInfo("[SFM-ACCEPT] stump: reach " + Cm(_reach[0]) + " / " + Cm(_reach[1]) + " / " + Cm(_reach[2]) + " / " + Cm(_reach[3])
                                              + " cm (arm L/R, leg L/R) from " + _points.Count + " skin points");
            _valid = true;
        }

        public string Describe() => _valid ? Cm(_reach[0]) + "/" + Cm(_reach[1]) + "/" + Cm(_reach[2]) + "/" + Cm(_reach[3]) + " cm" : "-";

        private static string Cm(float m) => (m * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Every enabled skinned mesh under her animator, in world space (BakeMesh with the renderer's scale applied:
        /// docs/knowledge/api-notes/player-and-movement.md "Bake to world").</summary>
        private bool Bake(Animator anim)
        {
            if (_renderers == null || Time.unscaledTime - _renderersAt > 5f) // clothes change: look again now and then
            {
                _renderers = anim.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                _renderersAt = Time.unscaledTime;
            }
            if (_bake == null) _bake = new Mesh { name = "MagicCircle.Bake", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _points.Clear();
            foreach (var r in _renderers)
            {
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
                    _points.Add(np + NVector3.Transform(new NVector3(v.x, v.y, v.z) * ns, nr));
                }
            }
            return _points.Count > 0;
        }

        public void Destroy()
        {
            if (_bake != null) UnityEngine.Object.Destroy(_bake);
            _bake = null;
        }
    }
}
