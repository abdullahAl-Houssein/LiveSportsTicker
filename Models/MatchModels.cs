namespace LiveSportsTicker.Models;

public enum MatchEventType
{
    KickOff,
    Goal,
    YellowCard,
    RedCard,
    Substitution,
    HalfTime,
    FullTime
}

/// <summary>
/// A single thing that happened in the match (goal, card, etc.).
/// </summary>
public class MatchEvent
{
    public int Minute { get; set; }
    public MatchEventType Type { get; set; }
    public string Team { get; set; } = "-";
    public string Player { get; set; } = "-";
    public string Description { get; set; } = "";
}

/// <summary>
/// The "Smart Streaming Idea": a live win-probability estimate
/// that is recomputed after every event in the match.
/// </summary>
public class WinProbability
{
    public double Home { get; set; } = 0.4;
    public double Draw { get; set; } = 0.2;
    public double Away { get; set; } = 0.4;
}

/// <summary>Live stats for a single team.</summary>
public class TeamStats
{
    public int Possession { get; set; } = 50;
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int Corners { get; set; }
    public int Fouls { get; set; }
    public int Offsides { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
}

/// <summary>Live stats for both teams, streamed alongside the match state.</summary>
public class MatchStatistics
{
    public TeamStats Home { get; set; } = new();
    public TeamStats Away { get; set; } = new();
}

/// <summary>
/// The full current snapshot of the live match.
/// </summary>
public class MatchState
{
    public string HomeTeam { get; set; } = "Team A";
    public string AwayTeam { get; set; } = "Team B";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int Minute { get; set; }
    public bool IsFinished { get; set; }
    public WinProbability Probability { get; set; } = new();
    public MatchStatistics Statistics { get; set; } = new();
    public string? ManOfTheMatch { get; set; }
    public string? ManOfTheMatchTeam { get; set; }
}

/// <summary>
/// The envelope sent down the SSE stream.
/// "snapshot" = full state sent once on (re)connect.
/// "event"    = full state + the single new event that just happened.
/// </summary>
public class TickerUpdate
{
    public string Type { get; set; } = "snapshot"; // "snapshot" | "event"
    public MatchState? Match { get; set; }
    public MatchEvent? Event { get; set; }
}
