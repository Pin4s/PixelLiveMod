using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

// Network I/O and JSON decoding stay off the Unity thread. Only ProcessMainThread
// touches Unity, InfernoSettings, or BallSpawnService.
internal sealed class LocalSpawnBridge : IDisposable
{
    private const int MaxPendingCommands = 100;
    private readonly int _port;
    private readonly BallSpawnService _spawner;
    private readonly Func<InfernoSettings> _infernoSettings;
    private readonly ManualLogSource _log;
    private readonly ConcurrentQueue<Incoming> _incoming = new ConcurrentQueue<Incoming>();
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new Queue<string>();
    private TcpListener? _listener;
    private Thread? _thread;
    private volatile bool _stopping;
    private int _pendingCount;
    private int _connectionNumber;

    internal LocalSpawnBridge(int port, BallSpawnService spawner,
        Func<InfernoSettings> infernoSettings, ManualLogSource log)
    {
        _port = port;
        _spawner = spawner;
        _infernoSettings = infernoSettings;
        _log = log;
    }

    internal void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start(1);
            _thread = new Thread(Serve) { IsBackground = true, Name = "PixelLiveMod local bridge" };
            _thread.Start();
            _log.LogInfo($"[Bridge] Listening on 127.0.0.1:{_port}");
        }
        catch (Exception error)
        {
            _log.LogError($"[Bridge] Could not bind 127.0.0.1:{_port}: {error}");
            _listener?.Stop();
            _listener = null;
        }
    }

    private void Serve()
    {
        while (!_stopping)
        {
            try
            {
                TcpListener? listener = _listener;
                if (listener == null) return;
                if (!listener.Pending()) { Thread.Sleep(50); continue; }
                using (TcpClient client = listener.AcceptTcpClient())
                {
                    int connection = Interlocked.Increment(ref _connectionNumber);
                    client.NoDelay = true;
                    _log.LogInfo($"[Bridge] Connector connected (session {connection})");
                    HandleClient(client, connection);
                    _log.LogInfo($"[Bridge] Connector disconnected (session {connection})");
                }
            }
            catch (SocketException error)
            {
                if (!_stopping) _log.LogError($"[Bridge] Socket error: {error}");
            }
            catch (Exception error)
            {
                if (!_stopping) _log.LogError($"[Bridge] Connection error: {error}");
            }
        }
    }

    private void HandleClient(TcpClient client, int connection)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        var line = new List<byte>(256);
        var responses = new ConcurrentQueue<BridgeResponse>();
        while (!_stopping)
        {
            while (responses.TryDequeue(out BridgeResponse? response))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Serialize(response) + "\n");
                stream.Write(bytes, 0, bytes.Length);
            }
            if (!stream.DataAvailable)
            {
                if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Available == 0) break;
                Thread.Sleep(10);
                continue;
            }
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    string json = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
                    line.Clear();
                    if (json.Length == 0) continue;
                    BridgeRequest? request = Deserialize(json);
                    if (request == null)
                    {
                        responses.Enqueue(new BridgeResponse("", false, "invalid JSON command"));
                        _log.LogWarning("[Bridge] Invalid JSON command rejected");
                    }
                    else if (Interlocked.Increment(ref _pendingCount) > MaxPendingCommands)
                    {
                        Interlocked.Decrement(ref _pendingCount);
                        responses.Enqueue(new BridgeResponse(request.RequestId ?? "", false, "bridge queue full"));
                        _log.LogWarning("[Bridge] Command rejected: bridge queue full");
                    }
                    else _incoming.Enqueue(new Incoming(request, responses, connection));
                }
                else if (line.Count >= 2048)
                {
                    _log.LogWarning("[Bridge] Oversized command rejected; closing connector");
                    return;
                }
                else line.Add(buffer[i]);
            }
        }
    }

    // Called by PixelLiveMod.Update, always on Unity's main thread.
    internal void ProcessMainThread()
    {
        for (int i = 0; i < 10 && _incoming.TryDequeue(out Incoming? item); i++)
        {
            Interlocked.Decrement(ref _pendingCount);
            BridgeRequest request = item.Request;
            string id = request.RequestId ?? "";
            BridgeResponse response;
            if (item.Connection != _connectionNumber)
                response = new BridgeResponse(id, false, "stale connection");
            else if (id.Length < 1 || id.Length > 80 || !IsSafeId(id))
                response = new BridgeResponse(id, false, "invalid requestId");
            else if (!_seen.Add(id))
                response = new BridgeResponse(id, false, "duplicate requestId");
            else
            {
                _seenOrder.Enqueue(id);
                while (_seenOrder.Count > 2048) _seen.Remove(_seenOrder.Dequeue());
                response = Execute(request);
            }
            item.Responses.Enqueue(response);
            _log.LogInfo($"[Bridge] Command {id}: {(response.Accepted ? "accepted into spawn queue" : "rejected")}; {response.Reason}");
        }
    }

    private BridgeResponse Execute(BridgeRequest request)
    {
        string id = request.RequestId ?? "";
        if (request.Quantity < 1 || request.Quantity > BallSpawnService.MaxBallsPerCommand)
            return new BridgeResponse(id, false, "quantity must be 1..20");
        if (request.Type != "Orange" && request.Type != "Yellow" &&
            request.Type != "Green" && request.Type != "Pink" && request.Type != "Inferno")
            return new BridgeResponse(id, false, "unsupported ball type");

        try
        {
            if (!TryFindVisibleLaunchPosition(out Vector3 position))
                return new BridgeResponse(id, false, "no open cell near visible play-area center");
            bool accepted;
            if (request.Type == "Inferno")
            {
                if (!_infernoSettings().TrySnapshot(out InfernoParameters parameters, out string reason))
                    return new BridgeResponse(id, false, $"Inferno configuration invalid: {reason}");
                accepted = _spawner.SpawnInfernoBalls(request.Quantity, position, parameters);
            }
            else
            {
                UpgradeManager.BallColor color = (UpgradeManager.BallColor)Enum.Parse(
                    typeof(UpgradeManager.BallColor), request.Type);
                accepted = _spawner.SpawnBalls(color, request.Quantity, position);
            }
            return new BridgeResponse(id, accepted, accepted ? "queued, not yet created" : "spawn service rejected; see Unity log");
        }
        catch (Exception error)
        {
            _log.LogError($"[Bridge] Command {id} failed: {error}");
            return new BridgeResponse(id, false, "exception; see Unity log");
        }
    }

    // External commands have no pointer. Find the closest visible open cell to
    // the camera center; BallSpawnService still performs final scene/position checks.
    private static bool TryFindVisibleLaunchPosition(out Vector3 position)
    {
        position = Vector3.zero;
        Camera camera = Camera.main;
        WorldManager world = WorldManager.Instance;
        if (camera == null || world == null || world.worldTiles == null || world.isMainMenu)
            return false;

        Vector3 center = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0f));
        Vector3 lower = camera.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 upper = camera.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));
        int halfWorld = world.worldSize / 2;
        int centerX = Mathf.FloorToInt(center.x + halfWorld);
        int centerY = Mathf.FloorToInt(center.y + halfWorld);
        int maxRadius = Mathf.CeilToInt(Mathf.Max(upper.x - lower.x, upper.y - lower.y) * 0.5f) + 1;
        Tile[,] tiles = world.worldTiles;
        float playLimit = world.playableSize * 0.5f + 10f;

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (radius > 0 && Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                        continue;
                    int x = centerX + dx;
                    int y = centerY + dy;
                    if (x < 0 || y < 0 || x >= tiles.GetLength(0) || y >= tiles.GetLength(1) ||
                        tiles[x, y].type >= 1)
                        continue;
                    float worldX = x - halfWorld + 0.5f;
                    float worldY = y - halfWorld + 0.5f;
                    if (Mathf.Abs(worldX) > playLimit || Mathf.Abs(worldY) > playLimit ||
                        worldX < lower.x || worldX > upper.x || worldY < lower.y || worldY > upper.y)
                        continue;
                    position = new Vector3(worldX, worldY, 0f);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsSafeId(string id)
    {
        foreach (char c in id)
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
        return true;
    }

    private static BridgeRequest? Deserialize(string json)
    {
        try
        {
            var serializer = new DataContractJsonSerializer(typeof(BridgeRequest));
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return serializer.ReadObject(input) as BridgeRequest;
        }
        catch { return null; }
    }

    private static string Serialize(BridgeResponse response)
    {
        var serializer = new DataContractJsonSerializer(typeof(BridgeResponse));
        using (var output = new MemoryStream())
        {
            serializer.WriteObject(output, response);
            return Encoding.UTF8.GetString(output.ToArray());
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _listener?.Stop();
    }

    private sealed class Incoming
    {
        internal readonly BridgeRequest Request;
        internal readonly ConcurrentQueue<BridgeResponse> Responses;
        internal readonly int Connection;
        internal Incoming(BridgeRequest request, ConcurrentQueue<BridgeResponse> responses, int connection)
        { Request = request; Responses = responses; Connection = connection; }
    }

    [DataContract]
    private sealed class BridgeRequest
    {
        [DataMember(Name = "requestId")] public string? RequestId { get; set; }
        [DataMember(Name = "type")] public string? Type { get; set; }
        [DataMember(Name = "quantity")] public int Quantity { get; set; }
    }

    [DataContract]
    private sealed class BridgeResponse
    {
        [DataMember(Name = "requestId")] public string RequestId { get; set; }
        [DataMember(Name = "accepted")] public bool Accepted { get; set; }
        [DataMember(Name = "reason")] public string Reason { get; set; }
        internal BridgeResponse(string requestId, bool accepted, string reason)
        { RequestId = requestId; Accepted = accepted; Reason = reason; }
    }
}
