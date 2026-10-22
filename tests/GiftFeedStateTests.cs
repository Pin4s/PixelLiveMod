using System;

internal static class GiftFeedStateTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new Exception(message); }

    private static void Main()
    {
        var state = new GiftFeedState(5f, 0.2f, 0.4f, 5);
        var first = state.Apply("batch-a", "user-1", "Fulano", "rose", "Rose", 1, "Orange", 1, 0f);
        Check(first != null && state.Entries.Count == 1, "one gift creates one entry");
        state.Tick(0.2f, 0.2f);
        Check(first!.Opacity > 0.99f, "fade in completes");
        var same = state.Apply("batch-b", "user-1", "Fulano", "rose", "Rose", 3, "Orange", 3, 2f);
        Check(ReferenceEquals(first, same) && state.Entries.Count == 1, "same user and gift reuse entry");
        Check(first.Gifts == 4 && first.Balls == 4, "counts accumulate");
        state.Tick(2.05f, 0.05f);
        Check(first.Opacity > 0.99f, "updating an active entry does not replay fade-in");
        state.Apply("batch-b", "user-1", "Fulano", "rose", "Rose", 3, "Orange", 2, 2.1f);
        Check(first.Gifts == 4 && first.Balls == 6, "split command adds balls once per completion and gifts once per batch");
        Check(state.Apply("batch-failed", "user-1", "Fulano", "rose", "Rose", 9, "Orange", 0, 2.2f) == null && first.Gifts == 4,
            "zero created never attributes gifts or balls");
        state.Apply("batch-c", "user-2", "Fulano", "rose", "Rose", 1, "Orange", 1, 2.3f);
        state.Apply("batch-d", "user-1", "Fulano", "heart", "Heart Me", 1, "Pink", 1, 2.4f);
        Check(state.Entries.Count == 3, "distinct IDs and gifts stay separate even with same display name");
        float oldPosition = first.Position;
        state.Tick(2.5f, 0.1f);
        Check(first.Position < oldPosition, "older entries slide upward when new gifts arrive");
        state.Tick(6.9f, 0.1f);
        Check(state.Entries.Count == 3, "last update renewed lifetime");
        state.Tick(7.2f, 0.1f);
        Check(first.Opacity < 1f && first.Opacity > 0f, "fade out begins after duration");
        state.Tick(8f, 0.1f);
        Check(state.Entries.Count == 0, "entries removed after fade out");
        var fresh = state.Apply("batch-e", "user-1", "Fulano", "rose", "Rose", 1, "Orange", 1, 8f);
        Check(!ReferenceEquals(first, fresh), "expired entry is recreated");
        for (int i = 0; i < 8; i++)
            state.Apply("extra-" + i, "viewer-" + i, "Viewer", "rose", "Rose", 1, "Orange", 1, 9f + i);
        Check(state.Entries.Count == 5, "visible entries capped at five");
        Check(state.Apply("", "", "", "", "", 1, "Orange", 1, 18f) == null,
            "commands without gift identity do not make feed entries");
        Check(GiftFeedNames.BallName("Pink", 1) == "Bola Roxa" &&
            GiftFeedNames.BallName("Pink", 5) == "Bolas Roxas" &&
            GiftFeedNames.BallName("Inferno", 3) == "Inferno Balls", "viewer-facing ball names are singular/plural");
        Check(GiftFeedNames.GiftName("Rose", 1) == "Rosa" &&
            GiftFeedNames.GiftName("Rose", 4) == "Rosas" &&
            GiftFeedNames.GiftName("Hand Heart", 2) == "Hand Hearts", "gift names are readable and pluralized");
        var partial = new GiftFeedState(5f, 0.2f, 0.4f, 5);
        var partialEntry = partial.Apply("partial", "viewer", "Viewer", "rose", "Rose", 5, "Orange", 2, 0f);
        Check(partialEntry != null && partialEntry.Gifts == 5 && partialEntry.Balls == 2,
            "partial spawn displays all received gifts and only created balls");
        Console.WriteLine($"GiftFeedState: {_checks} checks passed");
    }
}
