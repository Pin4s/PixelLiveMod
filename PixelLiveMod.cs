using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

[BepInPlugin("com.pixellive.mod", "PixelLiveMod", "0.7.0")]
public sealed class PixelLiveMod : BaseUnityPlugin
{
    private const float HotkeyCooldownSeconds = 0.5f;

    private HotkeyBinding[] _hotkeys = Array.Empty<HotkeyBinding>();
    private BallSpawnService? _spawner;
    private LocalSpawnBridge? _bridge;
    private LiveGiftFeed? _giftFeed;
    private PixelLeaderboardService? _leaderboard;
    private PixelDestructionTracker? _destructionTracker;
    private InfernoSettings? _infernoSettings;
    private ConfigEntry<int>? _infernoQuantity;
    private float _nextInfernoAllowedAt;
    private bool _inputErrorLogged;
    private bool _inputLoopLogged;
    private bool _sceneLogged;
    private float _nextSceneProbeAt;

    private void Awake()
    {
        // This table owns the input mapping and defaults. The spawn service has
        // no dependency on keys, BepInEx configuration, or command provenance.
        _hotkeys = new[]
        {
            Bind("F5_Orange", KeyCode.F5, false, UpgradeManager.BallColor.Orange, 1),
            Bind("Shift_F5_Orange", KeyCode.F5, true, UpgradeManager.BallColor.Orange, 5),
            Bind("F6_Yellow", KeyCode.F6, false, UpgradeManager.BallColor.Yellow, 1),
            Bind("Shift_F6_Yellow", KeyCode.F6, true, UpgradeManager.BallColor.Yellow, 5),
            Bind("F7_Green", KeyCode.F7, false, UpgradeManager.BallColor.Green, 1),
            Bind("Shift_F7_Green", KeyCode.F7, true, UpgradeManager.BallColor.Green, 5),
            Bind("F8_Pink", KeyCode.F8, false, UpgradeManager.BallColor.Pink, 1),
            Bind("Shift_F8_Pink", KeyCode.F8, true, UpgradeManager.BallColor.Pink, 5)
        };

        _infernoQuantity = Config.Bind("Hotkeys", "F9_Inferno_Quantity", 1,
            $"Number of Inferno Balls for F9. Valid range: 1..{BallSpawnService.MaxBallsPerCommand}.");
        _infernoSettings = InfernoSettings.Bind(Config);
        _spawner = new BallSpawnService(this, Logger);
        bool feedEnabled = Config.Bind("GiftFeed", "GiftFeedEnabled", true,
            "Show a small in-game feed for confirmed TikTok gift spawns.").Value;
        float feedDuration = Config.Bind("GiftFeed", "GiftFeedDuration", 5f,
            "Seconds visible after the latest created ball.").Value;
        float feedFadeIn = Config.Bind("GiftFeed", "GiftFeedFadeInDuration", 0.2f,
            "Fade-in duration in real-time seconds.").Value;
        float feedFadeOut = Config.Bind("GiftFeed", "GiftFeedFadeOutDuration", 0.4f,
            "Fade-out duration in real-time seconds.").Value;
        int feedMaxVisible = Config.Bind("GiftFeed", "GiftFeedMaxVisible", 5,
            "Maximum simultaneous gift messages.").Value;
        int feedFontSize = Config.Bind("GiftFeed", "GiftFeedFontSize", 20,
            "TMP font size at a 1920x1080 reference resolution.").Value;
        if (feedEnabled)
        {
            _giftFeed = gameObject.AddComponent<LiveGiftFeed>();
            _giftFeed.Configure(Mathf.Clamp(feedDuration, 0.5f, 60f),
                Mathf.Clamp(feedFadeIn, 0f, 2f), Mathf.Clamp(feedFadeOut, 0f, 3f),
                Mathf.Clamp(feedMaxVisible, 1, 10), Mathf.Clamp(feedFontSize, 12, 36), Logger);
        }
        bool leaderboardEnabled = Config.Bind("PixelLeaderboard", "PixelLeaderboardEnabled", true,
            "Show the in-game podium for pixels destroyed by TikTok gift balls.").Value;
        int leaderboardMax = Config.Bind("PixelLeaderboard", "PixelLeaderboardMaxVisible", 5,
            "Visible podium positions, from 1 to 5.").Value;
        float leaderboardRefresh = Config.Bind("PixelLeaderboard", "PixelLeaderboardRefreshInterval", 0.2f,
            "Seconds between visual score updates; internal counting remains immediate.").Value;
        int leaderboardFontSize = Config.Bind("PixelLeaderboard", "PixelLeaderboardFontSize", 20,
            "Podium row font size at a 1920x1080 reference resolution.").Value;
        if (leaderboardEnabled)
        {
            var candidate = new PixelLeaderboardService();
            _destructionTracker = new PixelDestructionTracker(candidate, Logger);
            if (_destructionTracker.Install())
            {
                _leaderboard = candidate;
                _giftFeed?.SetTop(360f);
                gameObject.AddComponent<PixelLeaderboardView>().Configure(candidate,
                    Mathf.Clamp(leaderboardMax, 1, 5),
                    Mathf.Clamp(leaderboardRefresh, 0.05f, 1f),
                    Mathf.Clamp(leaderboardFontSize, 12, 32), Logger);
            }
        }
        int bridgePort = Config.Bind("Bridge", "Port", 19081,
            "Local TCP port for the TikTok connector (127.0.0.1 only). Restart required.").Value;
        if (bridgePort < 1024 || bridgePort > 65535)
            Logger.LogError($"[Bridge] Invalid port {bridgePort}; expected 1024..65535");
        else
        {
            _bridge = new LocalSpawnBridge(bridgePort, _spawner, () => _infernoSettings!, Logger,
                _giftFeed, _leaderboard);
            _bridge.Start();
        }
        Logger.LogInfo("PixelLiveMod initialized successfully (0.7.0)");
        Logger.LogInfo($"Game data path: {Application.dataPath}");
        Logger.LogInfo($"Plugin assembly path: {typeof(PixelLiveMod).Assembly.Location}");
        foreach (HotkeyBinding binding in _hotkeys)
        {
            Logger.LogInfo($"Hotkey {binding.Label}: {binding.Color}, configured quantity={binding.Quantity.Value}");
            if (binding.Quantity.Value < 1 || binding.Quantity.Value > BallSpawnService.MaxBallsPerCommand)
                Logger.LogWarning($"Hotkey {binding.Label}: invalid quantity {binding.Quantity.Value}; " +
                    $"allowed range is 1..{BallSpawnService.MaxBallsPerCommand}");
        }
        Logger.LogInfo($"Hotkey F9: Inferno, configured quantity={_infernoQuantity.Value}");
        Logger.LogInfo($"Inferno settings: radius={_infernoSettings.Radius.Value} cells, " +
            $"damage={_infernoSettings.Damage.Value}, tiles/frame={_infernoSettings.TilesPerFrame.Value}, " +
            $"duration={_infernoSettings.Duration.Value:F2}s, particles={_infernoSettings.ParticlesEnabled.Value}, " +
            $"max particles={_infernoSettings.MaxParticles.Value}");
    }

