using System;

internal static class GoldenPixelContactTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }

    private static void Main()
    {
        // The collision hook requires a Ball component but does not inspect ball color.
        foreach (string ball in new[] { "Orange", "Green", "Yellow", "Pink", "Golden", "Inferno" })
        {
            Check(GoldenPixelContactRules.IsGoldenTarget(true, 12345, 2, 10, 20, 3, 10, 20),
                ball + " direct Golden Pixel contact qualifies regardless of color");
        }
        Check(!GoldenPixelContactRules.IsGoldenTarget(false, 12345, 2, 10, 20, 3, 10, 20),
            "area damage and rays without collision do not qualify");
        Check(!GoldenPixelContactRules.IsGoldenTarget(true, 1, 2, 10, 20, 3, 10, 20),
            "ordinary pixel contact does not qualify");
        Check(!GoldenPixelContactRules.IsGoldenTarget(true, 1000, 2, 10, 20, 3, 10, 20),
            "another special pixel does not qualify");
        Check(!GoldenPixelContactRules.IsGoldenTarget(true, 12345, 2, 10, 21, 3, 10, 20),
            "tile coordinates must match the native Golden Pixel position");
        Check(!GoldenPixelContactRules.IsGoldenTarget(true, 12345, 0, 10, 20, 3, 10, 20),
            "invalid Golden Pixel index is rejected");

        var gate = new GoldenPixelCompletionGate();
        Check(gate.TryClaim(), "first direct collision claims completion");
        Check(!gate.TryClaim(), "simultaneous second collision cannot complete twice");
        gate.NewRound();
        Check(gate.TryClaim(), "scene change allows a later round to win");
        gate.Release();
        Check(gate.TryClaim(), "failed native collection can be retried");
        Console.WriteLine($"GoldenPixelContact: {_checks} checks passed");
    }
}
