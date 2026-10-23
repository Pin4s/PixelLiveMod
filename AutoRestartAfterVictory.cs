using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Opt-in transition. All callbacks run on Unity's main thread; the TCP listener
// remains alive on the persistent BepInEx plugin while the scene is replaced.
internal sealed class AutoRestartAfterVictory : IDisposable
{
    internal enum Phase { Playing, Ending, Galaxy, Restarting, Revealing, Failed }

    private static AutoRestartAfterVictory? _active;
    private readonly Harmony _harmony = new Harmony("com.pixellive.mod.auto-restart");
    private readonly ManualLogSource _log;
    private readonly BallSpawnService _spawner;
    private readonly LocalSpawnBridge? _bridge;
    private readonly GoldenPixelCollisionPatch? _goldenContact;
    private readonly PixelLeaderboardService? _leaderboard;
    private readonly bool _autoVictoryEnabled;
    private readonly Transform _host;
    private Phase _phase = Phase.Playing;
    private EndingManager? _ending;
    private Animator? _statsAnimator;
    private GameObject? _statsScreen;
    private GameObject? _black;
    private Graphic? _blackGraphic;
    private GameObject? _laterEndingAnimation;
    private GameObject? _cover;
    private Image? _coverImage;
    private int _coverShownFrame = -1;
    private readonly RestartBlackoutState _blackout = new RestartBlackoutState();
    private GalaxyResetVFX? _galaxy;
    private int _sceneIndex;
    private bool _archived;
    private bool _gameStarted;
    private bool _worldFinished;
    private bool _installed;
    private string? _archivePath;

    internal AutoRestartAfterVictory(ManualLogSource log, BallSpawnService spawner,
        LocalSpawnBridge? bridge, GoldenPixelCollisionPatch? goldenContact,
        PixelLeaderboardService? leaderboard, Transform host, bool autoVictoryEnabled)
    {
        _log = log;
        _spawner = spawner;
        _bridge = bridge;
        _goldenContact = goldenContact;
        _leaderboard = leaderboard;
        _host = host;
        _autoVictoryEnabled = autoVictoryEnabled;
    }

    internal void Install()
    {
        try
        {
            Type? iterator = null;
            foreach (Type nested in typeof(WorldManager).GetNestedTypes(BindingFlags.NonPublic))
                if (nested.Name.StartsWith("<Start>", StringComparison.Ordinal)) { iterator = nested; break; }
            if (iterator == null) throw new MissingMemberException("WorldManager.Start iterator");
            MethodInfo moveNext = AccessTools.Method(iterator, "MoveNext") ??
                throw new MissingMethodException(iterator.FullName, "MoveNext");
            _active = this;
            _harmony.Patch(AccessTools.Method(typeof(EndingManager), "LevelCleared"),
                postfix: new HarmonyMethod(typeof(AutoRestartAfterVictory), nameof(VictoryPostfix)));
            _harmony.Patch(moveNext,
                postfix: new HarmonyMethod(typeof(AutoRestartAfterVictory), nameof(WorldStartPostfix)));
            _harmony.Patch(AccessTools.Method(typeof(SaveManager), "QuickSaveSequence"),
                prefix: new HarmonyMethod(typeof(AutoRestartAfterVictory), nameof(QuickSavePrefix)));
            SceneManager.sceneLoaded += OnSceneLoaded;
            _installed = true;
            _log.LogInfo($"[AutoRestart] Safe round transition ready; automatic victory={_autoVictoryEnabled}");
        }
        catch (Exception error)
        {
            _harmony.UnpatchSelf();
            if (ReferenceEquals(_active, this)) _active = null;
            _log.LogError($"[AutoRestart] Installation failed: {error}");
        }
    }

    private static void VictoryPostfix(EndingManager __instance)
    {
        AutoRestartAfterVictory? restart = _active;
        if (restart == null || !restart._autoVictoryEnabled || restart._phase != Phase.Playing ||
            !EndingManager.ending) return;
        restart._phase = Phase.Ending;
        restart._ending = __instance;
        restart._spawner.BlockAndCancelPending();
        restart._bridge?.BeginRoundTransition();
        restart._goldenContact?.Hold();
        restart.BindVisuals();
        restart._log.LogInfo("[AutoRestart] PLAYING -> ENDING; previous-round commands invalidated");
    }

