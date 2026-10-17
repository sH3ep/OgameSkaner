using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services;
using static OgameSkaner.Core.Tests.Fakes.TestData;

namespace OgameSkaner.Core.Tests.Services;

public class HistoryTextTests
{
    private static readonly Coordinates Slot = new(1, 2, 3);

    [Fact]
    public void Slot_changes_are_described_newest_first()
    {
        var entries = HistoryText.Slot(
        [
            new SlotObservation(Slot, ScanTime, "A", "a", false),
            new SlotObservation(Slot, ScanTime.AddDays(1), "A", "a", true),
            new SlotObservation(Slot, ScanTime.AddDays(2), "B", "b", true),
            new SlotObservation(Slot, ScanTime.AddDays(3), null, null, false),
            new SlotObservation(Slot, ScanTime.AddDays(4), "C", "c", false)
        ]);

        Assert.Equal(
            ["Colonized by C", "Free (abandoned or destroyed)", "Taken over by B", "Moon appeared", "A (first seen)"],
            entries.Select(e => e.Text));
        Assert.Equal(ScanTime.AddDays(4), entries[0].At);
    }

    [Fact]
    public void Player_changes_list_what_changed()
    {
        var entries = HistoryText.Player(
        [
            new PlayerObservation("p", "Kowalski", ScanTime, PlayerStatus.None, 100),
            new PlayerObservation("p", "Kowalski", ScanTime.AddDays(1), PlayerStatus.Inactive, 120),
            new PlayerObservation("p", "Nowak", ScanTime.AddDays(2), PlayerStatus.Inactive, 120)
        ]);

        Assert.Equal(
            ["Renamed Kowalski → Nowak", "Status active → i, rank #100 → #120", "Kowalski: active, rank #100 (first seen)"],
            entries.Select(e => e.Text));
    }
}