    private HotkeyBinding Bind(
        string configKey, KeyCode key, bool shift, UpgradeManager.BallColor color, int defaultQuantity)
    {
        ConfigEntry<int> quantity = Config.Bind("Hotkeys", configKey + "_Quantity",
            defaultQuantity,
            $"Number of {color} balls for {(shift ? "Shift+" : "")}{key}. " +
            $"Valid range: 1..{BallSpawnService.MaxBallsPerCommand}. Changes apply after restarting the game.");
        return new HotkeyBinding(key, shift, color, quantity);
    }

    private void Update()
    {
        try
        {
            // The original BallShooting.Update uses UnityEngine.Input, so this
            // game's legacy input API is active.
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!_inputLoopLogged)
            {
                Logger.LogInfo("Hotkey polling active (UnityEngine.Input.GetKeyDown)");
                _inputLoopLogged = true;
            }
            if (!ctrl && !alt && !shift && Input.GetKeyDown(KeyCode.F9))
                HandleInfernoHotkey();

            foreach (HotkeyBinding binding in _hotkeys)
            {
                // Modified combinations are distinct commands; Ctrl/Alt have no mapping.
                if (ctrl || alt || binding.Shift != shift || !Input.GetKeyDown(binding.Key))
                    continue;

                int quantity = binding.Quantity.Value;
                Logger.LogInfo($"Hotkey {binding.Label} detected: key={binding.Key}, shift={shift}, " +
                    $"type={binding.Color}, configured quantity={quantity}");
                if (Time.unscaledTime < binding.NextAllowedAt)
                {
                    Logger.LogWarning($"Hotkey {binding.Label} rejected: 500 ms cooldown");
                    continue;
                }

                binding.NextAllowedAt = Time.unscaledTime + HotkeyCooldownSeconds;
                if (!TryCapturePointerWorld(out Vector3 worldPosition, out string pointerInfo))
                {
                    Logger.LogWarning($"Hotkey {binding.Label} rejected: {pointerInfo}");
                    continue;
                }
                Logger.LogInfo($"Hotkey {binding.Label}: {pointerInfo}");

                bool accepted = _spawner!.SpawnBalls(binding.Color, quantity, worldPosition);
                Logger.LogInfo($"Hotkey {binding.Label}: spawn request " +
                    (accepted ? "accepted" : "rejected"));
            }
        }
        catch (Exception exception)
        {
            if (!_inputErrorLogged)
            {
                Logger.LogError($"Hotkey input failed: {exception}");
                _inputErrorLogged = true;
            }
        }

        try
        {
            _bridge?.ProcessMainThread();
            _spawner?.ProcessFrame();
        }
        catch (Exception exception)
        {
            Logger.LogError($"Spawn queue processing failed: {exception}");
        }

