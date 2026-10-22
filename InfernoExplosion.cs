using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine;

// One scene-local detonation. The cells are sorted by actual distance from the
// contact point, so the native damage progresses radially rather than by rows.
public sealed class InfernoExplosion : MonoBehaviour
{
    private const int MaxActiveExplosions = 2;
    private const int MaxPendingExplosions = 20;
    private const double MaxProcessingMillisecondsPerFrame = 8.0;
    private static readonly List<InfernoExplosion> Pending = new List<InfernoExplosion>();
    private static int _activeExplosions;

    private WorldManager? _world;
    private ManualLogSource? _log;
    private PixelParticipant? _owner;
    private InfernoParameters _parameters;
    private Vector2 _impact;
    private CellOffset[]? _cells;
    private InfernoExplosionVFX? _vfx;
    private bool _active;
    private bool _complete;
    private float _elapsed;
    private float _startedRealtime;
    private float _cleanupAt;
    private int _index;
    private int _frames;
    private int _damaged;
    private int _destroyed;
    private int _alreadyEmpty;
    private int _protected;
    private int _outside;
    private int _survivors;
    private int _chunkRefreshes;
    private double _totalProcessingMs;
    private double _slowestFrameMs;
    private readonly HashSet<int> _dirtyPhysics = new HashSet<int>();
    private readonly HashSet<int> _dirtyVisual = new HashSet<int>();
    private readonly Stopwatch _frameWatch = new Stopwatch();

    public static bool IsOrdinaryDestructible(Tile tile) =>
        tile.currentHealth > 0 && ((tile.type >= 1 && tile.type <= 7) || tile.type == 66);

    internal static bool TryCreate(WorldManager world, Vector2 impact,
        InfernoParameters parameters, ManualLogSource log, PixelParticipant? owner, out string reason)
    {
        reason = "";
        if (world == null || world.worldTiles == null || world.physicsChunks == null ||
            ChunkManager.Instance == null || world.isMainMenu ||
            Mathf.Abs(impact.x) > world.playableSize * 0.5f ||
            Mathf.Abs(impact.y) > world.playableSize * 0.5f)
        {
            reason = "playable world, chunks or impact position are not ready";
            return false;
        }

        if (_activeExplosions + Pending.Count >= MaxActiveExplosions + MaxPendingExplosions)
        {
            reason = $"detonation capacity reached: {_activeExplosions} active, " +
                $"{Pending.Count} pending (limits {MaxActiveExplosions}+{MaxPendingExplosions})";
            return false;
        }

        GameObject? effect = null;
        try
        {
            effect = new GameObject("PixelLiveMod Inferno Explosion");
            effect.transform.position = new Vector3(impact.x, impact.y, 0f);
            InfernoExplosion explosion = effect.AddComponent<InfernoExplosion>();
            explosion._world = world;
            explosion._log = log;
            explosion._owner = owner;
            explosion._parameters = parameters;
            explosion._impact = impact;
            Pending.Add(explosion);
            log.LogInfo($"Inferno detonation queued: impact={impact}, radius={parameters.Radius} cells, " +
                $"damage={parameters.Damage}, tiles/frame={parameters.TilesPerFrame}, " +
                $"active={_activeExplosions}, pending={Pending.Count}");
            return true;
        }
        catch (Exception exception)
        {
            if (effect != null)
                Destroy(effect);
            reason = exception.ToString();
            return false;
        }
    }

    private void Update()
    {
        if (_world == null || WorldManager.Instance != _world || _world.worldTiles == null)
        {
            Destroy(gameObject); // Scene changed or world unloaded.
            return;
        }

        try
        {
            if (!_active)
            {
                if (_activeExplosions >= MaxActiveExplosions || Pending.Count == 0 ||
                    Pending[0] != this)
                    return;

                Pending.RemoveAt(0);
                Activate();
            }

            if (_complete)
            {
                UpdateVfx(1f, Time.time - (_cleanupAt - 2.5f));
                if (Time.time >= _cleanupAt)
                    Destroy(gameObject);
                return;
            }

            ProcessFrame();
        }
        catch (Exception exception)
        {
            _log?.LogError($"Inferno detonation aborted safely after {_index} cells: {exception}");
            Destroy(gameObject);
        }
    }

