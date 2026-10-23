using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Temporary, opt-in observation of the game's ending. Never changes animation or save state.
internal sealed class EndingDiagnostics : MonoBehaviour
{
    private const float PollInterval = 0.1f;
    private const string Prefix = "[EndingDiag]";
    private static EndingDiagnostics? _active;

    private static readonly string[] WatchedPaths =
    {
        "Ending Canvas",
        "Ending Canvas/Ending Animation",
        "Ending Canvas/Ending Animation/Shade",
        "End Shade Animator",
        "Earthquake Shade Animator",
        "Ending Fish",
        "Fullscreen Shade",
        "Golden Pixel Canvas",
        "Golden Pixel Canvas/Post Collection",
        "Golden Pixel Canvas/Post Collection/Animation",
        "Golden Pixel Canvas/Post Collection/Animation/Pictures/Pyramids",
        "Golden Pixel Canvas/Post Collection/Animation/Pictures/Horus",
        "Golden Pixel Canvas/Post Collection/Animation/Black",
        "Golden Pixel Canvas/Post Collection/Animation/Black (1)",
        "Golden Pixel Canvas/Post Collection/Animation/GameObject/Black (1)",
        "Golden Pixel Canvas/Post Collection/Animation/GameObject (1)/Black (2)",
        "Golden Pixel Canvas/Post Collection/Stats Screen",
        "Golden Pixel Canvas/Post Collection/Stats Screen/Black"
    };

    private sealed class WatchedObject
    {
        internal readonly string Path;
        internal readonly GameObject? Object;
        internal readonly Animator? Animator;
        internal readonly Canvas? Canvas;
        internal readonly Graphic? Graphic;
        internal readonly SpriteRenderer? Sprite;
        internal readonly CanvasGroup? CanvasGroup;
        internal string? LastSignature;

        internal WatchedObject(string path, GameObject? target)
        {
            Path = path;
            Object = target;
            if (target == null) return;
            Animator = target.GetComponent<Animator>();
            Canvas = target.GetComponent<Canvas>();
            Graphic = target.GetComponent<Graphic>();
            Sprite = target.GetComponent<SpriteRenderer>();
            CanvasGroup = target.GetComponent<CanvasGroup>();
        }
    }

    private readonly Harmony _harmony = new Harmony("com.pixellive.mod.ending-diagnostics");
    private readonly List<WatchedObject> _watched = new List<WatchedObject>();
    private ManualLogSource? _log;
    private EndingManager? _winningEnding;
    private float _victoryAt;
    private float _nextPollAt;
    private int _sequence;
    private int _snapshot;
    private bool _installed;
    private bool _pollErrorLogged;

    internal void Configure(ManualLogSource log)
    {
        _log = log;
        try
        {
            _active = this;
            _harmony.Patch(AccessTools.Method(typeof(EndingManager), "LevelCleared"),
                postfix: new HarmonyMethod(typeof(EndingDiagnostics), nameof(VictoryPostfix)));
            SceneManager.sceneLoaded += OnSceneLoaded;
            _installed = true;
            log.LogInfo($"{Prefix} Enabled; F10 writes a numbered state snapshot to BepInEx/LogOutput.log");
        }
        catch (Exception error)
        {
            _harmony.UnpatchSelf();
            if (ReferenceEquals(_active, this)) _active = null;
            log.LogError($"{Prefix} Could not install victory observer: {error}");
        }
    }

    private static void VictoryPostfix(EndingManager __instance)
    {
        try { _active?.Begin(__instance); }
        catch (Exception error) { _active?._log?.LogWarning($"{Prefix} Victory observation failed: {error}"); }
    }