    private void BindVisuals()
    {
        Transform? camera = Camera.main != null ? Camera.main.transform : null;
        Transform? screen = camera != null ? camera.Find("Golden Pixel Canvas/Post Collection/Stats Screen") : null;
        _statsScreen = screen != null ? screen.gameObject : null;
        _statsAnimator = screen != null ? screen.GetComponent<Animator>() : null;
        Transform? black = screen != null ? screen.Find("Black") : null;
        _black = black != null ? black.gameObject : null;
        _blackGraphic = black != null ? black.GetComponent<Graphic>() : null;
        Transform? later = camera != null ? camera.Find("Ending Canvas/Ending Animation") : null;
        _laterEndingAnimation = later != null ? later.gameObject : null;
    }

    internal void Update()
    {
        if (_phase == Phase.Ending)
        {
            if (_statsAnimator == null || _blackGraphic == null || _laterEndingAnimation == null)
                BindVisuals();
            if (AtChosenVisualState()) BeginRestart();
        }
        else if (_phase == Phase.Galaxy)
        {
            _galaxy?.Update();
            // Establish an opaque, persistent cover while the VFX is still
            // black. Let Unity render it before unloading the old scene.
            if (_galaxy?.BlackoutOpaque == true) ShowCover();
            if (_galaxy?.Complete == true && _cover != null &&
                Time.frameCount > _coverShownFrame) BeginRestart();
        }
        else if (_phase == Phase.Restarting && _archived && _gameStarted && _worldFinished)
            BeginRevealWhenReady();
        else if (_phase == Phase.Revealing)
            UpdateReveal();
    }

    internal bool TryBeginGalaxy(string? viewerName)
    {
        if (!_installed || _phase != Phase.Playing || EndingManager.ending ||
            WorldManager.Instance == null || WorldManager.Instance.worldTiles == null ||
            WorldManager.Instance.isMainMenu || MainMenuManager.inMainMenu ||
            Time.timeScale <= 0f || BallShooting.Instance == null ||
            !BallShooting.Instance.canShootBalls) return false;
        try { _galaxy = new GalaxyResetVFX(_host, viewerName, _log); }
        catch (Exception error)
        {
            _log.LogError($"[Galaxy] Could not start visual reset: {error}");
            return false;
        }
        _phase = Phase.Galaxy;
        _spawner.BlockAndCancelPending();
        _bridge?.BeginRoundTransition();
        _goldenContact?.Hold();
        _leaderboard?.ResetLeaderboard();
        _log.LogInfo("[Galaxy] PLAYING -> GALAXY; old round blocked and podium cleared");
        return true;
    }

    private bool AtChosenVisualState()
    {
        if (_ending == null || _statsScreen == null || _statsAnimator == null ||
            _black == null || _blackGraphic == null || _laterEndingAnimation == null) return false;
        AnimatorStateInfo state = _statsAnimator.GetCurrentAnimatorStateInfo(0);
        return EndingTransitionRules.Matches(EndingManager.ending,
            _statsScreen.activeInHierarchy && _statsAnimator.enabled,
            state.IsName("Base Layer.You_Found_The_Golden_Pixels"),
            _statsAnimator.IsInTransition(0), state.normalizedTime,
            _black.activeInHierarchy, _blackGraphic.color.a,
            _laterEndingAnimation.activeInHierarchy);
    }

    private void BeginRestart()
    {
        if (_phase != Phase.Ending && _phase != Phase.Galaxy) return;
        bool galaxy = _phase == Phase.Galaxy;
        _phase = Phase.Restarting;
        _sceneIndex = SceneManager.GetActiveScene().buildIndex;
        _archived = _gameStarted = _worldFinished = false;
        _archivePath = null;
        _log.LogInfo(galaxy ? "[Galaxy] GALAXY -> RESTARTING after cosmic darkness" :
            "[AutoRestart] ENDING -> RESTARTING at opaque Stats Screen, " +
            "You_Found_The_Golden_Pixels normalizedTime >= 0.046");
        try
        {
            ShowCover();
            _galaxy?.Dispose();
            _galaxy = null;
            SceneManager.LoadScene(_sceneIndex);
        }
        catch (Exception error) { Fail("Scene reload failed", error); }
    }

