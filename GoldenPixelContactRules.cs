// Pure checks shared by the Unity collision patch and its simulated tests.
internal static class GoldenPixelContactRules
{
    internal const int GoldenPixelType = 12345;

    internal static bool IsGoldenTarget(bool directBallCollision, int tileType,
        int health, int x, int y, int goldenCount, int expectedX, int expectedY)
        => directBallCollision && tileType == GoldenPixelType &&
           health >= 1 && health <= goldenCount &&
           x == expectedX && y == expectedY;
}

internal sealed class GoldenPixelCompletionGate
{
    private bool _claimed;

    internal bool TryClaim()
    {
        if (_claimed) return false;
        _claimed = true;
        return true;
    }

    internal void Release() => _claimed = false;
    internal void NewRound() => _claimed = false;
}
