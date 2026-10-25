internal static class BridgeSourcePolicy
{
    internal static bool MayOwnPixels(string? source) => source == null || source == "gift" || source == "likes";
}
