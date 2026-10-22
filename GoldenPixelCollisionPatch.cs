using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

// Extends only a Ball/PhysicsAgent's direct collision with the native Golden Pixel tile.
internal sealed class GoldenPixelCollisionPatch : IDisposable
{
    private static GoldenPixelCollisionPatch? _active;
    private readonly Harmony _harmony = new Harmony("com.pixellive.mod.golden-pixel-contact");
    private readonly GoldenPixelCompletionGate _gate = new GoldenPixelCompletionGate();
    private readonly ManualLogSource _log;
    private FieldInfo? _completionEvent;
    private bool _installed;

    internal GoldenPixelCollisionPatch(ManualLogSource log) { _log = log; }

    internal bool Install()
    {
        try
        {
            _completionEvent = AccessTools.Field(typeof(PointsManager), "OnAllPixelsCleared") ??
                throw new MissingFieldException(typeof(PointsManager).FullName, "OnAllPixelsCleared");
            MethodInfo collision = AccessTools.Method(typeof(PhysicsAgent), "OnCollisionEnter2D") ??
                throw new MissingMethodException(typeof(PhysicsAgent).FullName, "OnCollisionEnter2D");
            _active = this;
            _harmony.Patch(collision,
                prefix: new HarmonyMethod(typeof(GoldenPixelCollisionPatch), nameof(CollisionPrefix)),
                postfix: new HarmonyMethod(typeof(GoldenPixelCollisionPatch), nameof(CollisionPostfix)));
            SceneManager.sceneLoaded += OnSceneLoaded;
            _installed = true;
            _log.LogInfo("[GoldenPixel] Direct ball contact patch installed");
            return true;
        }
        catch (Exception error)
        {
            _harmony.UnpatchSelf();
            if (ReferenceEquals(_active, this)) _active = null;
            _log.LogError($"[GoldenPixel] Contact patch unavailable: {error}");
            return false;
        }
    }

    private readonly struct Contact
    {
        internal readonly WorldManager? World;
        internal readonly Tile[,]? Tiles;
        internal readonly int X;
        internal readonly int Y;
        internal Contact(WorldManager world, Tile[,] tiles, int x, int y)
        { World = world; Tiles = tiles; X = x; Y = y; }
    }

    private static void CollisionPrefix(PhysicsAgent __instance, Collision2D __0, out Contact __state)
    {
        __state = default;
        try
        {
            if (__instance.GetComponent<Ball>() == null || __0.gameObject == null ||
                !__0.gameObject.CompareTag("Tile")) return;
            PhysicsTile target = __0.gameObject.GetComponent<PhysicsTile>();
            WorldManager world = WorldManager.Instance;
            if (target == null || world == null || world.isMainMenu ||
                world.worldTiles == null || world.goldenPixelPositions == null ||
                GoldenPixelManager.Instance == null) return;
            Tile[,] tiles = world.worldTiles;
            Vector2Int cell = target.worldCoordinates;
            if ((uint)cell.x >= (uint)tiles.GetLength(0) ||
                (uint)cell.y >= (uint)tiles.GetLength(1)) return;
            Tile tile = tiles[cell.x, cell.y];
            int index = tile.currentHealth - 1;
            if ((uint)index >= (uint)world.goldenPixelPositions.Length) return;
            Vector2Int expected = world.goldenPixelPositions[index];
            if (GoldenPixelContactRules.IsGoldenTarget(true, tile.type, tile.currentHealth,
                cell.x, cell.y, world.goldenPixelPositions.Length, expected.x, expected.y))
                __state = new Contact(world, tiles, cell.x, cell.y);
        }
        catch (Exception error)
        {
            _active?._log.LogWarning($"[GoldenPixel] Contact inspection skipped: {error}");
        }
    }

    private static void CollisionPostfix(Contact __state)
    {
        GoldenPixelCollisionPatch? patch = _active;
        if (patch == null || __state.World == null || __state.Tiles == null ||
            EndingManager.ending || !ReferenceEquals(__state.World, WorldManager.Instance) ||
            !ReferenceEquals(__state.Tiles, __state.World.worldTiles)) return;
        if (!patch._gate.TryClaim()) return;

        try
        {
            WorldManager world = __state.World;
            PointsManager points = PointsManager.Instance;
            Action<int>? finish = points == null ? null :
                patch._completionEvent?.GetValue(points) as Action<int>;
            if (finish == null || !HasNativeEndingListener(finish))
            {
                patch._gate.Release();
                patch._log.LogWarning("[GoldenPixel] Native ending listener unavailable; contact skipped");
                return;
            }

            // Golden Ball may already have collected it in the original collision call.
            // Other balls use the same native damage/collection route once.
            if (__state.Tiles[__state.X, __state.Y].type == GoldenPixelContactRules.GoldenPixelType)
                world.DamageTile(__state.X, __state.Y, 1);
            if (__state.Tiles[__state.X, __state.Y].type != 0)
            {
                patch._gate.Release();
                patch._log.LogWarning("[GoldenPixel] Native collection did not complete; victory skipped");
                return;
            }

            // Breaking the last tile may have invoked this event inside DamageTile.
            if (!EndingManager.ending)
                finish(points!.totalPixelsMined);
            patch._log.LogInfo("[GoldenPixel] Direct contact completed through native ending event");
        }
        catch (Exception error)
        {
            if (!EndingManager.ending) patch._gate.Release();
            patch._log.LogError($"[GoldenPixel] Native contact completion failed: {error}");
        }
    }

    private static bool HasNativeEndingListener(Action<int> finish)
    {
        foreach (Delegate listener in finish.GetInvocationList())
            if (listener.Target is EndingManager && listener.Method.Name == "LevelCleared")
                return true;
        return false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => _gate.NewRound();

    public void Dispose()
    {
        if (_installed) SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony.UnpatchSelf();
        if (ReferenceEquals(_active, this)) _active = null;
        _installed = false;
    }
}
