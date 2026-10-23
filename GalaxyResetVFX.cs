using System;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bounded pixel-art illusion. Dark tile silhouettes and their paired shards
// suggest matter being torn from the map; no tile or ball physics is touched.
internal sealed class GalaxyResetVFX : IDisposable
{
    private const int Segments = 56;
    private const int FragmentCount = 72;
    private const int RiftCount = 48;
    private readonly ManualLogSource _log;
    private readonly GameObject _worldRoot;
    private readonly GameObject _canvasRoot;
    private readonly SpriteRenderer _core;
    private readonly SpriteRenderer _aura;
    private readonly SpriteRenderer _glow;
    private readonly SpriteRenderer[] _fragments = new SpriteRenderer[FragmentCount];
    private readonly SpriteRenderer[] _rifts = new SpriteRenderer[RiftCount];
    private readonly SpriteRenderer[] _riftShards = new SpriteRenderer[RiftCount];
    private readonly Vector3[] _riftOrigins = new Vector3[RiftCount];
    private readonly LineRenderer _innerRing;
    private readonly LineRenderer _outerRing;
    private readonly LineRenderer _vortexRing;
    private readonly LineRenderer _shockRing;
    private readonly Image _shade;
    private readonly Image _flash;
    private readonly TextMeshProUGUI _label;
    private readonly TMP_FontAsset? _font;
    private readonly Sprite _pixel;
    private readonly Texture2D _texture;
    private readonly Texture2D _glowTexture;
    private readonly Sprite _glowSprite;
    private readonly Material _material;
    private readonly GalaxyCameraShake _shake;
    private readonly float _startedAt;
    private readonly float _screenRadius;
    private readonly float _screenHalfWidth;
    private readonly float _screenHalfHeight;
    private GalaxyStage _lastStage = GalaxyStage.Birth;

    internal bool Complete => GalaxyResetTimeline.Stage(Time.realtimeSinceStartup - _startedAt) == GalaxyStage.Complete;
    internal bool BlackoutOpaque => _shade != null && _shade.color.a >= 0.999f &&
        _flash.color.a <= 0.001f;

