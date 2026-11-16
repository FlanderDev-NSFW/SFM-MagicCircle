using System;
using System.Collections.Generic;
using System.Numerics;

namespace MagicCircle.Logic
{
    /// <summary>
    /// The portal's inside: a tube behind the circle, open at the circle (z = 0), closed at its end (z = 1), radius 1 - scaled to
    /// size where it is used. Drawn twice: its outside as an invisible depth mask (hides what is in it from every side but the
    /// opening), its inside as the visible walls. Pure geometry.
    /// </summary>
    public static class Tunnel
    {
        public sealed class MeshData
        {
            public Vector3[] Positions;
            public Vector2[] Uvs; // v = depth (0 at the opening, 1 at the end)
            public int[] Indices;
        }

        /// <summary>The tube with <paramref name="segments"/> sides. <paramref name="inside"/>: faces seen from within (the walls),
        /// else from outside (the mask). A triangle's front is where cross(b - a, c - a) points (Unity's convention).</summary>
        public static MeshData Tube(int segments, bool inside)
        {
            int n = Math.Max(3, segments);
            var pos = new List<Vector3>();
            var uv = new List<Vector2>();
            var idx = new List<int>();
            for (int k = 0; k <= n; k++)
            {
                double a = 2 * Math.PI * k / n;
                float x = (float)Math.Cos(a), y = (float)Math.Sin(a);
                pos.Add(new Vector3(x, y, 0f)); uv.Add(new Vector2((float)k / n, 0f));
                pos.Add(new Vector3(x, y, 1f)); uv.Add(new Vector2((float)k / n, 1f));
            }
            for (int k = 0; k < n; k++)
            {
                int a0 = 2 * k, a1 = 2 * k + 1, b0 = 2 * k + 2, b1 = 2 * k + 3;
                Add(idx, a0, b0, a1, inside);
                Add(idx, b0, b1, a1, inside);
            }
            int centre = pos.Count;
            pos.Add(new Vector3(0f, 0f, 1f)); uv.Add(new Vector2(0.5f, 1f));
            int ring = pos.Count;
            for (int k = 0; k <= n; k++)
            {
                double a = 2 * Math.PI * k / n;
                pos.Add(new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 1f)); uv.Add(new Vector2((float)k / n, 1f));
            }
            for (int k = 0; k < n; k++) Add(idx, centre, ring + k, ring + k + 1, inside);
            return new MeshData { Positions = pos.ToArray(), Uvs = uv.ToArray(), Indices = idx.ToArray() };
        }

        // (a, b, c) as given faces outwards (side: away from the axis; end: +z); flipped for the inside.
        private static void Add(List<int> idx, int a, int b, int c, bool inside)
        {
            idx.Add(a);
            if (inside) { idx.Add(c); idx.Add(b); }
            else { idx.Add(b); idx.Add(c); }
        }

        /// <summary>
        /// Which triangles of her baked meshes make the limb's copy shown inside the tunnel (and over the shrunk stump): those
        /// with a vertex of this limb's own skin (<paramref name="own"/>), no vertex farther along the limb than
        /// <paramref name="maxAlong"/> (the tunnel's end), and past <paramref name="clipFrom"/> none farther from the axis than
        /// <paramref name="maxRadius"/> (it would stick out of the tunnel's wall). <paramref name="along"/> /
        /// <paramref name="radial"/>: each vertex along and off the limb's axis from the joint. Returns the kept triangles' indices.
        /// </summary>
        public static List<int> LimbTriangles(IReadOnlyList<int> triangles, Func<int, bool> own, Func<int, float> along, Func<int, float> radial,
                                              float maxAlong, float clipFrom, float maxRadius)
        {
            var kept = new List<int>();
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (!own(a) && !own(b) && !own(c)) continue;
                bool ok = true;
                foreach (int v in new[] { a, b, c })
                {
                    float al = along(v);
                    if (al > maxAlong || (al > clipFrom && radial(v) > maxRadius)) { ok = false; break; }
                }
                if (!ok) continue;
                kept.Add(a); kept.Add(b); kept.Add(c);
            }
            return kept;
        }
    }
}