    private void Activate()
    {
        _active = true;
        _activeExplosions++;
        _cells = BuildRadialCells(_parameters.Radius, _impact, _world!.worldSize / 2);
        _startedRealtime = Time.realtimeSinceStartup;
        _vfx = gameObject.AddComponent<InfernoExplosionVFX>();
        _vfx.Initialize(_parameters, _log!);
        _log!.LogInfo($"Inferno detonation started: contact={_impact}, " +
            $"circle candidates={_cells.Length}, radius={_parameters.Radius} world cells; " +
            $"physics chunk size={_world.physicsChunkSize}, visual chunk size={_world.visualChunkSize}");
    }

    private void ProcessFrame()
    {
        WorldManager world = _world!;
        CellOffset[] cells = _cells!;
        _elapsed += Time.deltaTime;
        float timeProgress = Mathf.Clamp01(_elapsed / _parameters.Duration);
        float allowedDistanceSquared = _parameters.Radius * timeProgress;
        allowedDistanceSquared *= allowedDistanceSquared;
        int budget = _parameters.TilesPerFrame;
        int processed = 0;
        _frameWatch.Restart();

        while (processed < budget && _index < cells.Length &&
            cells[_index].DistanceSquared <= allowedDistanceSquared + 0.0001f)
        {
            CellOffset cell = cells[_index++];
            processed++;
            ProcessCell(world, cell.X, cell.Y);
            if ((processed & 15) == 0 &&
                _frameWatch.Elapsed.TotalMilliseconds >= MaxProcessingMillisecondsPerFrame)
                break;
        }

        FlushDirtyChunks(world);
        _frameWatch.Stop();
        double milliseconds = _frameWatch.Elapsed.TotalMilliseconds;
        _totalProcessingMs += milliseconds;
        if (milliseconds > _slowestFrameMs)
            _slowestFrameMs = milliseconds;
        _frames++;

        float reached = _index > 0
            ? Mathf.Clamp01(Mathf.Sqrt(cells[_index - 1].DistanceSquared) / _parameters.Radius)
            : 0f;
        UpdateVfx(reached, _elapsed);

        if (_index != cells.Length)
            return;

        _complete = true;
        _cleanupAt = Time.time + 2.5f;
        _log!.LogInfo($"Inferno detonation complete: radius={_parameters.Radius}, " +
            $"circle candidates={cells.Length}, native damage calls={_damaged}, destroyed={_destroyed}, " +
            $"survived damage={_survivors}, already empty={_alreadyEmpty}, " +
            $"protected/special={_protected}, outside playable/bounds={_outside}, " +
            $"chunk refreshes={_chunkRefreshes}, frames={_frames}, " +
            $"wall={Time.realtimeSinceStartup - _startedRealtime:F3}s, " +
            $"processing sum={_totalProcessingMs:F2}ms, slowest frame={_slowestFrameMs:F2}ms");
    }

    private void UpdateVfx(float progress, float elapsed)
    {
        if (_vfx == null)
            return;
        try
        {
            _vfx.SetProgress(progress, elapsed);
        }
        catch (Exception exception)
        {
            _log?.LogWarning($"Inferno VFX disabled after an error; damage continues: {exception}");
            Destroy(_vfx);
            _vfx = null;
        }
    }

    private void ProcessCell(WorldManager world, int x, int y)
    {
        Tile[,] tiles = world.worldTiles;
        int half = world.worldSize / 2;
        float playableHalf = world.playableSize * 0.5f;
        if (x < 1 || y < 1 || x >= tiles.GetLength(0) - 1 ||
            y >= tiles.GetLength(1) - 1 ||
            Mathf.Abs(x - half + 0.5f) > playableHalf ||
            Mathf.Abs(y - half + 0.5f) > playableHalf)
        {
            _outside++;
            return;
        }

        Tile tile = tiles[x, y];
        if (tile.type < 1)
        {
            _alreadyEmpty++;
            return;
        }

        // Native collision rejects 1000 and requires a Golden Ball for 12345.
        // 600/700 award Golden/upgrade progress. Only normal terrain (1..7)
        // and the normal poisoned form (66) are eligible for this mod's blast.
        if (!IsOrdinaryDestructible(tile))
        {
            _protected++;
            return;
        }

        using (PixelAttributionContext.Begin(_owner))
            world.DamageTile(x, y, _parameters.Damage);
        _damaged++;
        // Tile is a value type; reread the array after the native mutation.
        if (tiles[x, y].type == 0)
        {
            _destroyed++;
            MarkDirty(world, x, y);
        }
        else
        {
            _survivors++;
            MarkDirty(world, x, y);
        }
    }