    private void Begin(EndingManager ending)
    {
        if (ReferenceEquals(_winningEnding, ending)) return;
        _winningEnding = ending;
        _victoryAt = Time.realtimeSinceStartup;
        _nextPollAt = 0f;
        _snapshot = 0;
        _sequence++;
        _pollErrorLogged = false;
        BindObjects();
        _log?.LogInfo($"{Prefix} VICTORY sequence={_sequence} scene={SceneManager.GetActiveScene().name} " +
            "t=+0.000s; watching ending and Golden Pixel presentation");
        PollChanges();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F10)) Snapshot();
        if (_winningEnding == null || Time.realtimeSinceStartup < _nextPollAt) return;
        _nextPollAt = Time.realtimeSinceStartup + PollInterval;
        try { PollChanges(); }
        catch (Exception error)
        {
            if (_pollErrorLogged) return;
            _pollErrorLogged = true;
            _log?.LogWarning($"{Prefix} Observation failed: {error}");
        }
    }

    private void BindObjects()
    {
        _watched.Clear();
        Transform? camera = Camera.main != null ? Camera.main.transform : null;
        foreach (string path in WatchedPaths)
        {
            Transform? found = camera != null ? camera.Find(path) : null;
            _watched.Add(new WatchedObject(path, found != null ? found.gameObject : null));
        }
    }

    private void PollChanges()
    {
        foreach (WatchedObject watched in _watched)
        {
            string signature = Signature(watched);
            if (signature == watched.LastSignature) continue;
            watched.LastSignature = signature;
            _log?.LogInfo($"{Prefix} CHANGE sequence={_sequence} t={RelativeTime()} path={watched.Path} " +
                Describe(watched, false));
        }
    }

    private void Snapshot()
    {
        try
        {
            if (_watched.Count == 0) BindObjects();
            int number = ++_snapshot;
            _log?.LogInfo($"{Prefix} SNAPSHOT sequence={_sequence} number={number} t={RelativeTime()} " +
                $"scene={SceneManager.GetActiveScene().name} ending={EndingManager.ending} " +
                $"timeScale={F(Time.timeScale)} objects={_watched.Count}");
            foreach (WatchedObject watched in _watched)
                _log?.LogInfo($"{Prefix} SNAPSHOT_ITEM sequence={_sequence} number={number} " +
                    $"path={watched.Path} {Describe(watched, true)}");
            _log?.LogInfo($"{Prefix} SNAPSHOT_END sequence={_sequence} number={number}");
        }
        catch (Exception error)
        {
            _log?.LogWarning($"{Prefix} Snapshot failed: {error}");
        }
    }

    private string RelativeTime() => _winningEnding == null ? "unarmed" :
        "+" + F(Time.realtimeSinceStartup - _victoryAt) + "s";

    private static string Signature(WatchedObject watched)
    {
        GameObject? target = watched.Object;
        if (target == null) return "missing";
        string result = $"{target.activeSelf}/{target.activeInHierarchy}";
        if (watched.Canvas != null) result += $"/canvas={watched.Canvas.enabled}";
        if (watched.Animator != null)
        {
            result += $"/animator={watched.Animator.enabled}";
            if (target.activeInHierarchy && watched.Animator.enabled)
            {
                AnimatorStateInfo state = watched.Animator.GetCurrentAnimatorStateInfo(0);
                result += $"/{state.fullPathHash}/{watched.Animator.IsInTransition(0)}" +
                    $"/completed={state.normalizedTime >= 1f}";
            }
        }
        float? alpha = Alpha(watched);
        if (alpha.HasValue) result += $"/alphaBand={AlphaBand(alpha.Value)}";
        if (watched.Graphic != null)
            result += $"/rendererBand={AlphaBand(watched.Graphic.canvasRenderer.GetAlpha())}";
        return result;
    }

    private static string Describe(WatchedObject watched, bool detailed)
    {
        GameObject? target = watched.Object;
        if (target == null) return "missing=true";
        string result = $"activeSelf={target.activeSelf} activeInHierarchy={target.activeInHierarchy}";
        if (watched.Canvas != null)
            result += $" canvasEnabled={watched.Canvas.enabled} sortOrder={watched.Canvas.sortingOrder}";
        float? alpha = Alpha(watched);
        if (alpha.HasValue) result += $" alpha={F(alpha.Value)} alphaBand={AlphaBand(alpha.Value)}";
        if (watched.Graphic != null)
            result += $" rendererAlpha={F(watched.Graphic.canvasRenderer.GetAlpha())}";
        if (watched.Animator != null)
        {
            Animator animator = watched.Animator;
            result += $" animatorEnabled={animator.enabled}";
            if (target.activeInHierarchy && animator.enabled)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                bool transitioning = animator.IsInTransition(0);
                result += $" stateHash={state.fullPathHash} shortHash={state.shortNameHash}" +
                    $" normalizedTime={F(state.normalizedTime)} inTransition={transitioning}" +
                    $" completed={state.normalizedTime >= 1f}";
                if (detailed)
                {
                    result += $" stateLength={F(state.length)} speed={F(state.speed)}" +
                        $" expectedState={ExpectedState(watched.Path, state)}";
                    if (transitioning)
                    {
                        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                        result += $" nextHash={next.fullPathHash} nextNormalizedTime={F(next.normalizedTime)}";
                    }
                }
            }
        }
        return result;
    }

    private static string ExpectedState(string path, AnimatorStateInfo state)
    {
        string? name = path == "Ending Canvas/Ending Animation" ? "Ending_Demo_Finished" :
            path == "Golden Pixel Canvas/Post Collection/Stats Screen" ? "You_Found_The_Golden_Pixels" :
            path == "Golden Pixel Canvas/Post Collection/Animation" ? "Golden Pixel Break" :
            path == "End Shade Animator" ? "Ending_Shade" : null;
        return name == null ? "unknown" :
            (state.IsName("Base Layer." + name) ? name : "other");
    }

    private static float? Alpha(WatchedObject watched)
    {
        if (watched.Graphic != null) return watched.Graphic.color.a;
        if (watched.Sprite != null) return watched.Sprite.color.a;
        if (watched.CanvasGroup != null) return watched.CanvasGroup.alpha;
        return null;
    }

    private static string AlphaBand(float alpha) => alpha <= 0.01f ? "clear" :
        alpha < 0.5f ? "low" : alpha < 0.99f ? "partial" : "opaque";

    private static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _winningEnding = null;
        _watched.Clear();
        _snapshot = 0;
        _log?.LogInfo($"{Prefix} SCENE_LOADED scene={scene.name}; observation disarmed");
    }

    private void OnDestroy()
    {
        if (_installed) SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony.UnpatchSelf();
        if (ReferenceEquals(_active, this)) _active = null;
    }
}
