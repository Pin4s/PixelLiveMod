using System;
using BepInEx.Logging;
using UnityEngine;

// A handful of bounded Unity renderers/particle systems, never one object per
// destroyed cell. Point-filtered procedural sprites keep the game's crisp look.
public sealed class InfernoExplosionVFX : MonoBehaviour
{
    private const int RingSegments = 96;
    private InfernoParameters _parameters;
    private ManualLogSource? _log;
    private Material? _material;
    private Texture2D? _flashTexture;
    private Sprite? _flashSprite;
    private SpriteRenderer? _flash;
    private LineRenderer? _ringOuter;
    private LineRenderer? _ringInner;
    private ParticleSystem? _sparks;
    private ParticleSystem? _embers;
    private ParticleSystem? _smoke;
    private readonly Vector3[] _ringPositions = new Vector3[RingSegments + 1];
    private int _emitted;
    private float _previousProgress;
    private float _fullReachedAt = -1f;

    public static void AttachAura(GameObject ball, ManualLogSource log)
    {
        try
        {
            GameObject aura = new GameObject("Inferno ember aura");
            aura.transform.SetParent(ball.transform, false);
            ParticleSystem system = aura.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 0.38f;
            main.startSpeed = 0.8f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.18f, 0.02f, 0.6f),
                new Color(1f, 0.8f, 0.16f, 0.9f));
            main.maxParticles = 24;
            var emission = system.emission;
            emission.rateOverTime = 18f;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.35f;
            InfernoAuraResources resources = aura.AddComponent<InfernoAuraResources>();
            resources.Initialize();
            system.Play();
        }
        catch (Exception exception)
        {
            log.LogWarning($"Inferno moving aura unavailable; ball physics unaffected: {exception.Message}");
        }
    }

    public void Initialize(InfernoParameters parameters, ManualLogSource log)
    {
        _parameters = parameters;
        _log = log;
        try
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _material = new Material(shader) { name = "PixelLiveMod Inferno VFX" };
                _material.mainTexture = Texture2D.whiteTexture;
            }

            CreateFlash();
            _ringOuter = CreateRing("Inferno shockwave red", 2.8f);
            _ringInner = CreateRing("Inferno shockwave gold", 1.25f);
            if (parameters.ParticlesEnabled && parameters.MaxParticles > 0)
            {
                _sparks = CreateParticles("Inferno fire sparks", parameters.MaxParticles);
                _embers = CreateParticles("Inferno embers", parameters.MaxParticles);
                _smoke = CreateParticles("Inferno smoke", parameters.MaxParticles);
                var trails = _sparks.trails;
                trails.enabled = true;
                trails.ratio = 0.2f;
                trails.lifetime = 0.18f;
            }

            SetProgress(0f, 0f);
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Inferno VFX initialization incomplete; native damage remains enabled: {exception}");
        }
    }

    public void SetProgress(float progress, float elapsed)
    {
        progress = Mathf.Clamp01(progress);
        if (progress >= 1f && _fullReachedAt < 0f)
            _fullReachedAt = Time.time;

        float fade = _fullReachedAt >= 0f
            ? Mathf.Clamp01(1f - (Time.time - _fullReachedAt) / 1.5f)
            : 1f;
        if (_flash != null)
        {
            float flashTime = Mathf.Clamp01(elapsed / 0.28f);
            _flash.transform.localScale = Vector3.one * Mathf.Lerp(6f,
                Mathf.Min(45f, _parameters.Radius * 0.6f), flashTime);
            _flash.color = new Color(1f, Mathf.Lerp(1f, 0.46f, flashTime),
                Mathf.Lerp(0.72f, 0.04f, flashTime),
                Mathf.Clamp01(1f - flashTime));
        }

        float radius = Mathf.Max(0.02f, progress * _parameters.Radius);
        SetRing(_ringOuter, radius, new Color(1f, 0.19f, 0.02f,
            (0.82f - progress * 0.32f) * fade));
        SetRing(_ringInner, Mathf.Max(0.01f, radius - 1.1f),
            new Color(1f, 0.78f, 0.15f, (0.95f - progress * 0.25f) * fade));

        if (_sparks != null && _embers != null && _smoke != null)
            EmitToProgress(progress);
        _previousProgress = progress;
    }

    private void CreateFlash()
    {
        const int size = 32;
        _flashTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "PixelLiveMod Inferno radial flash"
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                alpha *= alpha;
                pixels[y * size + x] = new Color32(255, 239, 155, (byte)(alpha * 255f));
            }
        }
        _flashTexture.SetPixels32(pixels);
        _flashTexture.Apply(false, true);
        _flashSprite = Sprite.Create(_flashTexture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size);
        GameObject flashObject = new GameObject("Inferno white-hot flash");
        flashObject.transform.SetParent(transform, false);
        flashObject.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        _flash = flashObject.AddComponent<SpriteRenderer>();
        _flash.sprite = _flashSprite;
        _flash.sortingOrder = 100;
        if (_material != null)
            _flash.sharedMaterial = _material;
    }

    private LineRenderer CreateRing(string label, float width)
    {
        GameObject ringObject = new GameObject(label);
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 0f, -0.15f);
        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.positionCount = RingSegments + 1;
        ring.widthMultiplier = width;
        ring.numCapVertices = 0;
        ring.numCornerVertices = 0;
        ring.sortingOrder = 99;
        if (_material != null)
            ring.sharedMaterial = _material;
        return ring;
    }

    private ParticleSystem CreateParticles(string label, int maximum)
    {
        GameObject objectWithSystem = new GameObject(label);
        objectWithSystem.transform.SetParent(transform, false);
        ParticleSystem system = objectWithSystem.AddComponent<ParticleSystem>();
        var main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maximum;
        main.startSpeed = 0f;
        main.startLifetime = 2f;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var colors = system.colorOverLifetime;
        colors.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.35f),
                new GradientAlphaKey(0f, 1f) });
        colors.color = new ParticleSystem.MinMaxGradient(fade);
        ParticleSystemRenderer renderer = objectWithSystem.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = 101;
        if (_material != null)
            renderer.sharedMaterial = _material;
        system.Play();
        return system;
    }

    private void SetRing(LineRenderer? ring, float radius, Color color)
    {
        if (ring == null)
            return;
        for (int index = 0; index <= RingSegments; index++)
        {
            float angle = index * (Mathf.PI * 2f / RingSegments);
            _ringPositions[index] = new Vector3(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius, 0f);
        }
        ring.SetPositions(_ringPositions);
        ring.startColor = color;
        ring.endColor = color;
    }

    private void EmitToProgress(float progress)
    {
        int target = Mathf.FloorToInt(progress * _parameters.MaxParticles);
        while (_emitted < target)
        {
            int ordinal = _emitted++;
            float distance = Mathf.Lerp(_previousProgress, progress, UnityEngine.Random.value) *
                _parameters.Radius;
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            ParticleSystem.EmitParams emission = new ParticleSystem.EmitParams
            {
                position = transform.position + (Vector3)(direction * distance),
                velocity = direction * UnityEngine.Random.Range(2f, 12f),
                startSize = UnityEngine.Random.Range(0.18f, 0.85f),
                startLifetime = UnityEngine.Random.Range(0.55f, 1.6f)
            };

            if (ordinal % 4 < 2)
            {
                emission.startColor = UnityEngine.Random.value < 0.5f
                    ? new Color(1f, 0.74f, 0.12f, 0.95f)
                    : new Color(1f, 0.18f, 0.02f, 0.95f);
                _sparks!.Emit(emission, 1);
            }
            else if (ordinal % 4 == 2)
            {
                emission.velocity = direction * UnityEngine.Random.Range(0.6f, 4f);
                emission.startColor = new Color(0.95f, 0.28f, 0.07f, 0.8f);
                emission.startLifetime = UnityEngine.Random.Range(1f, 2.2f);
                _embers!.Emit(emission, 1);
            }
            else
            {
                emission.velocity = direction * UnityEngine.Random.Range(0.2f, 1.8f) +
                    Vector2.up * 1.4f;
                emission.startColor = new Color(0.32f, 0.25f, 0.23f, 0.42f);
                emission.startSize = UnityEngine.Random.Range(0.7f, 2.3f);
                emission.startLifetime = UnityEngine.Random.Range(1.4f, 2.5f);
                _smoke!.Emit(emission, 1);
            }
        }
    }

    private void OnDestroy()
    {
        if (_flashSprite != null)
            Destroy(_flashSprite);
        if (_flashTexture != null)
            Destroy(_flashTexture);
        if (_material != null)
            Destroy(_material);
    }
}

public sealed class InfernoAuraResources : MonoBehaviour
{
    private Material? _material;

    public void Initialize()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return;
        _material = new Material(shader) { name = "PixelLiveMod Inferno aura" };
        _material.mainTexture = Texture2D.whiteTexture;
        ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = _material;
        renderer.sortingOrder = 100;
    }

    private void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }
}
