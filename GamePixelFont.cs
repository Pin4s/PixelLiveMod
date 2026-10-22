using System;
using BepInEx.Logging;
using TMPro;
using UnityEngine;

// One font asset for the feed and podium; game-owned assets are never destroyed.
internal static class GamePixelFont
{
    private static TMP_FontAsset? _font;
    private static bool _owned;
    private static int _users;

    internal static TMP_FontAsset? Acquire(ManualLogSource log)
    {
        if (_users == 0) _font = Find(log);
        _users++;
        return _font;
    }

    internal static void Release()
    {
        if (_users == 0 || --_users != 0) return;
        if (_owned && _font != null) UnityEngine.Object.Destroy(_font);
        _font = null;
        _owned = false;
    }

    private static TMP_FontAsset? Find(ManualLogSource log)
    {
        TMP_FontAsset? fallback = null;
        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null) continue;
            string name = font.name.ToLowerInvariant();
            if (name.Contains("perfect dos vga 437")) return font;
            if ((name.Contains("pixel") && !name.Contains("glow")) ||
                name.Contains("bitmap") || name.Contains("retro")) fallback = font;
            if (fallback == null && !name.Contains("liberation")) fallback = font;
        }
        foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
        {
            if (font == null || !font.name.ToLowerInvariant().Contains("perfect dos vga 437")) continue;
            try
            {
                TMP_FontAsset converted = TMP_FontAsset.CreateFontAsset(font);
                _owned = true;
                return converted;
            }
            catch (Exception error) { log.LogWarning($"[UI] Could not reuse game font: {error.Message}"); }
        }
        return fallback ?? TMP_Settings.defaultFontAsset;
    }
}
