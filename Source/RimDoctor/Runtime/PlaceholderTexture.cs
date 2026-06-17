using System;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Generates and caches the magenta/checkerboard placeholder that RimDoctor
    /// substitutes for any texture that fails to load. Using an obvious "missing
    /// texture" pattern (not a blank or transparent quad) makes broken content
    /// visible at a glance instead of silently invisible.
    ///
    /// Unity requires Texture2D creation on the main thread; ContentFinder.Get and
    /// GUI.DrawTexture both run there, so lazy creation on first substitution is
    /// safe. If creation ever fails we return null and let the original (null)
    /// behaviour stand — never throw.
    /// </summary>
    [StaticConstructorOnStartup] // creates the cached Texture2D on the main thread, post-load
    public static class PlaceholderTexture
    {
        private static Texture2D cached;

        public const int Size = 64;      // texture dimensions
        private const int Tile = 8;      // checkerboard tile size in px

        static PlaceholderTexture()
        {
            // Eagerly build on the main thread during startup so later draw-time
            // calls just return the cache (and to satisfy RimWorld's rule that
            // Texture2D assets are created on the main thread).
            try { Build(); } catch (Exception e) { RDLog.Exception("Placeholder static init failed", e); }
        }

        /// <summary>
        /// The shared placeholder texture. Returns the cached one if built, else
        /// builds it — but ONLY on the main thread (never off-thread, where
        /// Texture2D creation is illegal). May return null; callers must null-check.
        /// </summary>
        public static Texture2D Get()
        {
            if (cached != null)
                return cached;
            if (!UnityData.IsInMainThread)
                return null; // cannot safely create a texture off the main thread
            return Build();
        }

        private static Texture2D Build()
        {
            if (cached != null)
                return cached;
            try
            {
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "RimDoctor_MissingTexturePlaceholder",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };

                var magenta = new Color32(255, 0, 220, 255);
                var dark = new Color32(40, 0, 35, 255);
                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        bool on = ((x / Tile) + (y / Tile)) % 2 == 0;
                        pixels[y * Size + x] = on ? magenta : dark;
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                cached = tex;
                return cached;
            }
            catch (Exception e)
            {
                RDLog.Exception("Failed to generate placeholder texture", e);
                return null;
            }
        }
    }
}
