using UnityEngine;

// Camera render callbacks keep the offset out of gameplay and camera logic.
// Each render starts from the camera's actual position and restores it afterward.
internal sealed class GalaxyCameraShake : MonoBehaviour
{
    private Camera? _camera;
    private float _startedAt;
    private Vector3 _originalPosition;
    private bool _offsetApplied;

    internal void Configure(Camera camera, float startedAt)
    {
        _camera = camera;
        _startedAt = startedAt;
    }

    private void OnPreCull()
    {
        Restore();
        if (_camera == null) return;
        float elapsed = Time.realtimeSinceStartup - _startedAt;
        float fraction = GalaxyResetTimeline.ShakeFraction(elapsed);
        if (fraction <= 0f) return;
        Transform view = _camera.transform;
        _originalPosition = view.position;
        float size = _camera.orthographic ? _camera.orthographicSize : 12f;
        float amplitude = size * fraction;
        view.position = _originalPosition + view.right * (Mathf.Sin(elapsed * 39f) * amplitude) +
            view.up * (Mathf.Sin(elapsed * 33f + 1.2f) * amplitude * 0.7f);
        _offsetApplied = true;
    }

    private void OnPostRender() => Restore();
    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    internal void Stop()
    {
        Restore();
        enabled = false;
    }

    private void Restore()
    {
        if (!_offsetApplied) return;
        if (_camera != null) _camera.transform.position = _originalPosition;
        _offsetApplied = false;
    }
}