    internal GalaxyResetVFX(Transform host, string? viewerName, ManualLogSource log)
    {
        _log = log;
        Camera camera = Camera.main ?? throw new InvalidOperationException("Galaxy reset requires the main camera");
        Shader shader = Shader.Find("Sprites/Default") ??
            throw new InvalidOperationException("Sprite shader unavailable");
        Vector3 center = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f,
            -camera.transform.position.z));
        // Geometry is authored at the native fresh-game zoom. The world root
        // compensates for the actual zoom; screen-space UI needs no adjustment.
        _screenRadius = GalaxyResetTimeline.DefaultOrthographicSize * 1.8f;
        _screenHalfHeight = GalaxyResetTimeline.DefaultOrthographicSize;
        _screenHalfWidth = _screenHalfHeight * camera.aspect;
        _worldRoot = new GameObject("PixelLiveMod Galaxy Reset World");
        _worldRoot.transform.SetParent(host, false);
        _worldRoot.transform.position = new Vector3(center.x, center.y, 0f);
        _worldRoot.AddComponent<GalaxyZoomCompensation>().Configure(camera);
        _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "PixelLiveMod Galaxy Pixel"
        };
        _texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        _texture.Apply(false, true);
        _pixel = Sprite.Create(_texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
        _glowTexture = CreateGlowTexture();
        _glowSprite = Sprite.Create(_glowTexture, new Rect(0, 0, 32, 32),
            new Vector2(0.5f, 0.5f), 16f);
        _material = new Material(shader) { name = "PixelLiveMod Galaxy Rings" };
        _material.mainTexture = Texture2D.whiteTexture;
        _glow = Square("Pixelated violet corona", 297, Color.white);
        _glow.sprite = _glowSprite;
        _aura = Square("Violet pixel aura", 301, new Color(0.68f, 0.07f, 1f, 0.95f));
        _core = Square("Black hole core", 302, Color.black);
        _innerRing = Ring("Galaxy inner ring", 303, 0.24f);
        _outerRing = Ring("Galaxy outer ring", 304, 0.16f);
        _vortexRing = Ring("Galaxy unstable vortex", 305, 0.12f);
        _shockRing = Ring("Galaxy reset shockwave", 307, 0.28f);
        for (int i = 0; i < FragmentCount; i++)
            _fragments[i] = Square("Galaxy fragment", 306, i % 6 == 0 ? Color.white :
                new Color(0.78f, 0.3f, 1f, 0.95f));
        for (int i = 0; i < RiftCount; i++)
        {
            float x = (Hash(i, 1) * 1.8f - 0.9f) * _screenHalfWidth;
            float y = (Hash(i, 2) * 1.8f - 0.9f) * _screenHalfHeight;
            if (Mathf.Abs(x) + Mathf.Abs(y) < _screenHalfHeight * 0.45f)
                x += x < 0f ? -_screenHalfHeight * 0.45f : _screenHalfHeight * 0.45f;
            _riftOrigins[i] = new Vector3(Mathf.Round(x * 2f) * 0.5f,
                Mathf.Round(y * 2f) * 0.5f, 0f);
            _rifts[i] = Square("Erased world block", 298, new Color(0.015f, 0f, 0.025f, 0f));
            _riftShards[i] = Square("Consumed pixel block", 306, i % 5 == 0 ?
                new Color(1f, 0.86f, 1f) : new Color(0.62f, 0.16f, 0.95f));
        }

        _canvasRoot = new GameObject("PixelLiveMod Galaxy Reset UI", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        _canvasRoot.transform.SetParent(host, false);
        Canvas canvas = _canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        GameObject shadeObject = new GameObject("Cosmic dimming", typeof(RectTransform), typeof(Image));
        shadeObject.transform.SetParent(_canvasRoot.transform, false);
        RectTransform shadeRect = shadeObject.GetComponent<RectTransform>();
        shadeRect.anchorMin = Vector2.zero;
        shadeRect.anchorMax = Vector2.one;
        shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
        _shade = shadeObject.GetComponent<Image>();
        _shade.raycastTarget = false;
        _shade.color = Color.clear;
        GameObject flashObject = new GameObject("Cosmic overload flash", typeof(RectTransform), typeof(Image));
        flashObject.transform.SetParent(_canvasRoot.transform, false);
        RectTransform flashRect = flashObject.GetComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
        _flash = flashObject.GetComponent<Image>();
        _flash.raycastTarget = false;
        _flash.color = Color.clear;
        _font = GamePixelFont.Acquire(log);
        GameObject textObject = new GameObject("Galaxy reset caption", typeof(RectTransform),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(_canvasRoot.transform, false);
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        _label = label;
        RectTransform textRect = label.rectTransform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(900f, 100f);
        textRect.anchoredPosition = new Vector2(0f, -180f);
        label.font = _font;
        label.fontSize = 27f;
        label.alignment = TextAlignmentOptions.Center;
        label.richText = false;
        label.raycastTarget = false;
        label.color = new Color(0.82f, 0.58f, 1f, 0.95f);
        string cleanName = viewerName?.Replace('\r', ' ').Replace('\n', ' ').Trim().TrimStart('@') ?? "";
        label.text = cleanName.Length == 0 ? "GALAXY RESET\nO universo está reiniciando..." :
            "GALAXY RESET\n@" + cleanName.Substring(0, Math.Min(40, cleanName.Length)) +
            " está reiniciando o universo...";
        _startedAt = Time.realtimeSinceStartup;
        _shake = camera.gameObject.AddComponent<GalaxyCameraShake>();
        _shake.Configure(camera, _startedAt);
        Render(0f);
    }

    private SpriteRenderer Square(string name, int order, Color color)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(_worldRoot.transform, false);
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = _pixel;
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }

    private LineRenderer Ring(string name, int order, float width)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(_worldRoot.transform, false);
        LineRenderer ring = child.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = Segments;
        ring.widthMultiplier = width;
        ring.sortingOrder = order;
        ring.sharedMaterial = _material;
        return ring;
    }

    internal void Update()
    {
        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _startedAt);
        GalaxyStage stage = GalaxyResetTimeline.Stage(elapsed);
        if (stage != _lastStage)
        {
            _log.LogInfo($"[Galaxy] Visual stage {stage} at +{elapsed:F2}s");
            _lastStage = stage;
        }
        if (stage != GalaxyStage.Complete) Render(elapsed);
    }

    private void Render(float t)
    {
        GalaxyStage stage = GalaxyResetTimeline.Stage(t);
        float radius;
        float shade;
        float coreScale;
        if (stage == GalaxyStage.Birth)
        {
            float p = Mathf.Clamp01(t / 1.2f);
            radius = Mathf.Lerp(0.2f, 3.4f, p * p);
            shade = 0.06f + 0.17f * p;
            coreScale = Mathf.Lerp(0.18f, 2f, p * p);
        }
        else if (stage == GalaxyStage.Collapse)
        {
            float p = Mathf.Clamp01((t - 1.2f) / 1.9f);
            float surge = p * p * (3f - 2f * p);
            radius = Mathf.Lerp(3.4f, _screenRadius * 0.9f, surge);
            shade = Mathf.Lerp(0.23f, 0.65f, surge) + 0.045f * p * Mathf.Sin(t * 17f);
            coreScale = Mathf.Lerp(2f, 6.2f, surge);
        }
        else if (stage == GalaxyStage.Contraction)
        {
            float p = Mathf.Clamp01((t - 3.1f) / 0.45f);
            radius = Mathf.Lerp(_screenRadius * 0.9f, 0.08f, 1f - (1f - p) * (1f - p));
            shade = Mathf.Lerp(0.65f, 0.94f, p) + 0.035f * Mathf.Sin(t * 29f);
            coreScale = Mathf.Lerp(6.2f, 0.1f, p * p);
        }
        else if (stage == GalaxyStage.Burst)
        {
            float p = Mathf.Clamp01((t - 3.55f) / 0.8f);
            radius = Mathf.Lerp(0.08f, _screenRadius * 1.7f, 1f - Mathf.Pow(1f - p, 3f));
            shade = Mathf.Lerp(0.88f, 0.55f, Mathf.Clamp01(p * 2.3f));
            coreScale = Mathf.Lerp(0.1f, 3f, Mathf.Sin(p * Mathf.PI));
        }
        else
        {
            float p = Mathf.Clamp01((t - 4.35f) / 0.85f);
            radius = _screenRadius * (1.7f + p * 0.2f);
            shade = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(p * 1.45f));
            coreScale = Mathf.Lerp(1f, 0f, p);
        }
        shade = Mathf.Clamp01(shade);
        _shade.color = new Color(0.018f, 0f, 0.04f, shade);
        float overloadPulse = GalaxyResetTimeline.OverloadFlash(t);
        float burstFlash = GalaxyResetTimeline.BurstFlash(t);
        _flash.color = new Color(0.94f, 0.82f, 1f, Mathf.Max(overloadPulse, burstFlash));
        _label.color = new Color(0.94f, 0.7f, 1f,
            stage == GalaxyStage.Darkness ? Mathf.Clamp01((5.05f - t) * 2f) :
            0.84f + 0.12f * Mathf.Sin(t * 8f));
        _core.transform.localScale = Vector3.one * coreScale;
        _aura.transform.localScale = Vector3.one *
            (coreScale * (1.65f + 0.18f * Mathf.Sin(t * 18f)));
        _glow.transform.localScale = Vector3.one *
            (coreScale * (3.2f + 0.35f * Mathf.Sin(t * 13f)));
        _glow.color = new Color(1f, 1f, 1f,
            stage == GalaxyStage.Darkness ? 0.4f : 0.75f + 0.2f * Mathf.Sin(t * 11f));
        _core.transform.localRotation = Quaternion.Euler(0f, 0f, t * 130f);
        _aura.transform.localRotation = Quaternion.Euler(0f, 0f, -t * 115f);
        Color inner = stage == GalaxyStage.Burst ? new Color(1f, 0.92f, 1f, 1f) :
            new Color(0.8f, 0.33f, 1f, 0.98f);
        SetRing(_innerRing, radius, inner, t * 1.4f, 0.045f);
        SetRing(_outerRing, radius * 1.18f, new Color(0.92f, 0.14f, 1f, 0.86f), -t * 1.1f, 0.075f);
        SetRing(_vortexRing, radius * 1.48f,
            new Color(0.7f, 0.22f, 1f, stage == GalaxyStage.Darkness ? 0.15f : 0.7f),
            t * 1.9f, 0.13f);
        float shockAlpha = stage == GalaxyStage.Burst ? 1f :
            stage == GalaxyStage.Darkness ? Mathf.Clamp01((5.05f - t) * 1.5f) : 0f;
        SetRing(_shockRing, radius * 1.06f,
            new Color(1f, 0.88f, 1f, shockAlpha), -t * 0.3f, 0.018f);
        RenderRifts(t, stage, radius);
        for (int i = 0; i < FragmentCount; i++)
        {
            float angle = i * (Mathf.PI * 2f / FragmentCount) + t * (i % 2 == 0 ? 2.4f : -1.9f);
            float distance = stage == GalaxyStage.Burst || stage == GalaxyStage.Darkness ?
                Mathf.Min(_screenRadius * 1.4f, radius * (0.34f + Hash(i, 4) * 0.66f)) :
                Mathf.Max(0.08f, _screenRadius * (0.3f + Hash(i, 5) * 0.8f) *
                    Mathf.Pow(1f - Mathf.Clamp01(t / 3.55f), 1.4f));
            Transform fragment = _fragments[i].transform;
            fragment.localPosition = new Vector3(Mathf.Cos(angle) * distance,
                Mathf.Sin(angle) * distance, 0f);
            fragment.localScale = Vector3.one * (0.16f + i % 4 * 0.09f);
        }
    }

    private void RenderRifts(float t, GalaxyStage stage, float radius)
    {
        for (int i = 0; i < RiftCount; i++)
        {
            float reveal = Mathf.Clamp01((t - 0.65f - i * 0.035f) / 0.27f);
            float size = (0.9f + Hash(i, 3) * 1.6f) * reveal;
            SpriteRenderer rift = _rifts[i];
            rift.transform.localPosition = _riftOrigins[i];
            rift.transform.localScale = new Vector3(size, size * (0.7f + Hash(i, 6) * 0.65f), 1f);
            rift.color = new Color(0.01f, 0f, 0.025f, reveal * 0.96f);
            SpriteRenderer shard = _riftShards[i];
            float travel = Mathf.Clamp01((t - 1.0f - i * 0.035f) / 1.7f);
            Vector3 origin = _riftOrigins[i];
            if (stage == GalaxyStage.Burst || stage == GalaxyStage.Darkness)
            {
                float blast = Mathf.Clamp01((t - 3.55f) / 0.8f);
                shard.transform.localPosition = origin.normalized *
                    (radius * (0.4f + Hash(i, 7) * 0.65f));
                shard.transform.localScale = Vector3.one *
                    (0.2f + (1f - blast) * (0.35f + Hash(i, 8) * 0.55f));
            }
            else
            {
                float spiral = t * (1.5f + Hash(i, 9));
                float contraction = 1f - travel * travel;
                shard.transform.localPosition = new Vector3(
                    (origin.x * Mathf.Cos(spiral) - origin.y * Mathf.Sin(spiral)) * contraction,
                    (origin.x * Mathf.Sin(spiral) + origin.y * Mathf.Cos(spiral)) * contraction, 0f);
                shard.transform.localScale = Vector3.one *
                    (0.25f + Hash(i, 8) * 0.55f) * reveal * (1f - travel * 0.65f);
            }
        }
    }

    private static void SetRing(LineRenderer ring, float radius, Color color, float rotation, float jagged)
    {
        ring.startColor = ring.endColor = color;
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments + rotation;
            float uneven = radius * (1f + jagged * Mathf.Sin(angle * 9f + rotation * 3f));
            ring.SetPosition(i, new Vector3(
                Mathf.Round(Mathf.Cos(angle) * uneven * 8f) / 8f,
                Mathf.Round(Mathf.Sin(angle) * uneven * 8f) / 8f, 0f));
        }
    }

    private static float Hash(int index, int salt)
    {
        uint value = (uint)(index * 747796405 + salt * 289133645);
        value ^= value >> 16;
        value *= 2246822519u;
        value ^= value >> 13;
        return (value & 65535u) / 65535f;
    }

    private static Texture2D CreateGlowTexture()
    {
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "PixelLiveMod Galaxy Pixel Corona"
        };
        Color32[] pixels = new Color32[32 * 32];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 16f;
                float dy = (y - 15.5f) / 16f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = distance > 1f ? (byte)0 : distance > 0.78f ? (byte)44 :
                    distance > 0.55f ? (byte)105 : distance > 0.32f ? (byte)185 : (byte)235;
                pixels[y * 32 + x] = distance < 0.32f ?
                    new Color32(235, 160, 255, alpha) :
                    new Color32(139, 35, 255, alpha);
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    public void Dispose()
    {
        if (_shake != null)
        {
            _shake.Stop();
            UnityEngine.Object.Destroy(_shake);
        }
        UnityEngine.Object.Destroy(_worldRoot);
        UnityEngine.Object.Destroy(_canvasRoot);
        UnityEngine.Object.Destroy(_pixel);
        UnityEngine.Object.Destroy(_texture);
        UnityEngine.Object.Destroy(_glowSprite);
        UnityEngine.Object.Destroy(_glowTexture);
        UnityEngine.Object.Destroy(_material);
        GamePixelFont.Release();
    }
}
