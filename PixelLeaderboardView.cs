using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Persistent, bounded presentation. Scores are exact in the service; text refreshes at an interval.
internal sealed class PixelLeaderboardView : MonoBehaviour
{
    private sealed class Row
    {
        internal GameObject Root = null!;
        internal RectTransform Rect = null!;
        internal TextMeshProUGUI Place = null!;
        internal TextMeshProUGUI Name = null!;
        internal TextMeshProUGUI Score = null!;
        internal PixelParticipant? Participant;
        internal int Rank = -1;
        internal float Y;
        internal float TargetY;
        internal float HighlightUntil;
        internal Color BaseColor;
    }

    private readonly List<Row> _rows = new List<Row>(5);
    private PixelLeaderboardService? _service;
    private ManualLogSource? _log;
    private Canvas? _canvas;
    private TextMeshProUGUI? _empty;
    private TMP_FontAsset? _font;
    private float _refreshInterval;
    private float _nextRefresh;
    private long _lastVersion = -1;
    private int _maxVisible;
    private int _fontSize;
    private bool _animating;

    internal void Configure(PixelLeaderboardService service, int maxVisible,
        float refreshInterval, int fontSize, ManualLogSource log)
    {
        _service = service;
        _service.ResetPerformed += OnLeaderboardReset;
        _maxVisible = maxVisible;
        _refreshInterval = refreshInterval;
        _fontSize = fontSize;
        _log = log;
    }

    private void OnLeaderboardReset()
    {
        try
        {
            if (_canvas != null) Refresh(Time.unscaledTime);
        }
        catch (Exception error)
        {
            _log?.LogWarning($"[Leaderboard] Reset display refresh failed: {error}");
        }
    }

    private void Build(ManualLogSource log)
    {
        GameObject root = new GameObject("PixelLiveMod Podium", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 31;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _font = GamePixelFont.Acquire(log);
        log.LogInfo($"[Leaderboard] Font: {(_font != null ? _font.name : "TMP default fallback")}; size={_fontSize}");

        GameObject panel = new GameObject("Podium panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(660f, 304f);
        panelRect.anchoredPosition = new Vector2(-24f, -24f);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.035f, 0.045f, 0.055f, 0.72f);
        background.raycastTarget = false;
        AddBorder(panelRect);

        TextMeshProUGUI title = MakeText("Title", panelRect, new Vector2(0f, -15f),
            new Vector2(620f, 34f), TextAlignmentOptions.Center, _fontSize + 4);
        title.color = new Color(0.94f, 0.85f, 0.59f);
        title.text = "PÓDIO DOS PIXELS";
        _empty = MakeText("Empty", panelRect, new Vector2(0f, -128f),
            new Vector2(620f, 28f), TextAlignmentOptions.Center, _fontSize);
        _empty.color = new Color(0.68f, 0.73f, 0.75f);
        _empty.text = "Aguardando destruidores...";

        for (int i = 0; i < 5; i++)
        {
            GameObject rowRoot = new GameObject("Position " + (i + 1), typeof(RectTransform));
            rowRoot.transform.SetParent(panelRect, false);
            RectTransform rowRect = rowRoot.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.sizeDelta = new Vector2(620f, 35f);
            var row = new Row
            {
                Root = rowRoot, Rect = rowRect,
                Place = MakeText("Rank", rowRect, new Vector2(-286f, 0f),
                    new Vector2(40f, 32f), TextAlignmentOptions.Left, _fontSize),
                Name = MakeText("Viewer", rowRect, new Vector2(-75f, 0f),
                    new Vector2(380f, 32f), TextAlignmentOptions.Left, _fontSize),
                Score = MakeText("Pixels", rowRect, new Vector2(205f, 0f),
                    new Vector2(190f, 32f), TextAlignmentOptions.Right, _fontSize)
            };
            rowRoot.SetActive(false);
            _rows.Add(row);
        }
    }

    private TextMeshProUGUI MakeText(string name, RectTransform parent, Vector2 offset,
        Vector2 size, TextAlignmentOptions alignment, int sizePt)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        TextMeshProUGUI text = item.GetComponent<TextMeshProUGUI>();
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
        text.font = _font;
        text.fontSize = sizePt;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = false;
        text.raycastTarget = false;
        text.alignment = alignment;
        return text;
    }

