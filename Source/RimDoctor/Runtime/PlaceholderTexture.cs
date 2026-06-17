using System;
using UnityEngine;

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
    public static class PlaceholderTexture
    {
        private static Texture2D cached;

        public const int Size = 64;      // texture dimensions
        private const int Tile = 8;      // checkerboard tile size in px

        /// <summary>
        /// The shared placeholder texture, created on first use. May return null
        /// if Unity isn't in a state where a texture can be created — callers must
        /// null-check and fall back gracefully.
        /// </summary>
        public static Texture2D Get()
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
