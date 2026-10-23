using System;
using System.IO;

internal static class EndingTransitionRules
{
    internal static bool Matches(bool nativeEnding, bool statsVisible, bool expectedState,
        bool inTransition, float normalizedTime, bool blackVisible, float blackAlpha,
        bool laterEndingVisible)
        => nativeEnding && statsVisible && expectedState && !inTransition &&
           normalizedTime >= 0.046f && normalizedTime < 1f &&
           blackVisible && blackAlpha >= 0.99f && !laterEndingVisible;
}

internal static class RoundSaveArchive
{
    internal static string ArchiveActiveSave(string persistentDataPath)
    {
        string source = Path.Combine(persistentDataPath, "save");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        string archiveRoot = Path.Combine(persistentDataPath, "PixelLiveMod-RoundArchives");
        Directory.CreateDirectory(archiveRoot);
        string destination = Path.Combine(archiveRoot,
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(destination)) throw new IOException("Archive destination already exists");
        // A rename on the same volume is atomic. No save file is deleted.
        Directory.Move(source, destination);
        return destination;
    }
}
