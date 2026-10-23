using System;

internal enum GalaxyStage { Birth, Collapse, Contraction, Burst, Darkness, Complete }

internal static class GalaxyResetTimeline
{
    internal const float Duration = 5.2f;
    // CameraController.orthoSize serialized on the Main Camera in level0.
    // CameraController.Start applies this to Camera.orthographicSize.
    internal const float DefaultOrthographicSize = 8f;

    internal static float ZoomScale(float orthographicSize) =>
        orthographicSize > 0f ? orthographicSize / DefaultOrthographicSize : 1f;

    internal static GalaxyStage Stage(float seconds) =>
        seconds < 1.2f ? GalaxyStage.Birth :
        seconds < 3.1f ? GalaxyStage.Collapse :
        seconds < 3.55f ? GalaxyStage.Contraction :
        seconds < 4.35f ? GalaxyStage.Burst :
        seconds < Duration ? GalaxyStage.Darkness : GalaxyStage.Complete;

    internal static float OverloadFlash(float seconds)
    {
        if (seconds < 2.25f || seconds >= 3.55f) return 0f;
        return 0.18f * (float)Math.Pow(Math.Max(0d, Math.Sin(seconds * 22d)), 8d);
    }

    internal static float BurstFlash(float seconds)
    {
        if (seconds < 3.55f || seconds >= 4.35f) return 0f;
        return 0.58f * (float)Math.Exp(-(seconds - 3.55f) * 9d);
    }

    // Fraction of orthographic camera height. Smooth ramp, a short impact,
    // then no displacement once the blackout begins.
    internal static float ShakeFraction(float seconds)
    {
        if (seconds < 1.2f || seconds >= 4.35f) return 0f;
        if (seconds < 3.1f)
        {
            float p = (seconds - 1.2f) / 1.9f;
            return 0.002f + 0.009f * p * p;
        }
        if (seconds < 3.55f)
            return 0.011f + 0.007f * (seconds - 3.1f) / 0.45f;
        float burst = (seconds - 3.55f) / 0.8f;
        return 0.018f + 0.01f * (float)Math.Exp(-burst * 5d);
    }
}
