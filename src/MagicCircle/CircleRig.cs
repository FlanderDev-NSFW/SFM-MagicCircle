using System;
using System.Collections.Generic;
using MagicCircle.Logic;
using UnityEngine;

namespace MagicCircle
{
    /// <summary>
    /// The four visible circles: each a flat quad with the circle pattern (<see cref="CirclePattern.Circle"/>) and a soft glow
    /// quad behind it, both unlit and transparent (a particle shader when the game has one: glowing, never shaded). Placed on
    /// the limbs as drawn (onPreCull), turning slowly, the glow pulsing; they grow in as the circle forms.
    /// </summary>
    internal sealed class CircleRig
    {
        private const int TextureSize = 256;
        private GameObject _root;
        private readonly Transform[] _circles = new Transform[4], _halos = new Transform[4], _portals = new Transform[4];
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private Material _circleMat, _haloMat, _portalMat;
        private Texture2D _portalTex;
        private byte[] _portalShade;
        private string _colorHex;

        /// <summary>The shader chosen (diagnostics).</summary>
        public static string ShaderName = "-";

        public bool Built => _root != null;

        public void Build(int layer)
        {
            try { BuildParts(layer); }
            catch (Exception e)
            {
                Plugin.Logger.LogError("building the circles failed: " + e);
                Destroy();
            }
        }

        private void BuildParts(int layer)
        {
            Destroy();
            _root = new GameObject("MagicCircle");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            var quad = MakeQuad();
            Shader shader = PickShader();
            _circleMat = MakeMaterial(shader, MakeTexture(CirclePattern.Circle(TextureSize, 7), "MagicCircle.Circle"), "MagicCircle.Circle");
            _haloMat = MakeMaterial(shader, MakeTexture(CirclePattern.Halo(TextureSize / 2), "MagicCircle.Halo"), "MagicCircle.Halo");
            _portalMat = MakePortalMaterial();
            _colorHex = null;
            var disc = MakeDisc();
            for (int i = 0; i < 4; i++)
            {
                var limb = new GameObject("Circle" + i);
                limb.layer = layer;
                limb.transform.SetParent(_root.transform, false);
                _halos[i] = Part(limb.transform, "Glow", quad, _haloMat, layer);
                _circles[i] = Part(limb.transform, "Runes", quad, _circleMat, layer);
                _portals[i] = Part(limb.transform, "Portal", disc, _portalMat, layer);
                _portals[i].gameObject.SetActive(false);
            }
            _root.SetActive(false);
        }

        private static Transform Part(Transform parent, string name, Mesh quad, Material mat, int layer)
        {
            var g = new GameObject(name);
            g.layer = layer;
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.allowOcclusionWhenDynamic = false; // patterns.md 17b: runtime renderers vanish behind baked occlusion otherwise
            return g.transform;
        }

        /// <summary>Place circle <paramref name="i"/> at <paramref name="center"/>, facing along <paramref name="axis"/> (the limb),
        /// <paramref name="radius"/> metres across its outer ring, turned by <paramref name="spinDeg"/>; <paramref name="show"/>
        /// 0..1 grows it in.</summary>
        public void Place(int i, Vector3 center, Vector3 axis, float radius, float spinDeg, float show, bool portal = false)
        {
            if (_portals[i] != null)
            {
                if (_portals[i].gameObject.activeSelf != portal) _portals[i].gameObject.SetActive(portal);
                // The surface fills the hole up to the inner thin ring (r 0.6 of the circle): opaque, it hides the limb's end.
                _portals[i].localRotation = Quaternion.Euler(0f, 0f, -0.3f * spinDeg);
                _portals[i].localScale = Vector3.one * (radius * 0.6f);
            }
            if (_root == null || _circles[i] == null || axis.sqrMagnitude < 1e-10f) return;
            var face = Quaternion.FromToRotation(Vector3.forward, axis.normalized);
            float grow = Stages.Smooth(show);
            var t = _circles[i].parent;
            t.position = center;
            t.rotation = face;
            _circles[i].localRotation = Quaternion.Euler(0f, 0f, spinDeg);
            _circles[i].localScale = Vector3.one * (radius * (0.6f + 0.4f * grow));
            _halos[i].localRotation = Quaternion.Euler(0f, 0f, -0.5f * spinDeg);
            _halos[i].localScale = Vector3.one * (radius * 1.25f * (0.6f + 0.4f * grow));
        }

        /// <summary>Colour and brightness for this frame: <paramref name="show"/> fades in, <paramref name="pulse"/> 0..1 the
        /// glow's slow pulse.</summary>
        public void Shade(string hex, float glow, float pulse, float show)
        {
            if (_root == null) return;
            if (hex != _colorHex)
            {
                _colorHex = hex;
                _color = ParseColor(hex);
                TintPortal();
            }
            float fade = Stages.Smooth(show);
            SetColor(_circleMat, _color, (0.85f + 0.15f * pulse) * fade);
            SetColor(_haloMat, _color, glow * (0.55f + 0.45f * pulse) * fade);
        }

        private Color _color = new Color(1f, 0.44f, 0.88f);

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }

