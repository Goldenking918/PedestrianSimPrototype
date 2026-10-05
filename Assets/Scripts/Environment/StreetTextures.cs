using UnityEngine;

/// <summary>
/// Seeded texture generators for <see cref="SymondsStreetDressing"/>. The street's surfaces and facades are drawn in code
/// rather than imported as image files, so they are identical on every run and machine and add nothing to the repository.
/// Every texture tiles seamlessly.
/// </summary>
public static class StreetTextures
{
    public enum FacadeStyle
    {
        WhiteApartments = 0, // white residential tower with dark windows and balcony slabs (most of the street)
        DarkGlass = 1,       // dark blue-black glazed office tower
        RustFins = 2,        // university building: blue glass behind rust-brown vertical fins
        PaintedConcrete = 3, // grey-blue painted tower with small punched windows
    }

    // ---- Noise helpers -----------------------------------------------------------------------------------------------

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Value noise that repeats every <paramref name="period"/> cells, so textures built from it tile.</summary>
    static float Noise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period), ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
        float bottom = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
        float top = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
        return Mathf.Lerp(bottom, top, fy);
    }

    static int Wrap(int v, int period) => ((v % period) + period) % period;

    /// <summary>Fractal noise in 0..1 over texture coordinates u, v in 0..1.</summary>
    static float Fbm(float u, float v, int baseFrequency, int octaves, int seed)
    {
        float sum = 0f, amplitude = 0.5f, norm = 0f;
        int f = baseFrequency;
        for (int o = 0; o < octaves; o++)
        {
            sum += amplitude * Noise(u * f, v * f, f, seed + o * 31);
            norm += amplitude;
            amplitude *= 0.5f;
            f *= 2;
        }
        return sum / norm;
    }

    static Texture2D Finish(string name, Color32[] pixels, int width, int height, int aniso)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = aniso,
            hideFlags = HideFlags.DontSave,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true); // build mipmaps, then free the CPU copy
        return texture;
    }

    static Color32 Grey(float v, float r = 1f, float g = 1f, float b = 1f) =>
        new Color(Mathf.Clamp01(v * r), Mathf.Clamp01(v * g), Mathf.Clamp01(v * b));

    // ---- Ground ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Worn dark asphalt like the reference photos: uneven tone at metre scale, finer grain and light aggregate chips.
    /// </summary>
    public static Texture2D Asphalt(int size, int seed)
    {
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float tone = 0.29f
                             + (Fbm(u, v, 3, 3, seed) - 0.5f) * 0.07f       // patches and wear
                             + (Fbm(u, v, 48, 2, seed + 7) - 0.5f) * 0.05f; // grain
                float chip = Hash(x, y, seed + 99);
                if (chip > 0.94f) tone += 0.09f * (chip - 0.94f) / 0.06f + 0.03f; // pale aggregate
                else if (chip < 0.05f) tone -= 0.04f;                              // pits
                pixels[y * size + x] = Grey(tone, 0.98f, 1f, 1.03f);
            }
        return Finish("Asphalt (generated)", pixels, size, size, 8);
    }

    /// <summary>
    /// Square concrete footpath slabs (<paramref name="slabsAcross"/> per tile) with slightly varied tones and dark joints.
    /// </summary>
    public static Texture2D Pavers(int size, int slabsAcross, int seed)
    {
        var pixels = new Color32[size * size];
        float slab = (float)size / slabsAcross;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int sx = (int)(x / slab), sy = (int)(y / slab);
                float lx = x - sx * slab, ly = y - sy * slab;
                bool joint = lx < 2f || ly < 2f;
                float u = (float)x / size, v = (float)y / size;
                float tone = 0.60f + (Hash(sx, sy, seed) - 0.5f) * 0.06f
                             + (Fbm(u, v, 4, 2, seed + 3) - 0.5f) * 0.08f      // weathering
                             + (Hash(x, y, seed + 5) - 0.5f) * 0.03f;          // texture
                if (joint) tone *= 0.62f;
                pixels[y * size + x] = Grey(tone, 1f, 1f, 1.02f);
            }
        return Finish("Pavers (generated)", pixels, size, size, 4);
    }

    /// <summary>Plain surface with fine speckle, for kerbs and the concrete channel (tinted by the material colour).</summary>
    public static Texture2D Speckle(int size, int seed)
    {
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float tone = 0.9f + (Fbm(u, v, 8, 3, seed) - 0.5f) * 0.18f + (Hash(x, y, seed) - 0.5f) * 0.08f;
                pixels[y * size + x] = Grey(tone);
            }
        return Finish("Speckle (generated)", pixels, size, size, 4);
    }

    // ---- Buildings ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// One structural bay of a facade (one floor high, one bay wide); the tower meshes repeat it with their UVs.
    /// </summary>
    public static Texture2D Facade(FacadeStyle style, int size, int seed)
    {
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size; // u across the bay, v up the floor
                Color c;
                switch (style)
                {
                    case FacadeStyle.WhiteApartments:
                    {
                        Color wall = new Color(0.86f, 0.86f, 0.84f);
                        Color glass = Color.Lerp(new Color(0.14f, 0.18f, 0.24f), new Color(0.32f, 0.40f, 0.48f), v);
                        c = wall;
                        if (v < 0.08f) c = new Color(0.93f, 0.93f, 0.91f);                       // balcony slab edge
                        else if (u > 0.08f && u < 0.92f && v > 0.22f && v < 0.88f) c = glass;     // window wall
                        if (u > 0.5f && v > 0.08f && v < 0.36f) c = Color.Lerp(c, new Color(0.62f, 0.66f, 0.68f), 0.75f); // glass balustrade
                        if (Mathf.Abs(u - 0.5f) < 0.015f && v > 0.22f && v < 0.88f) c = wall;     // mullion
                        break;
                    }
                    case FacadeStyle.DarkGlass:
                    {
                        c = Color.Lerp(new Color(0.06f, 0.09f, 0.14f), new Color(0.22f, 0.30f, 0.42f), v * 0.8f);
                        if (v < 0.12f) c = new Color(0.10f, 0.11f, 0.13f);                         // spandrel
                        if ((u * 4f) % 1f < 0.04f) c = new Color(0.24f, 0.26f, 0.29f);             // mullions
                        break;
                    }
                    case FacadeStyle.RustFins:
                    {
                        c = Color.Lerp(new Color(0.16f, 0.30f, 0.52f), new Color(0.40f, 0.55f, 0.75f), v);
                        if (v < 0.07f) c = new Color(0.42f, 0.21f, 0.12f);                         // horizontal rail
                        int fin = (int)(u * 7f);
                        float finU = u * 7f - fin;
                        float finLength = 0.45f + Hash(fin, 1, seed) * 0.55f;
                        float finStart = Hash(fin, 2, seed) * (1f - finLength);
                        if (finU < 0.5f && v > finStart && v < finStart + finLength)
                            c = Color.Lerp(new Color(0.40f, 0.20f, 0.11f), new Color(0.55f, 0.30f, 0.17f), Hash(fin, 3, seed));
                        break;
                    }
                    default: // PaintedConcrete
                    {
                        c = new Color(0.60f, 0.66f, 0.74f);
                        if (u > 0.30f && u < 0.62f && v > 0.32f && v < 0.78f)
                            c = Color.Lerp(new Color(0.12f, 0.14f, 0.18f), new Color(0.30f, 0.34f, 0.40f), v);
                        if (u > 0.30f && u < 0.62f && v > 0.29f && v < 0.32f) c = new Color(0.75f, 0.78f, 0.82f); // sill
                        break;
                    }
                }
                float grime = 1f + (Hash(x, y, seed) - 0.5f) * 0.04f;
                pixels[y * size + x] = (Color32)new Color(c.r * grime, c.g * grime, c.b * grime);
            }
        return Finish(style + " facade (generated)", pixels, size, size, 2);
    }

    /// <summary>
    /// Four colourful advertising posters side by side (each a quarter of the width), like the bus shelter panels in the
    /// photos. Abstract shapes only; no real brands.
    /// </summary>
    public static Texture2D Posters(int posterWidth, int posterHeight, int seed)
    {
        Color[,] palettes =
        {
            { new Color(0.92f, 0.30f, 0.60f), new Color(0.40f, 0.18f, 0.65f) },
            { new Color(0.98f, 0.62f, 0.15f), new Color(0.85f, 0.25f, 0.20f) },
            { new Color(0.15f, 0.62f, 0.38f), new Color(0.05f, 0.35f, 0.25f) },
            { new Color(0.25f, 0.55f, 0.90f), new Color(0.55f, 0.25f, 0.75f) },
        };
        int width = posterWidth * 4;
        var pixels = new Color32[width * posterHeight];
        for (int y = 0; y < posterHeight; y++)
            for (int x = 0; x < width; x++)
            {
                int p = x / posterWidth;
                float u = (x - p * posterWidth + 0.5f) / posterWidth, v = (y + 0.5f) / posterHeight;
                Color c = Color.Lerp(palettes[p, 1], palettes[p, 0], v);
                float cx = 0.35f + Hash(p, 1, seed) * 0.3f, cy = 0.55f + Hash(p, 2, seed) * 0.2f;
                float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy) * 0.36f);
                if (d < 0.28f) c = Color.Lerp(c, new Color(0.98f, 0.85f, 0.70f), 0.75f);          // figure
                if (v > 0.10f && v < 0.26f && u > 0.12f && u < 0.88f && (int)(v * 24f) % 2 == 0)
                    c = Color.white;                                                                 // headline
                if (u < 0.03f || u > 0.97f || v < 0.015f || v > 0.985f) c = new Color(0.85f, 0.87f, 0.88f); // frame
                pixels[y * width + x] = (Color32)c;
            }
        return Finish("Posters (generated)", pixels, width, posterHeight, 2);
    }
}
