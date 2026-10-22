using UnityEngine;

// Exists only on ball instances created from a TikTok gift with a stable viewer ID.
public sealed class PixelBallOwner : MonoBehaviour
{
    internal PixelParticipant? Participant { get; private set; }
    internal void Initialize(PixelParticipant participant) { Participant = participant; }
}
