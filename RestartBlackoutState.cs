using System;

// The cover may reveal a scene only after the native world startup and play
// gate have both completed. SceneManager.sceneLoaded alone is not readiness.
internal sealed class RestartBlackoutState
{
    internal enum Stage { Hidden, Opaque, Revealing }

    internal const float RevealSeconds = 0.7f;
    internal Stage Current { get; private set; } = Stage.Hidden;
    internal float Alpha { get; private set; }
    private float _revealElapsed;

    internal void Cover()
    {
        Current = Stage.Opaque;
        Alpha = 1f;
        _revealElapsed = 0f;
    }

    internal bool RevealWhenReady(bool ready)
    {
        if (!ready || Current != Stage.Opaque) return false;
        Current = Stage.Revealing;
        return true;
    }

    internal void Advance(float unscaledSeconds)
    {
        if (Current != Stage.Revealing) return;
        _revealElapsed += Math.Max(0f, unscaledSeconds);
        float progress = Math.Min(1f, _revealElapsed / RevealSeconds);
        Alpha = 1f - progress * progress * (3f - 2f * progress);
        if (Alpha == 0f) Current = Stage.Hidden;
    }

    internal void Hide()
    {
        Current = Stage.Hidden;
        Alpha = 0f;
        _revealElapsed = 0f;
    }
}
