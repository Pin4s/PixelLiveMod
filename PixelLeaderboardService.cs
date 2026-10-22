using System;
using System.Collections.Generic;

// Session-only accounting. Unity rendering and TikTok command parsing are separate.
internal sealed class PixelParticipant
{
    internal readonly string Id;
    internal string Name;
    internal long Pixels;
    internal long ReachedOrder;

    internal PixelParticipant(string id, string name) { Id = id; Name = name; }
}

internal sealed class PixelLeaderboardService
{
    private readonly Dictionary<string, PixelParticipant> _participants =
        new Dictionary<string, PixelParticipant>(StringComparer.Ordinal);
    private readonly List<PixelParticipant> _ranked = new List<PixelParticipant>();
    private long _order;
    internal long Version { get; private set; }
    internal event Action? ResetPerformed;

    internal void ResetLeaderboard()
    {
        foreach (PixelParticipant participant in _participants.Values)
        {
            participant.Pixels = 0;
            participant.ReachedOrder = 0;
        }
        _participants.Clear();
        _ranked.Clear();
        _order = 0;
        Version++;
        ResetPerformed?.Invoke();
    }

    internal PixelParticipant Register(string stableId, string publicName)
    {
        if (string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(publicName))
            throw new ArgumentException("Stable viewer ID and public name are required");
        if (!_participants.TryGetValue(stableId, out PixelParticipant? participant))
        {
            participant = new PixelParticipant(stableId, publicName);
            _participants.Add(stableId, participant);
        }
        else if (participant.Name != publicName)
        {
            participant.Name = publicName;
            Version++;
        }
        return participant;
    }

    // Called only after the native BreakTile has made a nonempty tile empty.
    internal bool RecordTransition(PixelParticipant? owner, int oldType, int newType)
    {
        if (owner == null || oldType <= 0 || newType != 0 ||
            !_participants.TryGetValue(owner.Id, out PixelParticipant? current) ||
            !ReferenceEquals(current, owner)) return false;
        owner.Pixels++;
        owner.ReachedOrder = ++_order;
        Version++;
        return true;
    }

    // Sort only when the UI refreshes, never in the destruction path.
    internal IReadOnlyList<PixelParticipant> Top(int maxVisible)
    {
        _ranked.Clear();
        foreach (PixelParticipant participant in _participants.Values)
            if (participant.Pixels > 0) _ranked.Add(participant);
        _ranked.Sort((left, right) =>
        {
            int score = right.Pixels.CompareTo(left.Pixels);
            if (score != 0) return score;
            int reached = left.ReachedOrder.CompareTo(right.ReachedOrder);
            return reached != 0 ? reached : StringComparer.Ordinal.Compare(left.Id, right.Id);
        });
        if (_ranked.Count > maxVisible) _ranked.RemoveRange(maxVisible, _ranked.Count - maxVisible);
        return _ranked;
    }
}

// The native ending callback can repeat; the same EndingManager instance represents one win.
internal sealed class PixelLeaderboardLifecycle
{
    private readonly PixelLeaderboardService _leaderboard;
    private object? _lastWinningEnding;

    internal PixelLeaderboardLifecycle(PixelLeaderboardService leaderboard)
    { _leaderboard = leaderboard; }

    internal bool OnVictory(object ending)
    {
        if (ReferenceEquals(_lastWinningEnding, ending)) return false;
        _lastWinningEnding = ending;
        _leaderboard.ResetLeaderboard();
        return true;
    }

    internal void OnConfirmedReset()
    {
        _lastWinningEnding = null;
        _leaderboard.ResetLeaderboard();
    }
}

internal static class PixelAttributionContext
{
    [ThreadStatic] private static PixelParticipant? _current;
    internal static PixelParticipant? Current => _current;

    internal static Scope Begin(PixelParticipant? owner)
    {
        PixelParticipant? previous = _current;
        _current = owner;
        return new Scope(previous);
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly PixelParticipant? _previous;
        internal Scope(PixelParticipant? previous) { _previous = previous; }
        public void Dispose() { _current = _previous; }
    }
}