    private static void AddBorder(RectTransform panel)
    {
        Color color = new Color(0.63f, 0.59f, 0.41f, 0.7f);
        AddEdge(panel, "Top", new Vector2(0.5f, 1f), new Vector2(656f, 2f), new Vector2(0f, -2f), color);
        AddEdge(panel, "Bottom", new Vector2(0.5f, 0f), new Vector2(656f, 2f), new Vector2(0f, 2f), color);
        AddEdge(panel, "Left", new Vector2(0f, 0.5f), new Vector2(2f, 300f), new Vector2(2f, 0f), color);
        AddEdge(panel, "Right", new Vector2(1f, 0.5f), new Vector2(2f, 300f), new Vector2(-2f, 0f), color);
    }

    private static void AddEdge(RectTransform parent, string name, Vector2 anchor,
        Vector2 size, Vector2 offset, Color color)
    {
        GameObject edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(parent, false);
        RectTransform rect = edge.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
        Image image = edge.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private void Update()
    {
        if (_service == null) return;
        WorldManager world = WorldManager.Instance;
        bool playable = world != null && !world.isMainMenu;
        if (_canvas != null && _canvas.enabled != playable) _canvas.enabled = playable;
        if (!playable) return;
        if (_canvas == null)
        {
            Build(_log!);
            Refresh(Time.unscaledTime);
        }
        float now = Time.unscaledTime;
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + _refreshInterval;
            if (_lastVersion != _service.Version) Refresh(now);
        }
        if (!_animating) return;
        _animating = false;
        float factor = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
        foreach (Row row in _rows)
        {
            if (row.Participant == null) continue;
            row.Y = Mathf.Lerp(row.Y, row.TargetY, factor);
            if (Mathf.Abs(row.Y - row.TargetY) < 0.1f) row.Y = row.TargetY;
            row.Rect.anchoredPosition = new Vector2(0f, row.Y);
            float glow = Mathf.Clamp01((row.HighlightUntil - now) / 0.45f);
            Color color = Color.Lerp(row.BaseColor, Color.white, glow * 0.23f);
            row.Place.color = row.Name.color = row.Score.color = color;
            if (row.Y != row.TargetY || glow > 0f) _animating = true;
        }
    }

    private void Refresh(float now)
    {
        if (_service == null) return;
        IReadOnlyList<PixelParticipant> top = _service.Top(_maxVisible);
        _lastVersion = _service.Version;
        _empty!.gameObject.SetActive(top.Count == 0);
        foreach (Row row in _rows)
        {
            if (row.Participant == null) continue;
            bool kept = false;
            foreach (PixelParticipant participant in top)
                if (ReferenceEquals(row.Participant, participant)) { kept = true; break; }
            if (!kept) { row.Participant = null; row.Rank = -1; row.Root.SetActive(false); }
        }
        for (int rank = 0; rank < top.Count; rank++)
        {
            PixelParticipant participant = top[rank];
            Row? row = null;
            foreach (Row candidate in _rows)
                if (ReferenceEquals(candidate.Participant, participant)) { row = candidate; break; }
            if (row == null)
            {
                foreach (Row candidate in _rows)
                    if (candidate.Participant == null) { row = candidate; break; }
            }
            if (row == null) continue;
            bool entering = row.Participant == null;
            int previousRank = row.Rank;
            row.Participant = participant;
            row.Rank = rank;
            row.TargetY = -61f - rank * 45f;
            if (entering)
            {
                row.Y = row.TargetY - 8f;
                row.Root.SetActive(true);
            }
            if (entering || rank < previousRank) row.HighlightUntil = now + 0.45f;
            row.BaseColor = RankColor(rank);
            row.Place.text = (rank + 1).ToString();
            row.Name.text = PixelLeaderboardFormat.Name(participant.Name, 22);
            row.Score.text = PixelLeaderboardFormat.Score(participant.Pixels);
            _animating = true;
        }
    }

    private static Color RankColor(int rank)
    {
        switch (rank)
        {
            case 0: return new Color(1f, 0.84f, 0.44f);
            case 1: return new Color(0.83f, 0.87f, 0.89f);
            case 2: return new Color(0.82f, 0.59f, 0.43f);
            default: return new Color(0.76f, 0.8f, 0.8f);
        }
    }

    private void OnDestroy()
    {
        if (_service != null) _service.ResetPerformed -= OnLeaderboardReset;
        if (_canvas != null) GamePixelFont.Release();
        _rows.Clear();
    }
}