    private void MarkDirty(WorldManager world, int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= world.worldSize || ny >= world.worldSize)
                    continue;

                int physicsX = nx / world.physicsChunkSize;
                int physicsY = ny / world.physicsChunkSize;
                _dirtyPhysics.Add(physicsX * world.physicsChunks.GetLength(1) + physicsY);
                if (!world.isUsingComputeShader && world.visualChunks != null)
                {
                    int visualX = nx / world.visualChunkSize;
                    int visualY = ny / world.visualChunkSize;
                    _dirtyVisual.Add(visualX * world.visualChunks.GetLength(1) + visualY);
                }
            }
        }
    }

    private void FlushDirtyChunks(WorldManager world)
    {
        ChunkManager manager = ChunkManager.Instance;
        int physicsHeight = world.physicsChunks.GetLength(1);
        while (_dirtyPhysics.Count > 0)
        {
            int encoded = First(_dirtyPhysics);
            int x = encoded / physicsHeight;
            int y = encoded % physicsHeight;
            // The array holds chunk DATA, not the live Chunk component. The
            // native manager owns active objects and refreshes a 3x3 area.
            manager.UpdatePhysicsChunk(new Vector2Int(x, y));
            _chunkRefreshes++;
            RemoveCovered(_dirtyPhysics, x, y, physicsHeight);
        }

        if (world.visualChunks == null)
            return;
        int visualHeight = world.visualChunks.GetLength(1);
        while (_dirtyVisual.Count > 0)
        {
            int encoded = First(_dirtyVisual);
            int x = encoded / visualHeight;
            int y = encoded % visualHeight;
            manager.UpdateVisualChunk(new Vector2Int(x, y));
            _chunkRefreshes++;
            RemoveCovered(_dirtyVisual, x, y, visualHeight);
        }
    }

    private static int First(HashSet<int> items)
    {
        foreach (int item in items)
            return item;
        throw new InvalidOperationException("dirty chunk set unexpectedly empty");
    }

    private static void RemoveCovered(HashSet<int> items, int centerX, int centerY, int height)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (centerX + dx >= 0 && centerY + dy >= 0 && centerY + dy < height)
                    items.Remove((centerX + dx) * height + centerY + dy);
            }
        }
    }

    private static CellOffset[] BuildRadialCells(int radius, Vector2 impact, int halfWorld)
    {
        int centerX = Mathf.FloorToInt(impact.x + halfWorld);
        int centerY = Mathf.FloorToInt(impact.y + halfWorld);
        int reach = radius + 1;
        float radiusSquared = radius * radius;
        List<CellOffset> cells = new List<CellOffset>(Mathf.CeilToInt(Mathf.PI * radiusSquared) + 16);
        for (int dx = -reach; dx <= reach; dx++)
        {
            int x = centerX + dx;
            float worldX = x - halfWorld + 0.5f;
            float distanceX = worldX - impact.x;
            for (int dy = -reach; dy <= reach; dy++)
            {
                int y = centerY + dy;
                float worldY = y - halfWorld + 0.5f;
                float distanceY = worldY - impact.y;
                float distanceSquared = distanceX * distanceX + distanceY * distanceY;
                if (distanceSquared <= radiusSquared)
                    cells.Add(new CellOffset(x, y, distanceSquared));
            }
        }

        CellOffset[] ordered = cells.ToArray();
        Array.Sort(ordered, (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        return ordered;
    }

    private void OnDestroy()
    {
        Pending.Remove(this);
        if (_active)
        {
            _active = false;
            _activeExplosions = Math.Max(0, _activeExplosions - 1);
        }

        if (!_complete && _log != null)
            _log.LogWarning($"Inferno detonation cancelled: processed {_index}/{_cells?.Length ?? 0} cells; " +
                "scene changed or operation aborted");
    }

    private readonly struct CellOffset
    {
        public readonly int X;
        public readonly int Y;
        public readonly float DistanceSquared;

        public CellOffset(int x, int y, float distanceSquared)
        {
            X = x;
            Y = y;
            DistanceSquared = distanceSquared;
        }
    }
}
