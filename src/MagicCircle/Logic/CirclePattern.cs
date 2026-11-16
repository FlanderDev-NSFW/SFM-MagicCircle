using System;
using System.Collections.Generic;

namespace MagicCircle.Logic
{
    /// <summary>
    /// The magic circle's look, drawn in code (no image files): an alpha mask, white where the circle is. Radii are fractions of
    /// the texture's half size. The middle (inside <see cref="ClearRadius"/>) stays empty: her limb passes through it.
    /// Pure: the caller makes a texture from it.
    /// </summary>
    public static class CirclePattern
    {
        /// <summary>Nothing is drawn inside this radius (the limb's hole).</summary>
        public const float ClearRadius = 0.56f;

        /// <summary>The circle: two outer rings, a band of runes, two inner rings, a six-pointed star and dots.</summary>
        public static byte[] Circle(int size, int seed)
        {
            var a = new float[size * size];
            Ring(a, size, 0.95f, 0.025f);
            Ring(a, size, 0.885f, 0.009f);
            Ring(a, size, 0.665f, 0.018f);
            Ring(a, size, 0.595f, 0.008f);
            // Six-pointed star: two triangles with their points on the thin outer ring (the parts in the hole are clipped).
            for (int t = 0; t < 2; t++)
                for (int k = 0; k < 3; k++)
                {
                    double a0 = Math.PI / 2 + t * Math.PI / 3 + k * 2 * Math.PI / 3, a1 = a0 + 2 * Math.PI / 3;
                    Line(a, size, Polar(0.885f, a0), Polar(0.885f, a1), 0.007f);
                }
            // Runes: 20 glyphs between the rings, each two to four strokes in its own cell (polar box), from the seed.
            var rng = new Random(seed);
            const int glyphs = 20;
            for (int g = 0; g < glyphs; g++)
            {
                double mid = g * 2 * Math.PI / glyphs, half = Math.PI / glyphs * 0.62;
                int strokes = 2 + rng.Next(3);
                for (int s = 0; s < strokes; s++)
                {
                    float r0 = 0.72f + 0.12f * (float)rng.NextDouble(), r1 = 0.72f + 0.12f * (float)rng.NextDouble();
                    double t0 = mid + half * (2 * rng.NextDouble() - 1), t1 = mid + half * (2 * rng.NextDouble() - 1);
                    Line(a, size, Polar(r0, t0), Polar(r1, t1), 0.008f);
                }
            }
            // Dots between the outer rings.
            for (int d = 0; d < 12; d++) Dot(a, size, Polar(0.918f, d * Math.PI / 6 + Math.PI / 12), 0.012f);
            return ToBytes(a, size, ClearRadius);
        }

        /// <summary>The soft glow behind the circle: a blurred band, strongest at the runes, nothing in the hole.</summary>
        public static byte[] Halo(int size)
        {
            var a = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Radius(x, y, size);
                    float d = (r - 0.78f) / 0.17f;
                    a[y * size + x] = 0.55f * (float)Math.Exp(-d * d);
                }
            return ToBytes(a, size, ClearRadius - 0.06f);
        }

        /// <summary>A pixel's distance from the middle, as a fraction of the half size.</summary>
        public static float Radius(int x, int y, int size)
        {
            float h = size / 2f, fx = (x + 0.5f - h) / h, fy = (y + 0.5f - h) / h;
            return (float)Math.Sqrt(fx * fx + fy * fy);
        }

        private static (float X, float Y) Polar(float r, double angle) => (r * (float)Math.Cos(angle), r * (float)Math.Sin(angle));

        private static void Ring(float[] a, int size, float radius, float halfWidth)
        {
            float px = 2f / size; // one pixel in radius units: anti-aliasing width
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Math.Abs(Radius(x, y, size) - radius) - halfWidth;
                    Put(a, y * size + x, Cover(d, px));
                }
        }

        private static void Dot(float[] a, int size, (float X, float Y) c, float radius) => Line(a, size, c, c, radius);

        /// <summary>A stroke from p to q, <paramref name="halfWidth"/> thick on each side (radius units), round ends.</summary>
        private static void Line(float[] a, int size, (float X, float Y) p, (float X, float Y) q, float halfWidth)
        {
            float h = size / 2f, px = 2f / size, pad = halfWidth + px;
            int x0 = Math.Max(0, (int)((Math.Min(p.X, q.X) - pad + 1f) * h) - 1), x1 = Math.Min(size - 1, (int)((Math.Max(p.X, q.X) + pad + 1f) * h) + 1);
            int y0 = Math.Max(0, (int)((Math.Min(p.Y, q.Y) - pad + 1f) * h) - 1), y1 = Math.Min(size - 1, (int)((Math.Max(p.Y, q.Y) + pad + 1f) * h) + 1);
            float dx = q.X - p.X, dy = q.Y - p.Y, len2 = dx * dx + dy * dy;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float fx = (x + 0.5f - h) / h, fy = (y + 0.5f - h) / h;
                    float t = len2 < 1e-12f ? 0f : Math.Clamp(((fx - p.X) * dx + (fy - p.Y) * dy) / len2, 0f, 1f);
                    float ex = fx - (p.X + t * dx), ey = fy - (p.Y + t * dy);
                    Put(a, y * size + x, Cover((float)Math.Sqrt(ex * ex + ey * ey) - halfWidth, px));
                }
        }

        /// <summary>Coverage of a pixel whose centre is <paramref name="d"/> outside a shape's edge (negative: inside).</summary>
        private static float Cover(float d, float px) => Math.Clamp(0.5f - d / px, 0f, 1f);

        private static void Put(float[] a, int i, float v) { if (v > a[i]) a[i] = v; }

        private static byte[] ToBytes(float[] a, int size, float clear)
        {
            var b = new byte[a.Length];
            float px = 2f / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float v = a[i] * Math.Clamp((Radius(x, y, size) - clear) / px, 0f, 1f);
                    b[i] = (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
                }
            return b;
        }
    }
}
