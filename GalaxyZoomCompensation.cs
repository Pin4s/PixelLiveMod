using UnityEngine;

// Galaxy world effects use the game's serialized starting zoom as their
// geometry reference. Apply one uniform scale after CameraController.Update,
// including when the player scrolls during the animation.
internal sealed class GalaxyZoomCompensation : MonoBehaviour
{
    private Camera? _camera;

    internal void Configure(Camera camera)
    {
        _camera = camera;
        Apply();
    }

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (_camera == null || !_camera.orthographic) return;
        float scale = GalaxyResetTimeline.ZoomScale(_camera.orthographicSize);
        transform.localScale = Vector3.one * scale;
    }
}