        // --- building ---------------------------------------------------------------------------------------------------------

        /// <summary>The portal surface: opaque and unlit (it must hide what is behind it, so it writes depth like a solid), colour
        /// baked into its texture. Unlit/Texture is loaded in the game; Unlit/Color as a fallback.</summary>
        private Material MakePortalMaterial()
        {
            _portalShade = CirclePattern.Portal(TextureSize / 2);
            int size = TextureSize / 2;
            _portalTex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "MagicCircle.Portal" };
            _portalTex.wrapMode = TextureWrapMode.Clamp;
            _portalTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(_portalTex);
            Shader shader = Shader.Find("Unlit/Texture");
            bool textured = shader != null && shader.isSupported;
            if (!textured) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "MagicCircle.Portal" };
            if (textured) m.mainTexture = _portalTex;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(m);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] portal surface: " + (shader == null ? "no shader" : shader.name));
            return m;
        }

        private void TintPortal()
        {
            if (_portalTex == null || _portalShade == null) return;
            var px = new Color32[_portalShade.Length];
            for (int i = 0; i < px.Length; i++)
            {
                float v = _portalShade[i] / 255f;
                px[i] = new Color32((byte)(255 * Mathf.Clamp01(_color.r * v)), (byte)(255 * Mathf.Clamp01(_color.g * v)), (byte)(255 * Mathf.Clamp01(_color.b * v)), 255);
            }
            _portalTex.SetPixels32(px);
            _portalTex.Apply(true, false);
            if (_portalMat != null && _portalMat.HasProperty("_Color")) _portalMat.SetColor("_Color", _color * 0.3f);
        }

        /// <summary>A round disc of radius 1 facing both ways (two sets of triangles: the unlit shader culls back faces).</summary>
        private Mesh MakeDisc()
        {
            const int n = 48;
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
            var tri = new int[n * 6];
            for (int k = 0; k < n; k++)
            {
                int a = k + 1, b = (k + 1) % n + 1;
                tri[6 * k] = 0; tri[6 * k + 1] = b; tri[6 * k + 2] = a;     // one side
                tri[6 * k + 3] = 0; tri[6 * k + 4] = a; tri[6 * k + 5] = b; // the other
            }
            var m = new Mesh { name = "MagicCircle.Disc" };
            m.vertices = v;
            m.uv = uv;
            m.triangles = tri;
            m.RecalculateBounds();
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(m);
            return m;
        }

        private Mesh MakeQuad()
        {
            var m = new Mesh { name = "MagicCircle.Quad" };
            m.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(m);
            return m;
        }

        private Texture2D MakeTexture(byte[] alpha, string name)
        {
            int size = (int)Math.Round(Math.Sqrt(alpha.Length));
            var px = new Color32[alpha.Length];
            for (int i = 0; i < alpha.Length; i++) px[i] = new Color32(255, 255, 255, alpha[i]);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name };
            tex.SetPixels32(px); // (LoadRawTextureData would need every mip level)
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.Apply(true, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _owned.Add(tex);
            return tex;
        }

        /// <summary>Unlit, transparent, two-sided: additive particle shaders glow; Sprites/Default is always there.</summary>
        private static Shader PickShader()
        {
            // Loaded in the game [observed 2026-11-16]: Particles/Standard Unlit, Legacy Shaders/Particles/Alpha Blended (+ Premultiply),
            // Sprites/Default, Unlit/Transparent, UI/Default - no additive particle shader (Shader.Find("...Additive (Soft)") returned
            // one that drew white without a tint).
            foreach (string name in new[] { "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended", "Sprites/Default", "Unlit/Transparent" })
            {
                var s = Shader.Find(name);
                if (s == null || !s.isSupported) continue;
                ShaderName = name;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] shader: " + name);
                return s;
            }
            ShaderName = "Standard (fallback)";
            Plugin.Logger.LogWarning("no unlit transparent shader found; the circles use Standard");
            return Shader.Find("Standard");
        }

        private Material MakeMaterial(Shader shader, Texture2D tex, string name)
        {
            var m = new Material(shader) { name = name, mainTexture = tex };
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (shader != null && shader.name == "Particles/Standard Unlit")
            {
                // Additive, as the particle shader GUI sets it: glows, never darkens what is behind.
                m.SetFloat("_SrcBlend", 5f); // SrcAlpha
                m.SetFloat("_DstBlend", 1f); // One
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", 0f);     // both sides
                m.EnableKeyword("_ALPHABLEND_ON");
            }
            m.renderQueue = 3100; // after the game's transparent objects
            _owned.Add(m);
            return m;
        }

        private static void SetColor(Material m, Color c, float strength)
        {
            // Additive particles: colour x 2 x _TintColor, so 0.5 = as is; alpha blended shaders: alpha is the strength.
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f * strength, c.g * 0.5f * strength, c.b * 0.5f * strength, 0.5f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(c.r, c.g, c.b, strength));
        }

        internal static Color ParseColor(string hex)
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v))
                return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
            return new Color(1f, 0.44f, 0.88f);
        }
    }
}
