namespace OgameSkaner.Core.Domain;

/// <summary>A player's flags as the galaxy view and the reports show them (<c>isInactive7</c>, <c>isVacation</c>, …).</summary>
[Flags]
public enum PlayerStatus
{
    None = 0,

    /// <summary>Inactive for 7 days, "(i)".</summary>
    Inactive = 1,

    /// <summary>Inactive for 28 days, "(I)".</summary>
    LongInactive = 2,

    Vacation = 4,
    Banned = 8,

    /// <summary>Much weaker player (noob protection), "(n)".</summary>
    Noob = 16,

    /// <summary>Much stronger player, "(s)".</summary>
    Strong = 32,

    /// <summary>"Empire protection", "(p)".</summary>
    Protection = 64
}

public static class PlayerStatusExtensions
{
    /// <summary>Inactive (7 or 28 days) and not protected by vacation, a ban, noob or empire protection.</summary>
    public static bool IsRaidable(this PlayerStatus status) =>
        (status & (PlayerStatus.Inactive | PlayerStatus.LongInactive)) != 0
        && (status & (PlayerStatus.Vacation | PlayerStatus.Banned | PlayerStatus.Noob | PlayerStatus.Protection)) == 0;

    /// <summary>The game's letters, e.g. "i", "I v".</summary>
    public static string ToLetters(this PlayerStatus status)
    {
        var letters = new List<string>();
        if (status.HasFlag(PlayerStatus.Inactive)) letters.Add("i");
        if (status.HasFlag(PlayerStatus.LongInactive)) letters.Add("I");
        if (status.HasFlag(PlayerStatus.Vacation)) letters.Add("v");
        if (status.HasFlag(PlayerStatus.Banned)) letters.Add("b");
        if (status.HasFlag(PlayerStatus.Noob)) letters.Add("n");
        if (status.HasFlag(PlayerStatus.Strong)) letters.Add("s");
        if (status.HasFlag(PlayerStatus.Protection)) letters.Add("p");
        return string.Join(" ", letters);
    }

    /// <summary>Maps the markup's status classes (<c>isInactive7</c>, <c>isVacation</c>, …); unknown classes are ignored.</summary>
    public static PlayerStatus FromCssClasses(IEnumerable<string> classes)
    {
        var status = PlayerStatus.None;
        foreach (var css in classes)
        {
            status |= css switch
            {
                "isInactive7" => PlayerStatus.Inactive,
                "isInactive28" => PlayerStatus.LongInactive,
                "isVacation" => PlayerStatus.Vacation,
                "isBanned" => PlayerStatus.Banned,
                "isNoob" => PlayerStatus.Noob,
                "isStrong" => PlayerStatus.Strong,
                "isProtection" => PlayerStatus.Protection,
                _ => PlayerStatus.None
            };
        }

        return status;
    }
}
