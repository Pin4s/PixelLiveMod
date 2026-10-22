using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

// Observes native destruction without changing tile damage or the game's score.
internal sealed class PixelDestructionTracker : IDisposable
{
    private static PixelDestructionTracker? _active;
    private static FieldInfo? _poisonIteratorSource;
    private readonly PixelLeaderboardService _leaderboard;
    private readonly PixelLeaderboardLifecycle _lifecycle;
    private readonly ManualLogSource _log;
    private readonly Dictionary<PoisonTile, PixelParticipant> _poisonOwners =
        new Dictionary<PoisonTile, PixelParticipant>();
    private readonly Harmony _harmony = new Harmony("com.pixellive.mod.pixel-leaderboard");
    private bool _installed;

    internal PixelDestructionTracker(PixelLeaderboardService leaderboard, ManualLogSource log)
    { _leaderboard = leaderboard; _lifecycle = new PixelLeaderboardLifecycle(leaderboard); _log = log; }

    internal bool Install()
    {
        try
        {
            Type? iterator = null;
            foreach (Type nested in typeof(PoisonTile).GetNestedTypes(BindingFlags.NonPublic))
                if (nested.Name.StartsWith("<Poisoning>", StringComparison.Ordinal))
                { iterator = nested; break; }
            if (iterator == null) throw new MissingMemberException("PoisonTile.Poisoning iterator");
            _poisonIteratorSource = AccessTools.Field(iterator, "<>4__this") ??
                throw new MissingFieldException(iterator.FullName, "<>4__this");
            _active = this;
            Patch(typeof(PhysicsAgent), "OnCollisionEnter2D", nameof(BallCollisionPrefix), null, nameof(ScopeFinalizer));
            Patch(typeof(LightningBall), "StrikeTile", nameof(LightningPrefix), null, nameof(ScopeFinalizer));
            Patch(iterator, "MoveNext", nameof(PoisonMoveNextPrefix), null, nameof(ScopeFinalizer));
            Patch(typeof(PoisonTile), "StartPoisoning", nameof(PoisonStartPrefix), null, null);
            Patch(typeof(PoisonTile), "ReturnToPool", nameof(PoisonReleasePrefix), null, null);
            Patch(typeof(PoisonTile), "OnDestroy", nameof(PoisonReleasePrefix), null, null);
            Patch(typeof(WorldManager), "BreakTile", nameof(BreakPrefix), nameof(BreakPostfix), null);
            Patch(typeof(EndingManager), "LevelCleared", null, nameof(VictoryPostfix), null);
            Patch(typeof(SaveManager), "ResetAllProgress", nameof(ConfirmedResetPrefix), null, null);
            SceneManager.sceneLoaded += OnSceneLoaded;
            _installed = true;
            _log.LogInfo("[Leaderboard] Native BreakTile and source patches installed");
            return true;
        }
        catch (Exception error)
        {
            _harmony.UnpatchSelf();
            _active = null;
            _poisonOwners.Clear();
            _log.LogError($"[Leaderboard] Attribution disabled: patch installation failed: {error}");
            return false;
        }
    }

    private void Patch(Type type, string method, string? prefix, string? postfix, string? finalizer)
    {
        MethodInfo target = AccessTools.Method(type, method) ??
            throw new MissingMethodException(type.FullName, method);
        _harmony.Patch(target,
            prefix == null ? null : new HarmonyMethod(typeof(PixelDestructionTracker), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(PixelDestructionTracker), postfix),
            null,
            finalizer == null ? null : new HarmonyMethod(typeof(PixelDestructionTracker), finalizer),
            null);
    }

    private static PixelParticipant? BallOwner(Component source)
    {
        PixelBallOwner tag = source.GetComponentInParent<PixelBallOwner>();
        return tag != null ? tag.Participant : null;
    }

    private static void BallCollisionPrefix(PhysicsAgent __instance,
        out PixelAttributionContext.Scope __state)
        => __state = PixelAttributionContext.Begin(BallOwner(__instance));

    private static void LightningPrefix(LightningBall __instance,
        out PixelAttributionContext.Scope __state)
        => __state = PixelAttributionContext.Begin(BallOwner(__instance));

    private static void PoisonMoveNextPrefix(object __instance,
        out PixelAttributionContext.Scope __state)
    {
        PoisonTile? source = _poisonIteratorSource?.GetValue(__instance) as PoisonTile;
        PixelParticipant? owner = null;
        if (source != null) _active?._poisonOwners.TryGetValue(source, out owner);
        __state = PixelAttributionContext.Begin(owner);
    }

    private static Exception? ScopeFinalizer(Exception? __exception,
        PixelAttributionContext.Scope __state)
    {
        __state.Dispose();
        return __exception;
    }

    private static void PoisonStartPrefix(PoisonTile __instance)
    {
        PixelDestructionTracker? tracker = _active;
        if (tracker == null) return;
        PixelParticipant? owner = PixelAttributionContext.Current;
        if (owner == null) tracker._poisonOwners.Remove(__instance);
        else tracker._poisonOwners[__instance] = owner;
    }

    private static void PoisonReleasePrefix(PoisonTile __instance)
        => _active?._poisonOwners.Remove(__instance);

    private readonly struct BreakState
    {
        internal readonly Tile[,]? Tiles;
        internal readonly PixelParticipant? Owner;
        internal readonly int OldType;
        internal BreakState(Tile[,] tiles, PixelParticipant owner, int oldType)
        { Tiles = tiles; Owner = owner; OldType = oldType; }
    }

    private static void BreakPrefix(WorldManager __instance, int __0, int __1, out BreakState __state)
    {
        __state = default;
        PixelParticipant? owner = PixelAttributionContext.Current;
        Tile[,]? tiles = __instance.worldTiles;
        if (owner == null || __instance.isMainMenu || tiles == null ||
            (uint)__0 >= (uint)tiles.GetLength(0) || (uint)__1 >= (uint)tiles.GetLength(1)) return;
        __state = new BreakState(tiles, owner, tiles[__0, __1].type);
    }

    private static void BreakPostfix(WorldManager __instance, int __0, int __1, BreakState __state)
    {
        if (__state.Owner == null || __state.Tiles == null ||
            !ReferenceEquals(__state.Tiles, __instance.worldTiles)) return;
        _active?._leaderboard.RecordTransition(__state.Owner, __state.OldType,
            __state.Tiles[__0, __1].type);
    }

    private static void VictoryPostfix(EndingManager __instance)
    {
        PixelDestructionTracker? tracker = _active;
        if (tracker != null && tracker._lifecycle.OnVictory(__instance))
        {
            tracker._poisonOwners.Clear();
            tracker._log.LogInfo("[Leaderboard] Cleared after native victory");
        }
    }

    private static void ConfirmedResetPrefix()
    {
        PixelDestructionTracker? tracker = _active;
        if (tracker == null) return;
        tracker._lifecycle.OnConfirmedReset();
        tracker._poisonOwners.Clear();
        tracker._log.LogInfo("[Leaderboard] Cleared for confirmed Reset All Data");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => _poisonOwners.Clear();

    public void Dispose()
    {
        if (_installed) SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony.UnpatchSelf();
        _poisonOwners.Clear();
        if (ReferenceEquals(_active, this)) _active = null;
        _installed = false;
    }
}
