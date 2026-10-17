using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Services;

/// <summary>One line of a history: when and what changed.</summary>
public sealed record HistoryEntry(DateTimeOffset At, string Text);

/// <summary>Turns the stored observations into readable changes, newest first.</summary>
public static class HistoryText
{
    public static IReadOnlyList<HistoryEntry> Slot(IReadOnlyList<SlotObservation> history)
    {
        var entries = new List<HistoryEntry>();
        SlotObservation? before = null;
        foreach (var now in history)
        {
            var text = (before, now) switch
            {
                (_, { IsFree: true }) => "Free (abandoned or destroyed)",
                (null, _) => $"{now.PlayerName}{(now.HasMoon ? " with moon" : "")} (first seen)",
                ({ IsFree: true }, _) => $"Colonized by {now.PlayerName}",
                _ when !SameOwner(before, now) => $"Taken over by {now.PlayerName}",
                _ when now.HasMoon != before.HasMoon => now.HasMoon ? "Moon appeared" : "Moon gone",
                _ => $"{now.PlayerName}"
            };
            entries.Add(new HistoryEntry(now.ObservedAt, text));
            before = now;
        }

        entries.Reverse();
        return entries;
    }

    public static IReadOnlyList<HistoryEntry> Player(IReadOnlyList<PlayerObservation> history)
    {
        var entries = new List<HistoryEntry>();
        PlayerObservation? before = null;
        foreach (var now in history)
        {
            if (before is null)
            {
                entries.Add(new HistoryEntry(now.ObservedAt, $"{now.PlayerName}: {Describe(now.Status, now.Rank)} (first seen)"));
            }
            else
            {
                var changes = new List<string>();
                if (now.PlayerName != before.PlayerName)
                    changes.Add($"renamed {before.PlayerName} → {now.PlayerName}");
                if (now.Status != before.Status)
                    changes.Add($"status {Letters(before.Status)} → {Letters(now.Status)}");
                if (now.Rank != before.Rank)
                    changes.Add($"rank {Rank(before.Rank)} → {Rank(now.Rank)}");
                entries.Add(new HistoryEntry(now.ObservedAt, Capitalize(string.Join(", ", changes.DefaultIfEmpty("no change")))));
            }

            before = now;
        }

        entries.Reverse();
        return entries;
    }

    private static bool SameOwner(SlotObservation before, SlotObservation now) =>
        before.PlayerId is not null && now.PlayerId is not null ? before.PlayerId == now.PlayerId : before.PlayerName == now.PlayerName;

    private static string Describe(PlayerStatus status, int? rank) => $"{Letters(status)}, rank {Rank(rank)}";

    private static string Letters(PlayerStatus status) => status == PlayerStatus.None ? "active" : status.ToLetters();

    private static string Rank(int? rank) => rank is { } r ? $"#{r:N0}" : "?";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
