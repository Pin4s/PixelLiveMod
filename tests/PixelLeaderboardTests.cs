using System;
using System.Collections.Generic;

internal static class PixelLeaderboardTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }

    private static void Hit(PixelLeaderboardService board, PixelParticipant? owner,
        int oldType = 1, int newType = 0)
    {
        using (PixelAttributionContext.Begin(owner))
            board.RecordTransition(PixelAttributionContext.Current, oldType, newType);
    }

    private static void Main()
    {
        Check(BridgeSourcePolicy.MayOwnPixels(null) && BridgeSourcePolicy.MayOwnPixels("gift") &&
            BridgeSourcePolicy.MayOwnPixels("likes") && !BridgeSourcePolicy.MayOwnPixels("follow"),
            "gift and likes bridge commands may own destroyed pixels; follow may not");
        var likesBoard = new PixelLeaderboardService();
        PixelParticipant likesOwner = likesBoard.Register("viewer-likes", "João");
        Hit(likesBoard, likesOwner);
        Hit(likesBoard, likesOwner);
        Hit(likesBoard, null); // Follow and hotkey balls have no owner.
        Check(likesOwner.Pixels == 2 && likesBoard.Top(5)[0].Id == "viewer-likes",
            "owned like balls score actual destroyed pixels through the existing leaderboard");
        var board = new PixelLeaderboardService();
        PixelParticipant first = board.Register("viewer-1", "igual");
        PixelParticipant second = board.Register("viewer-2", "igual");
        Check(!ReferenceEquals(first, second) && board.Top(5).Count == 0,
            "stable IDs separate identical display names; empty podium stays empty");
        for (int i = 0; i < 100; i++) Hit(board, first);
        Check(first.Pixels == 100, "one ball's 100 destroyed pixels yield 100 points");
        for (int i = 0; i < 50; i++) Hit(board, first);
        Check(first.Pixels == 150, "another ball of the same viewer accumulates");
        for (int i = 0; i < 70; i++) Hit(board, second);
        Check(first.Pixels == 150 && second.Pixels == 70, "viewers never share points");
        long oldVersion = board.Version;
        Hit(board, first, 1, 1); // damage without destruction
        Hit(board, first, 0, 0); // tile already empty
        Hit(board, null); // hotkey or ordinary game ball
        Check(first.Pixels == 150 && board.Version == oldVersion,
            "damage, duplicate breaks, and unowned balls score nothing");

        // Yellow strike and Green poison continuation use retained owner references.
        PixelParticipant yellowEffectOwner = first;
        PixelParticipant poisonEffectOwner = second;
        Hit(board, yellowEffectOwner);
        Hit(board, poisonEffectOwner);
        Check(first.Pixels == 151 && second.Pixels == 71,
            "indirect effects retain the originating viewer");
        using (PixelAttributionContext.Begin(first))
        {
            Hit(board, second);
            board.RecordTransition(PixelAttributionContext.Current, 1, 0);
        }
        Check(first.Pixels == 152 && second.Pixels == 72,
            "nested simultaneous sources restore their prior attribution");
        Check(PixelAttributionContext.Current == null, "scope exits without leaking an owner");

        PixelParticipant infernoOwner = board.Register("inferno", "Inferno");
        for (int i = 0; i < 12450; i++) Hit(board, infernoOwner);
        for (int i = 0; i < 100; i++) Hit(board, infernoOwner, 1, 1);
        Check(infernoOwner.Pixels == 12450, "Inferno awards only 12450 actual transitions");

        var rank = new PixelLeaderboardService();
        var entrants = new List<PixelParticipant>();
        for (int i = 0; i < 6; i++) entrants.Add(rank.Register("id-" + i, "Viewer"));
        for (int i = 0; i < 5; i++)
            for (int n = 0; n < 5 - i; n++) Hit(rank, entrants[i]);
        Check(rank.Top(5).Count == 5 && rank.Top(5)[4].Id == "id-4",
            "only five ranked participants are displayed");
        for (int i = 0; i < 6; i++) Hit(rank, entrants[5]);
        Check(rank.Top(5)[0].Id == "id-5" && rank.Top(5).Count == 5,
            "sixth viewer enters after overtaking fifth");
        var ties = new PixelLeaderboardService();
        PixelParticipant earlier = ties.Register("earlier", "Same");
        PixelParticipant later = ties.Register("later", "Same");
        Hit(ties, earlier);
        Hit(ties, later);
        Check(ties.Top(2)[0].Id == "earlier", "earlier score attainment wins a tie");
        Check(PixelLeaderboardFormat.Score(12430) == "12.430 px", "scores use Portuguese thousands separator");
        Check(PixelLeaderboardFormat.Name("<b>nome</b>", 22) == "<b>nome</b>" &&
            PixelLeaderboardFormat.Name(new string('x', 30), 22).EndsWith("…"),
            "names remain literal and are truncated safely");
        PixelParticipant retained = first;
        Check(retained.Pixels == 152, "destroying a ball cannot erase accumulated score");
        TestLifecycle();
        Console.WriteLine($"PixelLeaderboard: {_checks} checks passed");
    }

    private static void TestLifecycle()
    {
        var board = new PixelLeaderboardService();
        var lifecycle = new PixelLeaderboardLifecycle(board);
        var participants = new List<PixelParticipant>();
        for (int i = 0; i < 7; i++)
        {
            PixelParticipant participant = board.Register("reset-" + i, "Viewer " + i);
            participants.Add(participant);
            Hit(board, participant);
        }
        Check(board.Top(5).Count == 5, "seven scorers occupy five visible rows");
        int resetNotifications = 0;
        int visibleAfterReset = -1;
        board.ResetPerformed += () =>
        {
            resetNotifications++;
            visibleAfterReset = board.Top(5).Count;
        };

        // The menu prompt and cancel never call the confirmed-reset callback.
        long beforeCancel = board.Version;
        Check(board.Top(5).Count == 5 && board.Version == beforeCancel,
            "opening or cancelling confirmation preserves ranking");
        lifecycle.OnConfirmedReset();
        Check(resetNotifications == 1 && visibleAfterReset == 0 && board.Top(5).Count == 0,
            "confirmed reset immediately presents an empty podium");
        Check(participants.TrueForAll(p => p.Pixels == 0 && p.ReachedOrder == 0),
            "all seven viewers, including hidden scorers, are zeroed");
        Check(!board.RecordTransition(participants[0], 1, 0),
            "a late old ball or poison effect cannot repopulate ranking");
        PixelParticipant fresh = board.Register("reset-0", "Viewer 0");
        Check(!ReferenceEquals(fresh, participants[0]) && board.RecordTransition(fresh, 1, 0) &&
            fresh.Pixels == 1 && board.Top(5).Count == 1,
            "a new gift after reset starts at one pixel");

        // Autosave and ordinary scene loads do not invoke lifecycle callbacks.
        long beforeOrdinaryEvents = board.Version;
        Check(board.Version == beforeOrdinaryEvents && fresh.Pixels == 1,
            "ordinary save and load leave accounting intact");
        object ending = new object();
        Check(lifecycle.OnVictory(ending) && board.Top(5).Count == 0 &&
            resetNotifications == 2, "native victory clears once");
        Check(!lifecycle.OnVictory(ending) && resetNotifications == 2,
            "repeated completion from the same ending is harmless");
        Check(!board.RecordTransition(fresh, 1, 0) && board.Top(5).Count == 0,
            "late destruction after victory stays invalid");
        PixelParticipant next = board.Register("reset-0", "Viewer 0");
        Hit(board, next);
        Check(next.Pixels == 1, "new round contributions score normally");
        Check(lifecycle.OnVictory(new object()) && next.Pixels == 0,
            "a distinct native ending can clear a later round");
    }
}
