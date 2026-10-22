using BepInEx.Configuration;

// Configuration belongs to the plugin boundary. Runtime systems receive an
// immutable snapshot and do not depend on BepInEx or keyboard input.
public sealed class InfernoSettings
{
    public readonly ConfigEntry<int> Radius;
    public readonly ConfigEntry<int> Damage;
    public readonly ConfigEntry<bool> ParticlesEnabled;
    public readonly ConfigEntry<int> MaxParticles;
    public readonly ConfigEntry<int> TilesPerFrame;
    public readonly ConfigEntry<float> Duration;

    private InfernoSettings(ConfigFile config)
    {
        Radius = config.Bind("Inferno", "InfernoExplosionRadius", 100,
            "Effective circular radius in world cells. Valid range: 1..150.");
        Damage = config.Bind("Inferno", "InfernoDamage", 1000000,
            "Native damage to each ordinary tile in the circle. Valid range: 1..1000000.");
        ParticlesEnabled = config.Bind("Inferno", "InfernoParticlesEnabled", true,
            "Enable the bounded sparks, embers and smoke systems.");
        MaxParticles = config.Bind("Inferno", "InfernoMaxParticles", 320,
            "Maximum explosion particles per detonation, shared by all emitters. Valid range: 0..1000.");
        TilesPerFrame = config.Bind("Inferno", "InfernoTilesPerFrame", 500,
            "Maximum candidate cells processed per frame per active explosion. Valid range: 16..2000.");
        Duration = config.Bind("Inferno", "InfernoExplosionDuration", 1.25f,
            "Target outward expansion time in seconds. Actual duration can be longer due to the tile budget. Valid range: 0.1..10.");
    }

    public static InfernoSettings Bind(ConfigFile config) => new InfernoSettings(config);

    public bool TrySnapshot(out InfernoParameters parameters, out string reason)
    {
        parameters = default;
        int radius = Radius.Value;
        int damage = Damage.Value;
        int maxParticles = MaxParticles.Value;
        int tilesPerFrame = TilesPerFrame.Value;
        float duration = Duration.Value;
        if (radius < 1 || radius > 150 || damage < 1 || damage > 1000000 ||
            maxParticles < 0 || maxParticles > 1000 || tilesPerFrame < 16 ||
            tilesPerFrame > 2000 || float.IsNaN(duration) || float.IsInfinity(duration) ||
            duration < 0.1f || duration > 10f)
        {
            reason = "invalid Inferno configuration; allowed ranges are documented in the .cfg";
            return false;
        }

        parameters = new InfernoParameters(radius, damage, ParticlesEnabled.Value,
            maxParticles, tilesPerFrame, duration);
        reason = "";
        return true;
    }
}

public readonly struct InfernoParameters
{
    public readonly int Radius;
    public readonly int Damage;
    public readonly bool ParticlesEnabled;
    public readonly int MaxParticles;
    public readonly int TilesPerFrame;
    public readonly float Duration;

    public InfernoParameters(int radius, int damage, bool particlesEnabled,
        int maxParticles, int tilesPerFrame, float duration)
    {
        Radius = radius;
        Damage = damage;
        ParticlesEnabled = particlesEnabled;
        MaxParticles = maxParticles;
        TilesPerFrame = tilesPerFrame;
        Duration = duration;
    }
}
