using System;
using System.Collections.Generic;

// The feed's lifetime and counting rules are independent of Unity rendering.
internal sealed class GiftFeedState
{
    internal sealed class Entry
    {
        internal readonly string ViewerId;
        internal readonly string GiftId;
        internal string ViewerName;
        internal string GiftName;
        internal string BallType;
        internal int Gifts;
        internal int Balls;
        internal readonly HashSet<string> CountedBatches = new HashSet<string>(StringComparer.Ordinal);
        internal float LastUpdate;
        internal readonly float BornAt;
        internal float Position;
        internal float Opacity;

        internal Entry(string viewerId, string giftId, string viewerName, string giftName,
            string ballType, float now)
        {
            ViewerId = viewerId; GiftId = giftId; ViewerName = viewerName;
            GiftName = giftName; BallType = ballType; LastUpdate = now; BornAt = now;
            Position = -1f;
        }
    }

    private readonly List<Entry> _entries = new List<Entry>();
    internal IReadOnlyList<Entry> Entries => _entries;
    internal readonly float Duration;
    internal readonly float FadeIn;
    internal readonly float FadeOut;
    internal readonly int MaxVisible;

    internal GiftFeedState(float duration, float fadeIn, float fadeOut, int maxVisible)
    {
        Duration = Math.Max(0.1f, duration);
        FadeIn = Math.Max(0f, fadeIn);
        FadeOut = Math.Max(0f, fadeOut);
        MaxVisible = Math.Max(1, maxVisible);
    }

    // Called only for an accepted command after BallSpawnService has finished it.
    internal Entry? Apply(string batchId, string viewerId, string viewerName,
        string giftId, string giftName, int giftUnits, string ballType,
        int created, float now)
    {
        if (created <= 0 || giftUnits <= 0 || string.IsNullOrEmpty(batchId) ||
            string.IsNullOrEmpty(viewerId) || string.IsNullOrEmpty(giftId)) return null;

        Entry? entry = null;
        foreach (Entry candidate in _entries)
            if (candidate.ViewerId == viewerId && candidate.GiftId == giftId)
            { entry = candidate; break; }
        if (entry == null)
        {
            if (_entries.Count >= MaxVisible) _entries.RemoveAt(0);
            entry = new Entry(viewerId, giftId, viewerName, giftName, ballType, now);
            entry.Position = MaxVisible - 1 + 0.2f;
            _entries.Add(entry);
        }
        entry.ViewerName = viewerName;
        entry.GiftName = giftName;
        entry.BallType = ballType;
        if (entry.CountedBatches.Add(batchId))
        {
            entry.Gifts += giftUnits;
        }
        entry.Balls += created;
        entry.LastUpdate = now;
        return entry;
    }

    internal void Tick(float now, float delta)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            float age = Math.Max(0f, now - entry.LastUpdate);
            if (age >= Duration + FadeOut)
            { _entries.RemoveAt(i); continue; }
            float entering = FadeIn <= 0f ? 1f : Math.Min(1f, Math.Max(0f, now - entry.BornAt) / FadeIn);
            float leaving = age <= Duration || FadeOut <= 0f ? 1f :
                Math.Max(0f, 1f - (age - Duration) / FadeOut);
            entry.Opacity = Math.Min(entering, leaving);
        }
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            // Anchor newest at the bottom of the compact top-right feed region.
            // Older entries slide upward as a new line arrives.
            float target = MaxVisible - _entries.Count + i;
            // Time based easing keeps rearrangement stable at different frame rates.
            entry.Position += (target - entry.Position) * (1f - (float)Math.Exp(-Math.Max(0f, delta) * 14f));
        }
    }
}