    private void ShowCover()
    {
        if (_cover != null) return;
        _cover = new GameObject("PixelLiveMod Restart Cover", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        UnityEngine.Object.DontDestroyOnLoad(_cover);
        Canvas canvas = _cover.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = short.MaxValue;
        GameObject screen = new GameObject("Opaque screen", typeof(RectTransform), typeof(Image));
        screen.transform.SetParent(_cover.transform, false);
        RectTransform rect = screen.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _coverImage = screen.GetComponent<Image>();
        _coverImage.color = Color.black;
        _coverImage.raycastTarget = true;
        _blackout.Cover();
        _coverShownFrame = Time.frameCount;
        Canvas.ForceUpdateCanvases();
        _log.LogInfo("[AutoRestart] Opaque cover established above scene canvases");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_phase != Phase.Restarting || scene.buildIndex != _sceneIndex || _archived) return;
        try
        {
            // sceneLoaded runs before Start: old scene coroutines have stopped,
            // and WorldManager has not yet checked whether a save exists.
            string destination = RoundSaveArchive.ArchiveActiveSave(Application.persistentDataPath);
            _archivePath = destination;
            _archived = true;
            WorldManager.goldenPixelsCollected = 0;
            _log.LogInfo($"[AutoRestart] Previous round archived at {destination}; new world will be generated");
        }
        catch (Exception error) { Fail("Could not archive the active save; automatic start stopped", error); }
    }

    private static void WorldStartPostfix(object __instance, bool __result)
    {
        AutoRestartAfterVictory? restart = _active;
        if (restart == null || restart._phase != Phase.Restarting || !restart._archived ||
            __instance is not IEnumerator iterator) return;
        try
        {
            if (__result && !restart._gameStarted && iterator.Current is WaitUntil)
            {
                MainMenuManager menu = MainMenuManager.Instance;
                if (menu == null || WorldManager.Instance == null)
                    throw new InvalidOperationException("New scene managers are unavailable");
                restart._gameStarted = true;
                menu.GameStarts();
                restart._log.LogInfo("[AutoRestart] Native GameStarts invoked at WorldManager's play gate");
            }
            else if (!__result && restart._gameStarted)
            {
                restart._worldFinished = true;
                restart._log.LogInfo("[AutoRestart] New WorldManager.Start coroutine completed");
            }
        }
        catch (Exception error) { restart.Fail("New world initialization failed", error); }
    }

    private static bool QuickSavePrefix(ref IEnumerator __result)
    {
        if (_active?._phase != Phase.Restarting) return true;
        // Native GameStarts starts autosave before fresh world generation ends.
        // Skip only saves during that narrow generation window.
        __result = EmptySave();
        return false;
    }

    private static IEnumerator EmptySave() { yield break; }

    private void BeginRevealWhenReady()
    {
        WorldManager world = WorldManager.Instance;
        if (world == null || world.worldTiles == null || world.isMainMenu ||
            BallManager.Instance == null || BallManager.Instance.ballList == null ||
            BallShooting.Instance == null || !BallShooting.Instance.canShootBalls ||
            PointsManager.Instance == null || MainMenuManager.inMainMenu ||
            !System.IO.Directory.Exists(System.IO.Path.Combine(Application.persistentDataPath, "save"))) return;
        if (!_blackout.RevealWhenReady(true)) return;
        _phase = Phase.Revealing;
        _log.LogInfo("[AutoRestart] New world and native play gate ready; revealing new round");
    }

    private void UpdateReveal()
    {
        _blackout.Advance(Time.unscaledDeltaTime);
        if (_coverImage != null)
            _coverImage.color = new Color(0f, 0f, 0f, _blackout.Alpha);
        if (_blackout.Current != RestartBlackoutState.Stage.Hidden) return;
        _spawner.Resume();
        _bridge?.FinishRoundTransition();
        _goldenContact?.ResumeNewRound();
        _ending = null;
        _statsAnimator = null;
        _phase = Phase.Playing;
        HideCover();
        _log.LogInfo($"[AutoRestart] RESTARTING -> PLAYING; archived round={_archivePath}");
        _archived = _gameStarted = _worldFinished = false;
        _archivePath = null;
    }

    private void Fail(string message, Exception error)
    {
        _phase = Phase.Failed;
        _galaxy?.Dispose();
        _galaxy = null;
        HideCover();
        _log.LogError($"[AutoRestart] {message}: {error}; archive={_archivePath ?? "none"}. " +
            "Automatic spawning remains blocked; inspect and recover the archived save before continuing.");
    }

    private void HideCover()
    {
        if (_cover != null) UnityEngine.Object.Destroy(_cover);
        _cover = null;
        _coverImage = null;
        _coverShownFrame = -1;
        _blackout.Hide();
    }

    public void Dispose()
    {
        if (_installed) SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony.UnpatchSelf();
        if (ReferenceEquals(_active, this)) _active = null;
        _galaxy?.Dispose();
        _galaxy = null;
        HideCover();
    }
}
