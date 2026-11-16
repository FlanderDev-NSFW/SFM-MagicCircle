using System;
using System.Collections.Generic;
using MagicCircle.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace MagicCircle
{
    /// <summary>
    /// What is behind a portal circle (elbows / knees, limbs hidden): a tunnel you can look into from her body's side, with her
    /// limb going on inside it and fading into the dark - and nothing of it visible from any other side. Per limb:
    /// <list type="bullet">
    /// <item><b>Mask:</b> the tunnel's outside drawn depth-only (the game's Particles/Standard Unlit with Blend Zero One, ZWrite
    /// on), after the scene and the sky: whatever is drawn later inside it is hidden unless seen through the opening.</item>
    /// <item><b>Walls</b> (inside faces, glowing at the opening, black at the end) and <b>veils</b> (dark see-through discs): the
    /// look of depth.</item>
    /// <item><b>Limb copy:</b> the first part of her forearm / shin as a rigid mesh on her upper arm / thigh - cut out of a bake
    /// of her skinned meshes with the limb straight and at full size (the real one is shrunk into the joint), her own
    /// materials. It starts where the real skin starts to shrink (so it also covers the stump) and ends at the tunnel's end.</item>
    /// <item><b>Far disc:</b> the portal surface, facing away from her, drawn before the mask: from beyond the circle one sees
    /// it, not into her arm.</item>
    /// </list>
    /// </summary>
    internal sealed class PortalInterior
    {
        // After the opaque scene and the skybox (2500), before ordinary transparent things (3000) and the circles (3100).
        private const int QDisc = 2980, QMask = 2985, QWalls = 2988, QCopy = 2990, QVeil = 2992;
        private static readonly float[] VeilAt = { 0.14f, 0.28f, 0.42f, 0.56f, 0.7f, 0.82f, 0.93f };
        private const float VeilAlpha = 0.24f, Moves = 0.002f;

        private static readonly HumanBodyBones[] Upper = { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
        private static readonly HumanBodyBones[] Lower = { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
        private static readonly HumanBodyBones[] End = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

        private GameObject _root;
        private readonly Transform[] _tunnel = new Transform[4], _mask = new Transform[4], _walls = new Transform[4], _disc = new Transform[4], _copy = new Transform[4];
        private readonly Transform[][] _veils = new Transform[4][];
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(), _copyOwned = new List<UnityEngine.Object>();
        private Material _maskMat, _wallMat, _veilMat, _discMat;
        private Texture2D _wallTex;
        private Mesh _bake;
        private int _layer;
        private string _tint;

        private readonly float[] _end = new float[4], _radius = new float[4];
        private int _signature;
        private float _checkedAt = -100f, _pull;

        /// <summary>The game has the shader the mask needs (else the flat portal surface is used).</summary>
        public bool Supported { get; private set; } = true;
        public bool Built => _root != null;
        public bool Captured { get; private set; }

        /// <summary>Diagnostics: "copy: 2 / 2 / 3 / 3 parts, 5210 triangles".</summary>
        public static string Info = "-";

        /// <summary>How far past the true joint limb <paramref name="i"/>'s tunnel ends (m).</summary>
        public float EndAlong(int i) => _end[i];

        public void Build(int layer, Material portalSurface)
        {
            Destroy();
            try
            {
                Shader particle = Shader.Find("Particles/Standard Unlit"), unlit = Shader.Find("Unlit/Texture");
                if (particle == null || !particle.isSupported || unlit == null || !unlit.isSupported || portalSurface == null)
                {
                    Supported = false;
                    Plugin.Logger.LogWarning("portal interior: shaders missing, using the flat portal surface");
                    return;
                }
                _layer = layer;
                _root = new GameObject("MagicCircle.Interior");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                _root.layer = layer;

                // Depth only: Blend Zero One leaves the picture as it is, ZWrite fills the depth buffer.
                _maskMat = Own(new Material(particle) { name = "MagicCircle.Mask" });
                _maskMat.SetFloat("_SrcBlend", 0f);
                _maskMat.SetFloat("_DstBlend", 1f);
                _maskMat.SetFloat("_ZWrite", 1f);
                _maskMat.SetFloat("_Cull", 2f);
                _maskMat.renderQueue = QMask;

                _wallTex = Own(new Texture2D(4, 64, TextureFormat.RGBA32, false) { name = "MagicCircle.Walls", wrapMode = TextureWrapMode.Clamp });
                _wallMat = Own(new Material(unlit) { name = "MagicCircle.Walls", mainTexture = _wallTex });
                _wallMat.renderQueue = QWalls;

                _veilMat = Own(new Material(particle) { name = "MagicCircle.Veil" });
                _veilMat.SetFloat("_SrcBlend", 5f);  // SrcAlpha
                _veilMat.SetFloat("_DstBlend", 10f); // OneMinusSrcAlpha
                _veilMat.SetFloat("_ZWrite", 0f);
                _veilMat.SetFloat("_Cull", 0f);
                _veilMat.EnableKeyword("_ALPHABLEND_ON");
                _veilMat.SetColor("_Color", new Color(0f, 0f, 0f, VeilAlpha));
                _veilMat.renderQueue = QVeil;

                _discMat = Own(new Material(portalSurface) { name = "MagicCircle.FarDisc" });
                _discMat.renderQueue = QDisc;

                Mesh tubeOut = ToMesh(Tunnel.Tube(40, false), "MagicCircle.TubeOut"), tubeIn = ToMesh(Tunnel.Tube(40, true), "MagicCircle.TubeIn");
                Mesh far = Disc(false), veil = Disc(true);
                for (int i = 0; i < 4; i++)
                {
                    _tunnel[i] = Node("Tunnel" + i, _root.transform);
                    _disc[i] = Part("FarDisc", _tunnel[i], far, _discMat);
                    _mask[i] = Part("Mask", _tunnel[i], tubeOut, _maskMat);
                    _walls[i] = Part("Walls", _tunnel[i], tubeIn, _wallMat);
                    _veils[i] = new Transform[VeilAt.Length];
                    for (int v = 0; v < VeilAt.Length; v++) _veils[i][v] = Part("Veil" + v, _tunnel[i], veil, _veilMat);
                    _copy[i] = Node("Limb" + i, _root.transform);
                }
                _root.SetActive(false);
                _tint = null;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] portal interior: built (mask " + particle.name + ", walls " + unlit.name + ")");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("building the portal interior failed, using the flat portal surface: " + e);
                Destroy();
                Supported = false;
            }
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>Forget the limb copies (the circle came off, the limbs show again): captured anew next time.</summary>
        public void Forget()
        {
            Captured = false;
            SetVisible(false);
        }

        /// <summary>The walls' glow in the circles' colour.</summary>
        public void Tint(string hex)
        {
            if (_wallTex == null || hex == _tint) return;
            _tint = hex;
            Color c = CircleRig.ParseColor(hex);
            var px = new Color32[4 * 64];
            for (int y = 0; y < 64; y++)
            {
                // Bright at the opening, gone a third of the way in.
                float v = 0.75f * Mathf.Exp(-6f * y / 63f);
                var p = new Color32((byte)(255 * Mathf.Clamp01(c.r * v)), (byte)(255 * Mathf.Clamp01(c.g * v)), (byte)(255 * Mathf.Clamp01(c.b * v)), 255);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = p;
            }
            _wallTex.SetPixels32(px);
            _wallTex.Apply(false, false);
        }

        /// <summary>Place limb <paramref name="i"/>'s tunnel: opening at <paramref name="plane"/> (the circle), along
        /// <paramref name="axis"/> (unit), <paramref name="radius"/> wide, <paramref name="depth"/> deep; the limb copy on
        /// <paramref name="upper"/> (her upper arm / thigh as drawn).</summary>
        public void Place(int i, Vector3 plane, Vector3 axis, float radius, float depth, Transform upper)
        {
            if (_root == null || _tunnel[i] == null) return;
            depth = Mathf.Max(depth, 0.02f);
            _tunnel[i].SetPositionAndRotation(plane, Quaternion.FromToRotation(Vector3.forward, axis));
            _mask[i].localScale = new Vector3(radius, radius, depth);
            _walls[i].localScale = new Vector3(radius * 0.998f, radius * 0.998f, depth * 0.998f);
            _disc[i].localScale = Vector3.one * radius;
            for (int v = 0; v < VeilAt.Length; v++)
            {
                _veils[i][v].localPosition = new Vector3(0f, 0f, depth * VeilAt[v]);
                _veils[i][v].localScale = Vector3.one * (radius * 0.99f);
            }
            _copy[i].SetPositionAndRotation(upper.position, upper.rotation);
            _copy[i].localScale = upper.lossyScale;
        }

        /// <summary>The copies must be made (again): none yet, the tunnels' size changed, or she wears something else.</summary>
        public bool NeedsCapture(Animator anim, float[] radius, float pull)
        {
            if (!Captured) return true;
            if (Math.Abs(pull - _pull) > 0.002f) return true;
            for (int i = 0; i < 4; i++) if (Math.Abs(radius[i] - _radius[i]) > 0.05f * _radius[i]) return true;
            if (Time.unscaledTime - _checkedAt < 5f) return false;
            _checkedAt = Time.unscaledTime;
            return Signature(anim.GetComponentsInChildren<SkinnedMeshRenderer>(false)) != _signature;
        }

        private static int Signature(SkinnedMeshRenderer[] renderers)
        {
            int s = 17;
            foreach (var r in renderers)
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh != null) s = s * 31 + r.GetInstanceID();
            return s;
        }

        private sealed class Baked
        {
            public int Start;
            public NVector3[] World, Normal;
            public Vector2[] Uv;
            public int[][] Triangles;
            public Material[] Materials;
            public string Name;
        }

        /// <summary>
        /// Make the limb copies - call in onPreCull while the limbs are hidden. Bakes her skinned meshes twice: with the limbs at
        /// full size and straight (lower bone in line with the upper one: the copy goes straight into the tunnel whatever her
        /// knee does), and shrunk as drawn. Vertices that differ are the limbs' own (given to the limb whose straight segment is
        /// nearest); triangles with such a vertex, up to the tunnel's end and inside its wall, become the copy, in the upper
        /// bone's coordinates. <paramref name="radius"/>: each tunnel's radius; <paramref name="pull"/>: how far the collapse point
        /// is before the joint (FloatPoser.Hide).
        /// </summary>
        public bool Capture(Animator anim, float[] radius, float pull)
        {
            Captured = false;
            foreach (var o in _copyOwned) if (o != null) UnityEngine.Object.Destroy(o);
            _copyOwned.Clear();
            for (int i = 0; i < 4; i++)
                for (int c = _copy[i].childCount - 1; c >= 0; c--) UnityEngine.Object.Destroy(_copy[i].GetChild(c).gameObject);

            var renderers = anim.GetComponentsInChildren<SkinnedMeshRenderer>(false);
            _signature = Signature(renderers);
            _checkedAt = Time.unscaledTime;
            _pull = pull;
            Transform[] up = new Transform[4], lo = new Transform[4], en = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                up[i] = anim.GetBoneTransform(Upper[i]); lo[i] = anim.GetBoneTransform(Lower[i]); en[i] = anim.GetBoneTransform(End[i]);
                if (up[i] == null || lo[i] == null || en[i] == null) return false;
                _radius[i] = radius[i];
            }

            // 1. Full size and straight.
            var joint = new NVector3[4];
            var axis = new NVector3[4];
            var segments = new (NVector3 From, NVector3 To)[4];
            var upPos = new NVector3[4];
            var upScale = new NVector3[4];
            var upInv = new System.Numerics.Quaternion[4];
            var savedRot = new Quaternion[4];
            List<Baked> full;
            FloatPoser.Unhide(anim);
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    savedRot[i] = lo[i].rotation;
                    Vector3 along = lo[i].position - up[i].position, now = en[i].position - lo[i].position;
                    if (along.sqrMagnitude > 1e-8f && now.sqrMagnitude > 1e-8f) lo[i].rotation = Quaternion.FromToRotation(now, along) * lo[i].rotation;
                    joint[i] = FloatPoser.N(lo[i].position);
                    axis[i] = NVector3.Normalize(FloatPoser.N(along));
                    segments[i] = (joint[i], FloatPoser.N(en[i].position));
                    float lower = Vector3.Distance(lo[i].position, en[i].position);
                    _end[i] = (i < 2 ? 0.65f : 0.5f) * lower; // well before the wrist / ankle
                    upPos[i] = FloatPoser.N(up[i].position);
                    upScale[i] = FloatPoser.N(up[i].lossyScale);
                    Quaternion q = Quaternion.Inverse(up[i].rotation);
                    upInv[i] = new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
                }
                full = Bake(renderers, true);
            }
            finally
            {
                for (int i = 0; i < 4; i++) if (lo[i] != null) lo[i].rotation = savedRot[i];
                FloatPoser.Hide(anim, pull);
            }
            // 2. As drawn (shrunk).
            var shrunk = Bake(renderers, false);
            if (full.Count == 0 || full.Count != shrunk.Count) return false;

            int parts = 0, triangles = 0;
            var perLimb = new int[4];
            for (int b = 0; b < full.Count; b++)
            {
                Baked f = full[b], s = shrunk[b];
                if (f.World.Length != s.World.Length) continue;
                int n = f.World.Length;
                var owner = new int[n]; // -1: not a limb's own skin
                var along = new float[n];
                var radial = new float[n];
                for (int v = 0; v < n; v++)
                {
                    owner[v] = -1;
                    if (NVector3.DistanceSquared(f.World[v], s.World[v]) <= Moves * Moves) continue;
                    owner[v] = Stump.Owner(f.World[v], segments);
                }
                for (int limb = 0; limb < 4; limb++)
                {
                    for (int v = 0; v < n; v++)
                    {
                        NVector3 d = f.World[v] - joint[limb];
                        along[v] = NVector3.Dot(d, axis[limb]);
                        radial[v] = (d - along[v] * axis[limb]).Length();
                    }
                    int me = limb;
                    for (int sub = 0; sub < f.Triangles.Length; sub++)
                    {
                        if (sub >= f.Materials.Length || f.Materials[sub] == null) continue;
                        // Past the earliest place the circle can be (the collapse point), nothing may stick out of the tunnel.
                        var kept = Tunnel.LimbTriangles(f.Triangles[sub], v => owner[v] == me, v => along[v], v => radial[v],
                                                        _end[limb] - 0.004f, -pull, radius[limb] * 0.96f);
                        if (kept.Count == 0) continue;
                        AddPart(limb, f, kept, sub, upPos[limb], upScale[limb], upInv[limb]);
                        parts++;
                        perLimb[limb]++;
                        triangles += kept.Count / 3;
                    }
                }
            }
            Captured = parts > 0;
            Info = "copy: " + perLimb[0] + " / " + perLimb[1] + " / " + perLimb[2] + " / " + perLimb[3] + " parts, " + triangles + " triangles; ends "
                   + Cm(_end[0]) + " / " + Cm(_end[2]) + " cm past elbow / knee";
            Plugin.Logger.LogInfo("[SFM-ACCEPT] portal interior: " + Info);
            return Captured;
        }

        private static string Cm(float m) => (m * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        private void AddPart(int limb, Baked f, List<int> kept, int sub, NVector3 upPos, NVector3 upScale, System.Numerics.Quaternion upInv)
        {
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new int[kept.Count];
            for (int t = 0; t < kept.Count; t++)
            {
                int v = kept[t];
                if (!map.TryGetValue(v, out int index))
                {
                    index = verts.Count;
                    map[v] = index;
                    NVector3 local = NVector3.Transform(f.World[v] - upPos, upInv) / upScale;
                    verts.Add(new Vector3(local.X, local.Y, local.Z));
                    NVector3 nl = NVector3.Transform(f.Normal[v], upInv);
                    normals.Add(new Vector3(nl.X, nl.Y, nl.Z));
                    uvs.Add(v < f.Uv.Length ? f.Uv[v] : Vector2.zero);
                }
                tris[t] = index;
            }
            var mesh = new Mesh { name = "MagicCircle.Limb" + limb + "." + f.Name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _copyOwned.Add(mesh);
            var mat = new Material(f.Materials[sub]) { renderQueue = QCopy };
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _copyOwned.Add(mat);
            Part(f.Name + "." + sub, _copy[limb], mesh, mat);
        }

        /// <summary>Her enabled skinned meshes in world space; <paramref name="everything"/>: also normals, UVs, triangles and
        /// materials (the bake's mesh is readable although the game's own meshes are not).</summary>
        private List<Baked> Bake(SkinnedMeshRenderer[] renderers, bool everything)
        {
            var result = new List<Baked>();
            if (_bake == null) _bake = Own(new Mesh { name = "MagicCircle.InteriorBake" });
            int start = 0;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh == null) continue;
                r.BakeMesh(_bake, true);
                var verts = _bake.vertices;
                Transform t = r.transform;
                NVector3 np = FloatPoser.N(t.position), ns = FloatPoser.N(t.lossyScale);
                Quaternion q = t.rotation;
                var nr = new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
                var b = new Baked { Start = start, World = new NVector3[verts.Length], Name = r.name };
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 v = verts[i];
                    b.World[i] = np + NVector3.Transform(new NVector3(v.x, v.y, v.z) * ns, nr);
                }
                if (everything)
                {
                    var normals = _bake.normals;
                    b.Normal = new NVector3[verts.Length];
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 nv = i < normals.Length ? normals[i] : Vector3.up;
                        b.Normal[i] = NVector3.Transform(new NVector3(nv.x, nv.y, nv.z), nr);
                    }
                    b.Uv = _bake.uv;
                    int subs = _bake.subMeshCount;
                    b.Triangles = new int[subs][];
                    for (int s = 0; s < subs; s++) b.Triangles[s] = _bake.GetTriangles(s);
                    b.Materials = r.sharedMaterials;
                }
                start += verts.Length;
                result.Add(b);
            }
            return result;
        }

        // --- building -----------------------------------------------------------------------------------------------------

        private T Own<T>(T o) where T : UnityEngine.Object
        {
            o.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(o);
            return o;
        }

        private Transform Node(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.layer = _layer;
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        private Transform Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var t = Node(name, parent);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.allowOcclusionWhenDynamic = false;
            return t;
        }

        private Mesh ToMesh(Tunnel.MeshData d, string name)
        {
            var m = Own(new Mesh { name = name });
            var v = new Vector3[d.Positions.Length];
            var uv = new Vector2[d.Uvs.Length];
            for (int i = 0; i < v.Length; i++) { v[i] = FloatPoser.U(d.Positions[i]); uv[i] = new Vector2(d.Uvs[i].X, d.Uvs[i].Y); }
            m.vertices = v;
            m.uv = uv;
            m.triangles = d.Indices;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A disc of radius 1 in the XY plane: facing +z only (seen from beyond the circle), or both ways.</summary>
        private Mesh Disc(bool bothSides)
        {
            const int n = 40;
            var v = new Vector3[n + 1];
            var uv = new Vector2[n + 1];
            v[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int k = 0; k < n; k++)
            {
                float a = 2f * Mathf.PI * k / n;
                v[k + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                uv[k + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
            }
            var tri = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int a = k + 1, b = (k + 1) % n + 1;
                tri.Add(0); tri.Add(a); tri.Add(b); // front towards +z
                if (bothSides) { tri.Add(0); tri.Add(b); tri.Add(a); }
            }
            var m = Own(new Mesh { name = bothSides ? "MagicCircle.Veil" : "MagicCircle.FarDisc" });
            m.vertices = v;
            m.uv = uv;
            m.triangles = tri.ToArray();
            m.RecalculateBounds();
            return m;
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            foreach (var o in _copyOwned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            _copyOwned.Clear();
            _bake = null;
            Captured = false;
        }
    }
}
