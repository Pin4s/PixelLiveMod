using System;
using System.IO;

internal static class AutoRestartSafetyTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }

    private static void Main()
    {
        bool At(float time, bool black = true, bool later = false, bool ending = true) =>
            EndingTransitionRules.Matches(ending, true, true, false, time, black, 1f, later);
        Check(!At(0.045f), "cannot transition before measured animation progress");
        Check(At(0.046f), "measured third snapshot qualifies");
        Check(!At(0.046f, black: false), "black must be visible");
        Check(!At(0.046f, later: true), "later ending animation must not have begun");
        Check(!At(0.046f, ending: false), "native victory is required");
        Check(!EndingTransitionRules.Matches(true, true, true, true, 0.046f, true, 1f, false),
            "animator transition does not qualify");
        Check(!EndingTransitionRules.Matches(true, true, true, false, 0.046f, true, 0.98f, false),
            "partially dark screen does not qualify");

        var blackout = new RestartBlackoutState();
        blackout.Cover();
        Check(blackout.Alpha == 1f && blackout.Current == RestartBlackoutState.Stage.Opaque,
            "blackout begins fully opaque");
        blackout.Advance(20f);
        Check(blackout.Alpha == 1f && !blackout.RevealWhenReady(false),
            "scene loading alone cannot reveal the old or incomplete world");
        Check(blackout.RevealWhenReady(true) && !blackout.RevealWhenReady(true),
            "ready world starts only one reveal");
        blackout.Advance(0.2f);
        Check(blackout.Alpha > 0f && blackout.Alpha < 1f,
            "new world is gradually revealed");
        blackout.Advance(1f);
        Check(blackout.Current == RestartBlackoutState.Stage.Hidden && blackout.Alpha == 0f,
            "cover releases after fade");
        blackout.Cover();
        Check(blackout.Current == RestartBlackoutState.Stage.Opaque && blackout.Alpha == 1f,
            "second round starts with a fresh opaque cover");

        string sandbox = Path.Combine(Path.GetTempPath(), "PixelLiveMod-ArchiveTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckThrows<DirectoryNotFoundException>(() => RoundSaveArchive.ArchiveActiveSave(sandbox),
                "missing source cannot silently create a new round");
            string save = Path.Combine(sandbox, "save");
            Directory.CreateDirectory(save);
            File.WriteAllText(Path.Combine(save, "chunk_0_0.dat"), "previous round");
            string first = RoundSaveArchive.ArchiveActiveSave(sandbox);
            Check(!Directory.Exists(save) && File.ReadAllText(Path.Combine(first, "chunk_0_0.dat")) == "previous round",
                "old world moves intact out of active save path");
            Directory.CreateDirectory(save);
            File.WriteAllText(Path.Combine(save, "chunk_0_0.dat"), "next round");
            string second = RoundSaveArchive.ArchiveActiveSave(sandbox);
            Check(first != second && File.ReadAllText(Path.Combine(first, "chunk_0_0.dat")) == "previous round" &&
                File.ReadAllText(Path.Combine(second, "chunk_0_0.dat")) == "next round",
                "later rounds do not overwrite earlier archives");
        }
        finally { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); }
        Console.WriteLine($"AutoRestartSafety: {_checks} checks passed");
    }

    private static void CheckThrows<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        Check(false, label);
    }
}
