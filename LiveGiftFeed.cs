using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Presentation only. The bridge calls Notify on Unity's main thread after spawn completion.
internal sealed class LiveGiftFeed : MonoBehaviour
{
    private GiftFeedState? _state;
    private Canvas? _canvas;
    private TMP_FontAsset? _font;
    private ManualLogSource? _log;
    private readonly Dictionary<GiftFeedState.Entry, TextMeshProUGUI> _views =
        new Dictionary<GiftFeedState.Entry, TextMeshProUGUI>();
    private readonly List<GiftFeedState.Entry> _removal = new List<GiftFeedState.Entry>();
    private int _fontSize;
    private float _top = 24f;

    internal void SetTop(float top) { _top = top; }

    internal void Configure(float duration, float fadeIn, float fadeOut, int maxVisible,
        int fontSize, ManualLogSource log)
    {
        _state = new GiftFeedState(duration, fadeIn, fadeOut, maxVisible);
        _fontSize = fontSize;
        _log = log;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    internal void Notify(string batchId, string viewerId, string viewerName,
        string giftId, string giftName, int giftUnits, string ballType, int created)
    {
        if (_state == null) return;
        GiftFeedState.Entry? entry = _state.Apply(batchId, viewerId, viewerName,
            giftId, giftName, giftUnits, ballType, created, Time.unscaledTime);
        if (entry == null) return;
        EnsureCanvas();
        Reconcile();
        if (!_views.TryGetValue(entry, out TextMeshProUGUI? label)) return;
        label.text = Format(entry);
        _canvas!.enabled = true;
    }

    internal void NotifyReward(string batchId, string source, string viewerId,
        string viewerName, int milestone, string ballType, int created)
    {
        if (_state == null || created <= 0) return;
        string key = source == "likes" ? "likes:" + milestone : "follow";
        GiftFeedState.Entry? entry = _state.Apply(batchId, viewerId, viewerName,
            key, key, 1, ballType, created, Time.unscaledTime);
        if (entry == null) return;
        EnsureCanvas();
        Reconcile();
        if (_views.TryGetValue(entry, out TextMeshProUGUI? label)) label.text = Format(entry);
        _canvas!.enabled = true;
    }

    private void Update()
    {
        if (_state == null || _state.Entries.Count == 0) return;
        _state.Tick(Time.unscaledTime, Time.unscaledDeltaTime);
        Reconcile();
        foreach (GiftFeedState.Entry entry in _state.Entries)
        {
            if (!_views.TryGetValue(entry, out TextMeshProUGUI? label)) continue;
            RectTransform rect = label.rectTransform;
            float age = Time.unscaledTime - entry.LastUpdate;
            float enterOffset = (1f - Mathf.Clamp01((Time.unscaledTime - entry.BornAt) /
                Mathf.Max(0.001f, _state.FadeIn))) * 7f;
            float exitOffset = age > _state.Duration ?
                Mathf.Clamp01((age - _state.Duration) / Mathf.Max(0.001f, _state.FadeOut)) * 5f : 0f;
            rect.anchoredPosition = new Vector2(-24f, -_top - entry.Position * 56f + enterOffset + exitOffset);
            label.alpha = entry.Opacity;
        }
        if (_canvas != null && _state.Entries.Count == 0) _canvas.enabled = false;
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;
        GameObject root = new GameObject("PixelLiveMod Gift Feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 30;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _font = GamePixelFont.Acquire(_log!);
        _log?.LogInfo($"[GiftFeed] Font: {(_font != null ? _font.name : "TMP default fallback")}; size={_fontSize}");
    }

    private void Reconcile()
    {
        if (_state == null) return;
        _removal.Clear();
        foreach (GiftFeedState.Entry entry in _views.Keys)
        {
            bool present = false;
            foreach (GiftFeedState.Entry active in _state.Entries)
                if (ReferenceEquals(entry, active)) { present = true; break; }
            if (!present) _removal.Add(entry);
        }
        foreach (GiftFeedState.Entry entry in _removal)
        {
            if (_views.TryGetValue(entry, out TextMeshProUGUI? label)) Destroy(label.gameObject);
            _views.Remove(entry);
        }
        if (_canvas == null) return;
        foreach (GiftFeedState.Entry entry in _state.Entries)
        {
            if (_views.ContainsKey(entry)) continue;
            GameObject item = new GameObject("Gift", typeof(RectTransform), typeof(TextMeshProUGUI));
            item.transform.SetParent(_canvas.transform, false);
            TextMeshProUGUI label = item.GetComponent<TextMeshProUGUI>();
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(660f, 54f);
            rect.anchoredPosition = new Vector2(-24f, -_top);
            label.font = _font;
            label.fontSize = _fontSize;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = true;
            label.raycastTarget = false;
            label.color = new Color(0.88f, 0.89f, 0.88f);
            label.alignment = TextAlignmentOptions.TopRight;
            label.text = Format(entry);
            _views.Add(entry, label);
        }
    }

    private static string Format(GiftFeedState.Entry entry)
    {
        string gift = GiftFeedNames.GiftName(entry.GiftName, entry.Gifts);
        string ball = GiftFeedNames.BallName(entry.BallType, entry.Balls);
        string ballColor = entry.BallType == "Orange" ? "#E8A46F" :
            entry.BallType == "Yellow" ? "#E7D58A" :
            entry.BallType == "Green" ? "#9BCB99" :
            entry.BallType == "Pink" ? "#C9A2D7" : "#E19779";
        string heading = entry.GiftId == "follow" ?
            $"<color=#A8CCD0>{Escape(entry.ViewerName)}</color> começou a seguir" :
            entry.GiftId.StartsWith("likes:", StringComparison.Ordinal) ?
            $"<color=#A8CCD0>{Escape(entry.ViewerName)}</color> enviou {Escape(entry.GiftId.Substring(6))} curtidas" :
            $"<color=#A8CCD0>{Escape(entry.ViewerName)}</color> enviou {entry.Gifts} {Escape(gift)}";
        return heading + $"\n> Invocou {entry.Balls} <color={ballColor}>{ball}</color>";
    }

    private static string Escape(string value) => value.Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (TextMeshProUGUI view in _views.Values) if (view != null) Destroy(view.gameObject);
        _views.Clear();
        _state = _state == null ? null : new GiftFeedState(_state.Duration, _state.FadeIn, _state.FadeOut, _state.MaxVisible);
        if (_canvas != null) _canvas.enabled = false;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _views.Clear();
        if (_canvas != null) GamePixelFont.Release();
    }
}
