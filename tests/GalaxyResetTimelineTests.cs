using System;

internal static class GalaxyResetTimelineTests
{
    private static void Check(float seconds, GalaxyStage expected)
    {
        if (GalaxyResetTimeline.Stage(seconds) != expected)
            throw new Exception($"At {seconds}s expected {expected}");
    }

    private static void Main()
    {
        Check(0f, GalaxyStage.Birth);
        Check(1.2f, GalaxyStage.Collapse);
        Check(3.1f, GalaxyStage.Contraction);
        Check(3.55f, GalaxyStage.Burst);
        Check(4.35f, GalaxyStage.Darkness);
        Check(5.19f, GalaxyStage.Darkness);
        Check(5.2f, GalaxyStage.Complete);
        if (GalaxyResetTimeline.ZoomScale(8f) != 1f ||
            GalaxyResetTimeline.ZoomScale(4f) != 0.5f ||
            GalaxyResetTimeline.ZoomScale(16f) != 2f ||
            GalaxyResetTimeline.ZoomScale(0f) != 1f)
            throw new Exception("World effect scale must cancel native orthographic zoom");
        float standardScreenRadius = 14.4f / (2f * 8f);
        foreach (float zoom in new[] { 4f, 8f, 16f, 32f })
        {
            float projected = 14.4f * GalaxyResetTimeline.ZoomScale(zoom) / (2f * zoom);
            if (Math.Abs(projected - standardScreenRadius) > 0.00001f)
                throw new Exception($"Projected Galaxy radius changed at zoom {zoom}");
        }
        if (GalaxyResetTimeline.OverloadFlash(2.1f) != 0f ||
            GalaxyResetTimeline.OverloadFlash(3.6f) != 0f ||
            GalaxyResetTimeline.OverloadFlash(2.5f) > 0.18f ||
            GalaxyResetTimeline.BurstFlash(3.54f) != 0f ||
            GalaxyResetTimeline.BurstFlash(3.55f) < 0.57f ||
            GalaxyResetTimeline.BurstFlash(4.35f) != 0f)
            throw new Exception("Visual flashes escaped their bounded phases");
        if (GalaxyResetTimeline.ShakeFraction(0f) != 0f ||
            GalaxyResetTimeline.ShakeFraction(1.5f) <= 0f ||
            GalaxyResetTimeline.ShakeFraction(3.4f) <= GalaxyResetTimeline.ShakeFraction(1.5f) ||
            GalaxyResetTimeline.ShakeFraction(3.55f) <= GalaxyResetTimeline.ShakeFraction(3.4f) ||
            GalaxyResetTimeline.ShakeFraction(4.35f) != 0f)
            throw new Exception("Camera shake must escalate and stop at blackout");
        Console.WriteLine("GalaxyResetTimeline: 13 checks passed");
    }
}
