using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Translation helpers. All user-facing strings flow through these so they
    /// stay translatable (Languages/English/Keyed) but never render as a raw
    /// "RimDoctor.Foo.Bar" key if a translation is missing — we fall back to a
    /// sensible English default instead.
    /// </summary>
    public static class TranslateExtensions
    {
        public static string TranslateSafe(this string key, string fallback)
        {
            if (key.TryTranslate(out var result))
                return result;
            return fallback;
        }
    }
}
