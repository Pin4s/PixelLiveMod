using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

// All members of this service are called from the Unity main thread. It knows
// nothing about hotkeys or where a future request originates.
public sealed class BallSpawnService
{
    public const int MaxBallsPerCommand = 20;
    private const int MaxBallsPerFrame = 5;
    private const int MaxQueuedBalls = 100;
    private const int MaxLaunchOffsetTiles = 4;

    private readonly MonoBehaviour _host;
    private readonly ManualLogSource _log;
    private readonly int _mainThreadId;
    private readonly Queue<SpawnRequest> _queue = new Queue<SpawnRequest>();
    private int _queuedBalls;
    private int _nextRequestId;

    public BallSpawnService(MonoBehaviour host, ManualLogSource log)
    {
        _host = host;
        _log = log;
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    // Returns true only when the whole command was accepted into the queue.
    // Completion, including partial failure, is reported in the log.
    public bool SpawnBalls(UpgradeManager.BallColor color, int quantity, Vector3 worldPosition)
        => Enqueue(color, quantity, worldPosition, false, default);

    // A separate command kind leaves the game's BallColor enum unchanged.
    public bool SpawnInfernoBalls(int quantity, Vector3 worldPosition, InfernoParameters parameters)
        => Enqueue(UpgradeManager.BallColor.Orange, quantity, worldPosition, true, parameters);

    private bool Enqueue(UpgradeManager.BallColor color, int quantity,
        Vector3 worldPosition, bool inferno, InfernoParameters parameters)
    {
        string typeName = inferno ? "Inferno" : color.ToString();
        if (!IsMainThread())
        {
            _log.LogError($"Spawn command rejected: {typeName} x{quantity}; must be called on Unity main thread");
            return false;
        }

        if (!IsSupportedColor(color))
        {
            _log.LogWarning($"Spawn command rejected: unsupported color {color}; requested={quantity}, created=0");
            return false;
        }

        if (quantity < 1 || quantity > MaxBallsPerCommand)
        {
            _log.LogWarning($"Spawn command rejected: {typeName} requested={quantity}, created=0; " +
                $"valid range is 1..{MaxBallsPerCommand}");
            return false;
        }

        if (float.IsNaN(worldPosition.x) || float.IsNaN(worldPosition.y) ||
            float.IsInfinity(worldPosition.x) || float.IsInfinity(worldPosition.y))
        {
            _log.LogWarning($"Spawn command rejected: {typeName} requested={quantity}, created=0; " +
                $"invalid world position {worldPosition}");
            return false;
        }

        worldPosition.z = 0f;

        if (_queuedBalls + quantity > MaxQueuedBalls)
        {
            _log.LogWarning($"Spawn command rejected: {typeName} requested={quantity}, created=0; " +
                $"queue would exceed {MaxQueuedBalls} balls (currently {_queuedBalls})");
            return false;
        }

        SpawnRequest request = new SpawnRequest(++_nextRequestId, color, quantity,
            worldPosition, inferno, parameters);
        _queue.Enqueue(request);
        _queuedBalls += quantity;
        _log.LogInfo($"Spawn command #{request.Id} queued: {typeName}, requested={quantity}, " +
            $"captured world position={worldPosition}, " +
            $"queued balls={_queuedBalls}");
        return true;
    }

    public void ProcessFrame()
    {
        if (_queue.Count == 0)
            return;

        if (!IsMainThread())
        {
            _log.LogError("Spawn queue processing refused: not on Unity main thread");
            return;
        }

        int frameBudget = MaxBallsPerFrame;
        while (frameBudget > 0 && _queue.Count > 0)
        {
            SpawnRequest request = _queue.Peek();
            if (!TryResolveContext(request.Color, out SpawnContext? context, out string reason) || context == null)
            {
                FailRemaining(request, reason);
                FinishRequest(request);
                continue;
            }

            if (!request.PrefabLogged)
            {
                _log.LogInfo($"Spawn command #{request.Id}: prefab[{context.Index}]={context.Prefab.name}, " +
                    $"type={(request.Inferno ? "Inferno" : request.Color.ToString())}, " +
                    $"serialized button force={context.ButtonForce}");
                request.PrefabLogged = true;
            }

            while (frameBudget > 0 && request.Remaining > 0)
            {
                int ordinal = request.Requested - request.Remaining;
                bool success = TrySpawnOne(context, request.Id, request.WorldPosition,
                    ordinal, request.Inferno, request.InfernoParameters, out string result);
                request.Remaining--;
                _queuedBalls--;
                frameBudget--;

                if (success)
                {
                    request.Created++;
                    _log.LogInfo($"Spawn command #{request.Id}: ball {request.Created}/{request.Requested} created; {result}");
                }
                else
                {
                    _log.LogError($"Spawn command #{request.Id}: instantiation/physics failed at ball " +
                        $"{ordinal + 1}/{request.Requested}: {result}");
                    FailRemaining(request, "stopped after a spawn failure");
                }
            }

            if (request.Remaining == 0)
                FinishRequest(request);
        }
    }

    private void FailRemaining(SpawnRequest request, string reason)
    {
        _log.LogWarning($"Spawn command #{request.Id}: {request.Remaining} uncreated ball(s): {reason}");
        _queuedBalls -= request.Remaining;
        request.Remaining = 0;
    }

    private void FinishRequest(SpawnRequest request)
    {
        _queue.Dequeue();
        _log.LogInfo($"Spawn command #{request.Id} finished: type={(request.Inferno ? "Inferno" : request.Color.ToString())}, " +
            $"requested={request.Requested}, created={request.Created}, " +
            $"not created={request.Requested - request.Created}");
    }

    private bool TryResolveContext(
        UpgradeManager.BallColor color, out SpawnContext? context, out string reason)
    {
        context = null;
        reason = "";
        try
        {
            BallShooting shooting = BallShooting.Instance;
            WorldManager world = WorldManager.Instance;
            BallManager manager = BallManager.Instance;
            BallStats stats = BallStats.Instance;
            PointsManager points = PointsManager.Instance;
            if (shooting == null || !shooting.canShootBalls || world == null ||
                world.isMainMenu || world.worldTiles == null || manager == null ||
                manager.ballList == null || stats == null || points == null ||
                points.isMainMenu || Time.timeScale <= 0f)
            {
                reason = "playable scene or required managers are not ready";
                return false;
            }

            BallData data = GetBallData(stats, color);
            if (data == null || data.speedMult <= 0f)
            {
                reason = $"{color} stats are missing or speed multiplier is invalid";
                return false;
            }

            GameObject[] prefabs = shooting.balls;
            if (prefabs == null)
            {
                reason = "BallShooting.balls is null";
                return false;
            }

            GameObject? prefab = null;
            int prefabIndex = -1;
            for (int index = 0; index < prefabs.Length; index++)
            {
                GameObject? candidate = prefabs[index];
                Ball? ball = candidate != null ? candidate.GetComponent<Ball>() : null;
                if (ball != null && ball.ballColor == color)
                {
                    prefab = candidate;
                    prefabIndex = index;
                    break;
                }
            }

            if (prefab == null || prefab.GetComponent<Rigidbody2D>() == null ||
                prefab.GetComponent<Collider2D>() == null ||
                prefab.GetComponent<PhysicsAgent>() == null ||
                prefab.GetComponent<ChunkAgent>() == null)
            {
                reason = $"{color} prefab is absent or lacks original physics/collision components";
                return false;
            }

            if (color == UpgradeManager.BallColor.Golden &&
                (prefab.GetComponent<GoldenBall>() == null ||
                 prefab.GetComponent<TrailRenderer>() == null ||
                 prefab.GetComponentInChildren<ParticleSystem>() == null ||
                 GoldenPixelManager.Instance == null ||
                 GoldenPixelManager.Instance.goldenPixelPositionsLeft == null ||
                 BlackoutManager.Instance == null))
            {
                reason = "Golden Ball special components or managers are not initialized";
                return false;
            }

            if (!TryGetButtonForce(color, out float buttonForce))
            {
                reason = $"no valid serialized BallButton force for {color}; no force is guessed";
                return false;
            }

            if (shooting.shootForce <= 0f)
            {
                reason = "BallShooting.shootForce is invalid";
                return false;
            }

            context = new SpawnContext(shooting, world, prefab, prefabIndex, buttonForce);
            return true;
        }
        catch (Exception exception)
        {
            reason = $"dependency check threw {exception}";
            return false;
        }
    }

    private static bool TryGetButtonForce(UpgradeManager.BallColor color, out float force)
    {
        force = 0f;
        BenchManager bench = BenchManager.Instance;
        if (bench == null || bench.ballButtons == null)
            return false;

        foreach (GameObject buttonPrefab in bench.ballButtons)
        {
            if (buttonPrefab == null)
                continue;

            BallButton button = buttonPrefab.GetComponent<BallButton>();
            if (button != null && button.ballColor == color && button.ballForce > 0f)
            {
                force = button.ballForce;
                return true;
            }
        }

        return false;
    }

    private static BallData GetBallData(BallStats stats, UpgradeManager.BallColor color)
    {
        switch (color)
        {
            case UpgradeManager.BallColor.Orange: return stats.orangeData;
            case UpgradeManager.BallColor.Red: return stats.redData;
            case UpgradeManager.BallColor.Green: return stats.greenData;
            case UpgradeManager.BallColor.Yellow: return stats.yellowData;
            case UpgradeManager.BallColor.Blue: return stats.blueData;
            case UpgradeManager.BallColor.Pink: return stats.pinkData;
            case UpgradeManager.BallColor.Gray: return stats.grayData;
            case UpgradeManager.BallColor.Golden: return stats.goldenData;
            default: throw new ArgumentOutOfRangeException(nameof(color));
        }
    }

    private static bool IsSupportedColor(UpgradeManager.BallColor color)
    {
        switch (color)
        {
            case UpgradeManager.BallColor.Orange:
            case UpgradeManager.BallColor.Red:
            case UpgradeManager.BallColor.Green:
            case UpgradeManager.BallColor.Yellow:
            case UpgradeManager.BallColor.Blue:
            case UpgradeManager.BallColor.Pink:
            case UpgradeManager.BallColor.Gray:
            case UpgradeManager.BallColor.Golden:
                return true;
            default:
                return false;
        }
    }

    private bool TrySpawnOne(SpawnContext context, int requestId, Vector3 requestedPosition,
        int ordinal, bool inferno, InfernoParameters parameters, out string result)
    {
        GameObject? created = null;
        try
        {
            if (!TryFindOpenPosition(context.World, requestedPosition, ordinal,
                out Vector3 position))
            {
                result = $"no distinct open launch position within {MaxLaunchOffsetTiles} tiles " +
                    $"of requested world position {requestedPosition}";
                return false;
            }

            Vector2 direction = FindLaunchDirection(context.World, position);
            created = UnityEngine.Object.Instantiate(
                context.Prefab, position, Quaternion.identity, context.Shooting.transform);

            Ball ball = created.GetComponent<Ball>();
            Rigidbody2D body = created.GetComponent<Rigidbody2D>();
            if (ball == null || body == null || !body.simulated ||
                created.GetComponent<Collider2D>() == null ||
                created.GetComponent<PhysicsAgent>() == null ||
                created.GetComponent<ChunkAgent>() == null)
                throw new InvalidOperationException("clone lacks active ball, physics, or collision components");

            if (inferno)
            {
                // Disable only the clone's ordinary collision damage. InfernoBall
                // owns the first valid tile impact; native Ball still manages
                // velocity and BallManager registration/removal.
                created.GetComponent<PhysicsAgent>().enabled = false;
                created.AddComponent<InfernoBall>().Initialize(parameters, _log);
            }

            float speedMultiplier = ball.GetSpeedMult();
            if (speedMultiplier <= 0f || float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier))
                throw new InvalidOperationException($"invalid speed multiplier {speedMultiplier}");

            Vector2 force = direction * context.Shooting.shootForce *
                context.ButtonForce * speedMultiplier;
            body.AddForce(force);
            _host.StartCoroutine(VerifyNativeStart(created, requestId, ordinal + 1));

            // Native Ball.Start registers the ball with BallManager; native
            // Ball.OnDestroy removes it. Do not alter recall or save lists.
            result = $"type={(inferno ? "Inferno" : ball.ballColor.ToString())}, " +
                $"requested={requestedPosition}, position={position}, " +
                $"offset={(position - requestedPosition).magnitude:F2}, " +
                $"force={force}, body simulated={body.simulated}";
            return true;
        }
        catch (Exception exception)
        {
            if (created != null)
                UnityEngine.Object.Destroy(created);
            result = exception.ToString();
            return false;
        }
    }

