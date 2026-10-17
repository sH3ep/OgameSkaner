using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.Core.Tests.Services;

public class RaidPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 22, 0, 0, TimeSpan.Zero);
    private static readonly RaidSettings Settings = RaidSettings.Default with { MinLoot = 1_000, MaxDefensePoints = 100, MaxFleetPoints = 0 };
    private static readonly Coordinates Target = new(5, 288, 3);
    private static readonly PlayerPlanet Inactive = Planet("Kowalski", Target, PlayerStatus.Inactive, 31);

    [Fact]
    public void Candidates_are_raidable_inactives_best_ranked_first_without_own_planets()
    {
        PlayerPlanet[] galaxy =
        [
            Planet("Active", new Coordinates(1, 1, 1), PlayerStatus.None, 1),
            Planet("Vacation", new Coordinates(1, 1, 2), PlayerStatus.Inactive | PlayerStatus.Vacation, 2),
            Planet("Noob", new Coordinates(1, 1, 3), PlayerStatus.Inactive | PlayerStatus.Noob, 3),
            Planet("Low", new Coordinates(1, 1, 4), PlayerStatus.Inactive, 500),
            Planet("Unranked", new Coordinates(1, 1, 5), PlayerStatus.LongInactive, null),
            Planet("Top", new Coordinates(2, 1, 1), PlayerStatus.LongInactive | PlayerStatus.Strong, 10),
            Planet("Me", new Coordinates(3, 3, 3), PlayerStatus.Inactive, 5)
        ];

        var candidates = RaidPlanner.Candidates(galaxy, [new Coordinates(3, 3, 3)], RaidSettings.Default);

        Assert.Equal(["Top", "Low", "Unranked"], candidates.Select(p => p.PlayerName));
    }

    [Fact]
    public void Reports_add_unscanned_planets_and_update_an_older_scan_status()
    {
        var scannedActive = Planet("WentInactive", new Coordinates(1, 1, 1), PlayerStatus.None, 7) with { ScannedAt = Now.AddDays(-1) };
        EspionageReport[] reports =
        [
            FullReport() with { Target = new Coordinates(1, 1, 1), PlayerName = "WentInactive", PlayerStatus = PlayerStatus.Inactive },
            FullReport() with { Target = new Coordinates(9, 9, 9), PlayerName = "OnlyReported", PlayerStatus = PlayerStatus.LongInactive },
            FullReport() with { Target = new Coordinates(9, 9, 8), PlayerName = "OnVacation", PlayerStatus = PlayerStatus.Inactive | PlayerStatus.Vacation }
        ];

        var candidates = RaidPlanner.Candidates([scannedActive], [], RaidSettings.Default, reports);

        Assert.Equal(["WentInactive", "OnlyReported"], candidates.Select(p => p.PlayerName));
        Assert.Equal(7, candidates[0].Rank);
    }

    [Fact]
    public void Without_a_report_the_target_needs_spying()
    {
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(null).State);
    }

    [Fact]
    public void A_fresh_report_within_the_limits_is_ready_with_half_the_resources_as_loot()
    {
        var target = Evaluate(FullReport(defense: 100));

        Assert.Equal(RaidTargetState.Ready, target.State);
        Assert.Equal(3_000, target.ExpectedLoot); // 50% of 6,000
    }

    [Theory]
    [InlineData(101, 0, RaidTargetState.TooMuchDefense)]
    [InlineData(0, 1, RaidTargetState.TooMuchFleet)]
    public void Defense_and_fleet_points_over_the_limits_reject_the_target(long defense, long fleet, RaidTargetState expected)
    {
        Assert.Equal(expected, Evaluate(FullReport(defense, fleet)).State);
    }

    [Fact]
    public void Hidden_defenses_need_more_probes()
    {
        var report = FullReport() with { Defenses = null };

        Assert.Equal(RaidTargetState.Unseen, Evaluate(report).State);
    }

    [Fact]
    public void The_defense_ranking_limit_skips_defended_and_unranked_players()
    {
        PlayerPlanet[] candidates =
        [
            Planet("NoDefense", new Coordinates(1, 1, 1), PlayerStatus.Inactive, 1) with { PlayerId = "a" },
            Planet("Defended", new Coordinates(1, 1, 2), PlayerStatus.Inactive, 2) with { PlayerId = "b" },
            Planet("Unranked", new Coordinates(1, 1, 3), PlayerStatus.Inactive, 3) with { PlayerId = "c" }
        ];
        var ranking = new RankingSnapshot(Now, new Dictionary<string, long> { ["a"] = 0, ["b"] = 500 });

        string[] Names(long? max) =>
            RaidPlanner.Evaluate(candidates, [], [], Now, Settings with { MaxRankingDefensePoints = max }, ranking).Select(t => t.Planet.PlayerName).ToArray();

        Assert.Equal(["NoDefense"], Names(0));
        Assert.Equal(["NoDefense", "Defended"], Names(500));
        Assert.Equal(["NoDefense", "Defended", "Unranked"], Names(null));
    }

    [Fact]
    public void Hidden_defenses_count_as_none_when_the_ranking_shows_no_defense_points()
    {
        var report = FullReport() with { Defenses = null };

        Assert.Equal(RaidTargetState.Ready, RaidPlanner.Evaluate(Inactive, report, [], Now, Settings, rankingDefensePoints: 0).State);
        Assert.Equal(RaidTargetState.Unseen, RaidPlanner.Evaluate(Inactive, report, [], Now, Settings, rankingDefensePoints: 5).State);
    }

    [Fact]
    public void Hidden_ships_and_defenses_count_as_none_when_the_ranking_shows_no_defense_points()
    {
        var report = FullReport() with { Ships = null, Defenses = null };

        Assert.Equal(RaidTargetState.Ready, RaidPlanner.Evaluate(Inactive, report, [], Now, Settings, rankingDefensePoints: 0).State);
        Assert.Equal(RaidTargetState.Unseen, RaidPlanner.Evaluate(Inactive, report, [], Now, Settings, rankingDefensePoints: 5).State);
        Assert.Equal(RaidTargetState.Unseen, Evaluate(report).State); // ranking not read
    }

    [Fact]
    public void A_target_hidden_from_as_many_probes_is_not_spied_on_again()
    {
        var spy = Raid(MissionType.Spy, Now.AddHours(-2), Now.AddHours(-2).AddMinutes(5)) with { Ships = Settings.ProbeCount };
        var oldHidden = FullReport(time: Now.AddHours(-2).AddMinutes(5)) with { Defenses = null };

        Assert.Equal(RaidTargetState.Unseen, Evaluate(oldHidden, spy).State);
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(oldHidden, spy with { Ships = Settings.ProbeCount - 1 }).State); // more probes now
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(oldHidden).State); // not our spy: no idea how many probes
        Assert.Equal(RaidTargetState.NeedsSpy, RaidPlanner.Evaluate(Inactive, oldHidden, [spy], Now, Settings, rankingDefensePoints: 0).State);
    }

    [Fact]
    public void Too_little_loot_is_not_worth_an_attack()
    {
        var report = FullReport() with { Resources = new ResourceSet(1_000, 0, 0) };

        Assert.Equal(RaidTargetState.TooLittleLoot, Evaluate(report).State);
    }

    [Fact]
    public void A_low_loot_report_isnt_spied_on_again_for_the_wait()
    {
        var lowLoot = FullReport(time: Now.AddHours(-2)) with { Resources = new ResourceSet(1_000, 0, 0) }; // loot 500 < 1,000

        var waiting = Evaluate(lowLoot);
        Assert.Equal(RaidTargetState.TooLittleLoot, waiting.State);
        Assert.StartsWith("spied again after", waiting.Note, StringComparison.Ordinal);

        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(lowLoot with { Time = Now.AddHours(-6) }).State); // 5 hours are over
        Assert.Equal(RaidTargetState.NeedsSpy, RaidPlanner.Evaluate(Inactive, lowLoot, [], Now, Settings with { LowLootRespyHours = 0 }).State); // off
        // An attack after the report took the loot it saw: the report says nothing about what's there now.
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(lowLoot, Raid(MissionType.Attack, Now.AddHours(-1.5), Now.AddHours(-1))).State);
    }

    [Fact]
    public void An_old_report_or_one_from_before_the_last_attack_needs_a_new_spy()
    {
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(FullReport(time: Now - Settings.ReportMaxAge - TimeSpan.FromMinutes(1))).State);

        var attack = Raid(MissionType.Attack, sentAt: Now.AddMinutes(-20), arrivesAt: Now.AddMinutes(-10));
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(FullReport(time: Now.AddMinutes(-15)), attack).State);
        Assert.Equal(RaidTargetState.Ready, Evaluate(FullReport(time: Now.AddMinutes(-5)), attack).State);
    }

    [Fact]
    public void Flying_attacks_and_spies_are_waited_for()
    {
        var attack = Raid(MissionType.Attack, sentAt: Now.AddMinutes(-1), arrivesAt: Now.AddMinutes(4));
        Assert.Equal(RaidTargetState.UnderAttack, Evaluate(FullReport(), attack).State);

        var spy = Raid(MissionType.Spy, sentAt: Now.AddSeconds(-20), arrivesAt: Now.AddSeconds(10));
        Assert.Equal(RaidTargetState.SpyPending, Evaluate(FullReport(time: Now.AddMinutes(-10)), spy).State);
        Assert.Equal(RaidTargetState.Ready, Evaluate(FullReport(time: Now), spy).State);

        // The probes arrived long ago and no report came (destroyed): spy again.
        var lostSpy = Raid(MissionType.Spy, sentAt: Now.AddMinutes(-10), arrivesAt: Now.AddMinutes(-9));
        Assert.Equal(RaidTargetState.NeedsSpy, Evaluate(null, lostSpy).State);
    }

    [Fact]
    public void A_report_showing_the_player_active_again_is_not_raidable()
    {
        Assert.Equal(RaidTargetState.NotRaidable, Evaluate(FullReport() with { PlayerStatus = PlayerStatus.Strong }).State);
    }

    [Theory]
    [InlineData(1_000_000, 25_000, 10, 44)] // 1.1M / 25k
    [InlineData(1, 25_000, 10, 1)]
    [InlineData(1_000_000, 0, 10, 0)]
    public void Ships_needed_cover_the_loot_plus_margin(long loot, long capacity, int margin, long expected)
    {
        Assert.Equal(expected, RaidPlanner.ShipsNeeded(loot, capacity, Settings with { CargoMarginPercent = margin }));
    }

    [Theory]
    [InlineData(0, 11, 90, 9)]
    [InlineData(4, 11, 90, 5)]
    [InlineData(10, 11, 90, 0)]
    [InlineData(0, 11, 100, 11)]
    public void Slot_budget_is_the_allowed_share_minus_slots_in_use(int used, int max, int percent, int expected)
    {
        Assert.Equal(expected, RaidPlanner.SlotBudget(new FleetSlots(used, max), Settings with { FleetSlotUsePercent = percent }));
    }

    [Theory]
    [InlineData(8, 260, 3, 8, 250, 10, 3_650)] // measured by the game's briefing
    [InlineData(8, 260, 3, 8, 260, 9, 1_030)]
    [InlineData(8, 260, 3, 8, 260, 3, 5)]
    [InlineData(1, 1, 1, 9, 1, 1, 20_000)] // galaxies wrap around: 1 → 9 is one step
    [InlineData(1, 1, 1, 1, 499, 1, 2_795)] // systems too
    public void Distance_follows_the_game(int g1, int s1, int p1, int g2, int s2, int p2, long expected)
    {
        Assert.Equal(expected, RaidPlanner.Distance(new Coordinates(g1, s1, p1), new Coordinates(g2, s2, p2), new UniverseProfile("tron")));
    }

    [Fact]
    public void The_nearest_origin_with_enough_ships_is_chosen_otherwise_the_one_with_most_ships()
    {
        var near = new Origin(new Coordinates(5, 288, 8), 10);
        var far = new Origin(new Coordinates(5, 100, 1), 50);
        Origin[] origins = [far, near, new Origin(new Coordinates(5, 288, 4), 0)];

        Origin? Choose(long needed) =>
            RaidPlanner.ChooseOrigin(Target, origins, o => o.Coordinates, o => o.Ships, needed, new UniverseProfile("tron"));

        Assert.Same(near, Choose(10));
        Assert.Same(far, Choose(20));
        Assert.Same(far, Choose(100));
        Assert.Null(RaidPlanner.ChooseOrigin(Target, [new Origin(Target, 0)], o => o.Coordinates, o => o.Ships, 1, new UniverseProfile("tron")));
    }

    [Fact]
    public void Nearest_first_orders_by_the_nearest_own_planet_and_keeps_ties_in_order()
    {
        RaidTarget At(string name, Coordinates coordinates) => new(Planet(name, coordinates, PlayerStatus.Inactive, 1), null, RaidTargetState.Ready, 0);
        RaidTarget[] targets =
        [
            At("OtherGalaxy", new Coordinates(2, 1, 1)),
            At("FarSystem", new Coordinates(5, 200, 1)),
            At("SameSystemA", new Coordinates(5, 288, 5)),
            At("NearSecondPlanet", new Coordinates(7, 51, 1)),
            At("SameSystemB", new Coordinates(5, 288, 1))
        ];

        var ordered = RaidPlanner.NearestFirst(targets, [Target, new Coordinates(7, 50, 1)], new UniverseProfile("tron"));

        Assert.Equal(["SameSystemA", "SameSystemB", "NearSecondPlanet", "FarSystem", "OtherGalaxy"], ordered.Select(t => t.Planet.PlayerName));
    }

    private sealed record Origin(Coordinates Coordinates, long Ships);

    private static RaidTarget Evaluate(EspionageReport? report, params RaidRecord[] raids) =>
        RaidPlanner.Evaluate(Inactive, report, raids, Now, Settings);

    private static EspionageReport FullReport(long defense = 0, long fleet = 0, DateTimeOffset? time = null) =>
        new("r", Target, PlanetType.Planet, time ?? Now.AddMinutes(-1), "Kowalski", "p", PlayerStatus.Inactive,
            new ResourceSet(3_000, 2_000, 1_000), 0, new UnitSection(fleet, []), new UnitSection(defense, []));

    private static RaidRecord Raid(MissionType mission, DateTimeOffset sentAt, DateTimeOffset arrivesAt) =>
        new(Target, PlanetType.Planet, "Kowalski", mission, new Coordinates(5, 288, 8), "HEAVY_CARGO", 1, 0, sentAt, arrivesAt, arrivesAt + (arrivesAt - sentAt));

    private static PlayerPlanet Planet(string name, Coordinates coordinates, PlayerStatus status, int? rank) =>
        new(name, coordinates, false, Now, status, null, rank);
}