        ReportSceneOnce();
    }

    private void OnDestroy()
    {
        _bridge?.Dispose();
        _destructionTracker?.Dispose();
    }

    private void HandleInfernoHotkey()
    {
        int quantity = _infernoQuantity!.Value;
        Logger.LogInfo($"Hotkey F9 detected: Inferno, configured quantity={quantity}");
        if (Time.unscaledTime < _nextInfernoAllowedAt)
        {
            Logger.LogWarning("Hotkey F9 rejected: 500 ms cooldown");
            return;
        }

        _nextInfernoAllowedAt = Time.unscaledTime + HotkeyCooldownSeconds;
        if (!_infernoSettings!.TrySnapshot(out InfernoParameters parameters, out string reason))
        {
            Logger.LogWarning($"Hotkey F9 rejected: {reason}");
            return;
        }

        if (!TryCapturePointerWorld(out Vector3 worldPosition, out string pointerInfo))
        {
            Logger.LogWarning($"Hotkey F9 rejected: {pointerInfo}");
            return;
        }

        Logger.LogInfo($"Hotkey F9: {pointerInfo}");
        bool accepted = _spawner!.SpawnInfernoBalls(quantity, worldPosition, parameters);
        Logger.LogInfo($"Hotkey F9: Inferno spawn request {(accepted ? "accepted" : "rejected")}");
    }

    private static bool TryCapturePointerWorld(out Vector3 worldPosition, out string info)
    {
        worldPosition = Vector3.zero;
        Camera camera = Camera.main;
        if (camera == null)
        {
            info = "main camera unavailable";
            return false;
        }

        Vector3 screenPosition = Input.mousePosition;
        worldPosition = camera.ScreenToWorldPoint(screenPosition);
        worldPosition.z = 0f;
        info = $"screen={screenPosition}, world={worldPosition}, cameraOrthographic={camera.orthographic}";
        return true;
    }

    private void ReportSceneOnce()
    {
        if (_sceneLogged || Time.unscaledTime < _nextSceneProbeAt)
            return;

        _nextSceneProbeAt = Time.unscaledTime + 1f;
        try
        {
            BallShooting shooting = BallShooting.Instance;
            if (shooting == null || shooting.balls == null)
                return;

            Logger.LogInfo($"BallShooting ready: {shooting.balls.Length} prefab slots");
            Camera sceneCamera = Camera.main;
            WorldManager sceneWorld = WorldManager.Instance;
            Logger.LogInfo($"Coordinate probe: camera={sceneCamera != null}; " +
                $"orthographic={sceneCamera != null && sceneCamera.orthographic}; " +
                $"cameraZ={(sceneCamera != null ? sceneCamera.transform.position.z : float.NaN)}; " +
                $"worldSize={(sceneWorld != null ? sceneWorld.worldSize : 0)}; " +
                $"playableSize={(sceneWorld != null ? sceneWorld.playableSize : 0)}");
            for (int index = 0; index < shooting.balls.Length; index++)
            {
                GameObject prefab = shooting.balls[index];
                if (prefab == null)
                    continue;

                Ball ball = prefab.GetComponent<Ball>();
                Logger.LogInfo($"Prefab[{index}]={prefab.name}; color={ball?.ballColor}; " +
                    $"body={prefab.GetComponent<Rigidbody2D>() != null}; " +
                    $"physics={prefab.GetComponent<PhysicsAgent>() != null}");

                if (ball != null && ball.ballColor == UpgradeManager.BallColor.Golden)
                {
                    GoldenPixelManager goldenManager = GoldenPixelManager.Instance;
                    Logger.LogInfo($"Golden preflight: component={prefab.GetComponent<GoldenBall>() != null}; " +
                        $"trail={prefab.GetComponent<TrailRenderer>() != null}; " +
                        $"particle={prefab.GetComponentInChildren<ParticleSystem>() != null}; " +
                        $"manager={goldenManager != null}; " +
                        $"positionsReady={goldenManager != null && goldenManager.goldenPixelPositionsLeft != null}; " +
                        $"blackout={BlackoutManager.Instance != null}; " +
                        $"activeGoldenCount={BlackoutManager.goldenBallsAlive}");
                }
            }

            BenchManager bench = BenchManager.Instance;
            if (bench != null && bench.ballButtons != null)
            {
                foreach (GameObject buttonPrefab in bench.ballButtons)
                {
                    if (buttonPrefab == null)
                        continue;
                    BallButton button = buttonPrefab.GetComponent<BallButton>();
                    if (button != null)
                        Logger.LogInfo($"Serialized button: color={button.ballColor}; force={button.ballForce}");
                }
            }

            _sceneLogged = true;
        }
        catch (Exception exception)
        {
            Logger.LogWarning($"One-time scene probe failed: {exception}");
            _sceneLogged = true;
        }
    }

    private sealed class HotkeyBinding
    {
        public readonly KeyCode Key;
        public readonly bool Shift;
        public readonly UpgradeManager.BallColor Color;
        public readonly ConfigEntry<int> Quantity;
        public float NextAllowedAt;

        public string Label => (Shift ? "Shift+" : "") + Key;

        public HotkeyBinding(KeyCode key, bool shift, UpgradeManager.BallColor color,
            ConfigEntry<int> quantity)
        {
            Key = key;
            Shift = shift;
            Color = color;
            Quantity = quantity;
        }
    }
}