    private IEnumerator VerifyNativeStart(GameObject created, int requestId, int ballNumber)
    {
        yield return null;
        yield return new WaitForFixedUpdate();

        if (created == null)
            yield break; // A native special-ball collision may already destroy it.

        try
        {
            Ball ball = created.GetComponent<Ball>();
            Rigidbody2D body = created.GetComponent<Rigidbody2D>();
            BallManager manager = BallManager.Instance;
            if (ball == null || body == null || manager == null ||
                manager.ballList == null || !manager.ballList.Contains(ball))
            {
                _log.LogWarning($"Spawn command #{requestId}, ball {ballNumber}: native Ball.Start " +
                    "registration or physics component not confirmed");
            }
            else
            {
                _log.LogInfo($"Spawn command #{requestId}, ball {ballNumber}: native Ball.Start " +
                    $"registered; velocity={body.linearVelocity}");
            }
        }
        catch (Exception exception)
        {
            _log.LogError($"Spawn command #{requestId}, ball {ballNumber}: " +
                $"initialization check failed: {exception}");
        }
    }

    private static bool TryFindOpenPosition(
        WorldManager world, Vector3 requestedPosition, int ordinal, out Vector3 position)
    {
        position = Vector3.zero;
        if (!IsInsidePlayableSpace(world, requestedPosition))
            return false;

        List<Vector3> candidates = new List<Vector3>();
        if (IsOpenTile(world, requestedPosition))
            candidates.Add(requestedPosition);

        int halfWorld = world.worldSize / 2;
        int centerX = Mathf.FloorToInt(requestedPosition.x + halfWorld);
        int centerY = Mathf.FloorToInt(requestedPosition.y + halfWorld);
        for (int radius = 1; radius <= MaxLaunchOffsetTiles && candidates.Count <= ordinal; radius++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int y = centerY - radius; y <= centerY + radius; y++)
                {
                    if (Mathf.Abs(x - centerX) != radius && Mathf.Abs(y - centerY) != radius)
                        continue;

                    Vector3 candidate = new Vector3(x - halfWorld + 0.5f,
                        y - halfWorld + 0.5f, 0f);
                    if (IsOpenTile(world, candidate))
                        candidates.Add(candidate);
                }
            }
        }

        if (candidates.Count <= ordinal)
            return false;

        position = candidates[ordinal];
        return true;
    }

    private static bool IsInsidePlayableSpace(WorldManager world, Vector3 position)
    {
        float limit = world.playableSize / 2 + 10;
        if (Mathf.Abs(position.x) > limit || Mathf.Abs(position.y) > limit)
            return false;

        Tile[,] tiles = world.worldTiles;
        int halfWorld = world.worldSize / 2;
        int x = Mathf.FloorToInt(position.x + halfWorld);
        int y = Mathf.FloorToInt(position.y + halfWorld);
        return x >= 0 && x < tiles.GetLength(0) && y >= 0 && y < tiles.GetLength(1);
    }

    private static bool IsOpenTile(WorldManager world, Vector3 position)
    {
        if (!IsInsidePlayableSpace(world, position))
            return false;

        int halfWorld = world.worldSize / 2;
        int x = Mathf.FloorToInt(position.x + halfWorld);
        int y = Mathf.FloorToInt(position.y + halfWorld);
        return world.worldTiles[x, y].type < 1;
    }

    private static Vector2 FindLaunchDirection(WorldManager world, Vector3 position)
    {
        Tile[,] tiles = world.worldTiles;
        int halfWorld = world.worldSize / 2;
        int originX = Mathf.FloorToInt(position.x + halfWorld);
        int originY = Mathf.FloorToInt(position.y + halfWorld);
        float nearest = float.PositiveInfinity;
        Vector2 direction = Vector2.up;

        for (int x = Mathf.Max(0, originX - 12); x <= Mathf.Min(tiles.GetLength(0) - 1, originX + 12); x++)
        {
            for (int y = Mathf.Max(0, originY - 12); y <= Mathf.Min(tiles.GetLength(1) - 1, originY + 12); y++)
            {
                Tile tile = tiles[x, y];
                if (tile.type < 1 || tile.type >= 1000 || tile.currentHealth <= 0)
                    continue;

                Vector2 delta = new Vector2(x - halfWorld + 0.5f, y - halfWorld + 0.5f) -
                    new Vector2(position.x, position.y);
                float distanceSquared = delta.sqrMagnitude;
                if (distanceSquared < 0.01f || distanceSquared >= nearest)
                    continue;

                nearest = distanceSquared;
                direction = delta.normalized;
            }
        }

        return direction;
    }

    private bool IsMainThread() => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

    private sealed class SpawnRequest
    {
        public readonly int Id;
        public readonly UpgradeManager.BallColor Color;
        public readonly int Requested;
        public readonly Vector3 WorldPosition;
        public readonly bool Inferno;
        public readonly InfernoParameters InfernoParameters;
        public int Remaining;
        public int Created;
        public bool PrefabLogged;

        public SpawnRequest(int id, UpgradeManager.BallColor color, int quantity,
            Vector3 worldPosition, bool inferno, InfernoParameters infernoParameters)
        {
            Id = id;
            Color = color;
            Requested = quantity;
            WorldPosition = worldPosition;
            Inferno = inferno;
            InfernoParameters = infernoParameters;
            Remaining = quantity;
        }
    }

    private sealed class SpawnContext
    {
        public readonly BallShooting Shooting;
        public readonly WorldManager World;
        public readonly GameObject Prefab;
        public readonly int Index;
        public readonly float ButtonForce;

        public SpawnContext(BallShooting shooting, WorldManager world,
            GameObject prefab, int index, float buttonForce)
        {
            Shooting = shooting;
            World = world;
            Prefab = prefab;
            Index = index;
            ButtonForce = buttonForce;
        }
    }
}
