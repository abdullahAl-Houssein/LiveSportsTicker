using System.Text.Json;
using LiveSportsTicker.Models;

namespace LiveSportsTicker.Services;

/// <summary>
/// Runs forever in the background (registered with AddHostedService).
/// Simulates one football match after another: advances the clock,
/// occasionally generates an event (goal/card/substitution) with a named
/// player, keeps live match statistics (possession, shots, corners, fouls,
/// cards), and after EVERY event recomputes the live win-probability
/// ("smart streaming idea") before broadcasting the new state to every
/// connected SSE client.
/// </summary>
public class MatchSimulatorService : BackgroundService
{
    private readonly MatchBroadcastService _broadcast;
    private readonly Random _rng = new();

    private static readonly string[] HomeTeams = { "Real Madrid", "Manchester City", "Bayern Munich", "Al-Hilal" };
    private static readonly string[] AwayTeams = { "Barcelona", "Liverpool", "PSG", "Al-Nassr" };

    // A small generic squad list used for every team - good enough to give
    // every goal/card/substitution a believable player name.
    private static readonly string[] PlayerPool =
    {
        "Karim", "Yusuf", "Omar", "Hamza", "Tariq", "Anas", "Bilal", "Khalid",
        "Marco", "Diego", "Lucas", "Mateo", "Pedro", "Bruno", "Carlos", "Rafael",
        "Kevin", "Erik", "Jonas", "Mikael", "Noah", "Liam", "Ethan", "Mason"
    };

    private string[] _homeRoster = Array.Empty<string>();
    private string[] _awayRoster = Array.Empty<string>();

    // Tracks a simple "performance score" per player during the current
    // match (key = "Team::Player") so we can crown a Man of the Match.
    private readonly Dictionary<string, int> _playerScores = new();

    public MatchSimulatorService(MatchBroadcastService broadcast)
    {
        _broadcast = broadcast;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            StartNewMatch();
            await RunMatchAsync(stoppingToken);

            // brief pause between matches before kicking off the next one
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private void StartNewMatch()
    {
        _homeRoster = PickRoster();
        _awayRoster = PickRoster();
        _playerScores.Clear();

        _broadcast.UpdateState(state =>
        {
            state.HomeTeam = HomeTeams[_rng.Next(HomeTeams.Length)];
            state.AwayTeam = AwayTeams[_rng.Next(AwayTeams.Length)];
            state.HomeScore = 0;
            state.AwayScore = 0;
            state.Minute = 0;
            state.IsFinished = false;
            state.ManOfTheMatch = null;
            state.ManOfTheMatchTeam = null;
            state.Probability = ComputeProbability(0, 0, 0);
            state.Statistics = new MatchStatistics
            {
                Home = new TeamStats { Possession = 50 },
                Away = new TeamStats { Possession = 50 }
            };
        });

        BroadcastEvent(new MatchEvent
        {
            Minute = 0,
            Type = MatchEventType.KickOff,
            Team = "-",
            Player = "-",
            Description = "Kick-off!"
        });
    }

    private string[] PickRoster()
    {
        // 11 unique random names per team from the shared pool
        return PlayerPool.OrderBy(_ => _rng.Next()).Take(11).ToArray();
    }

    private async Task RunMatchAsync(CancellationToken token)
    {
        int minute = 0;
        bool halfTimeSent = false;

        while (minute < 90 && !token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_rng.Next(3, 7)), token);

            minute = Math.Min(90, minute + _rng.Next(2, 6));
            _broadcast.UpdateState(s => s.Minute = minute);

            // Drift possession and minor stats a little every tick, then push
            // a lightweight "tick" update so the page feels continuously alive
            // even on minutes with no named event.
            DriftStats();
            BroadcastTick();

            if (minute >= 45 && !halfTimeSent)
            {
                halfTimeSent = true;
                BroadcastEvent(new MatchEvent { Minute = 45, Type = MatchEventType.HalfTime, Team = "-", Player = "-", Description = "Half-time." });
                continue;
            }

            GenerateRandomEvent(minute);
        }

        FinishMatch();
    }

    /// <summary>Small random walk on possession/shots/corners/fouls so stats feel alive every tick.</summary>
    private void DriftStats()
    {
        _broadcast.UpdateState(state =>
        {
            int shift = _rng.Next(-3, 4);
            state.Statistics.Home.Possession = Math.Clamp(state.Statistics.Home.Possession + shift, 30, 70);
            state.Statistics.Away.Possession = 100 - state.Statistics.Home.Possession;

            if (_rng.NextDouble() < 0.25)
            {
                if (_rng.Next(2) == 0) state.Statistics.Home.Corners++; else state.Statistics.Away.Corners++;
            }
            if (_rng.NextDouble() < 0.2)
            {
                if (_rng.Next(2) == 0) state.Statistics.Home.Fouls++; else state.Statistics.Away.Fouls++;
            }
            if (_rng.NextDouble() < 0.15)
            {
                if (_rng.Next(2) == 0) state.Statistics.Home.Offsides++; else state.Statistics.Away.Offsides++;
            }
        });
    }

    private void GenerateRandomEvent(int minute)
    {
        double roll = _rng.NextDouble();
        if (roll < 0.45)
        {
            return; // quiet minute, already covered by the tick update above
        }

        var snapshot = _broadcast.SnapshotState();
        bool isHomeTeam = _rng.Next(2) == 0;
        string team = isHomeTeam ? snapshot.HomeTeam : snapshot.AwayTeam;
        string player = PickPlayer(isHomeTeam);

        if (roll < 0.62)
        {
            _broadcast.UpdateState(state =>
            {
                if (isHomeTeam)
                {
                    state.HomeScore++;
                    state.Statistics.Home.Shots++;
                    state.Statistics.Home.ShotsOnTarget++;
                }
                else
                {
                    state.AwayScore++;
                    state.Statistics.Away.Shots++;
                    state.Statistics.Away.ShotsOnTarget++;
                }
                state.Probability = ComputeProbability(state.HomeScore, state.AwayScore, state.Minute);
            });

            BroadcastEvent(new MatchEvent
            {
                Minute = minute,
                Type = MatchEventType.Goal,
                Team = team,
                Player = player,
                Description = $"GOAL! {player} ({team}) scores!"
            });
            TrackPlayerScore(team, player, 3);
        }
        else if (roll < 0.80)
        {
            _broadcast.UpdateState(state =>
            {
                if (isHomeTeam) state.Statistics.Home.YellowCards++; else state.Statistics.Away.YellowCards++;
                if (isHomeTeam) state.Statistics.Home.Fouls++; else state.Statistics.Away.Fouls++;
            });

            BroadcastEvent(new MatchEvent
            {
                Minute = minute,
                Type = MatchEventType.YellowCard,
                Team = team,
                Player = player,
                Description = $"Yellow card - {player} ({team})"
            });
            TrackPlayerScore(team, player, -1);
        }
        else if (roll < 0.87)
        {
            _broadcast.UpdateState(state =>
            {
                if (isHomeTeam) state.Statistics.Home.RedCards++; else state.Statistics.Away.RedCards++;
                if (isHomeTeam) state.Statistics.Home.Fouls++; else state.Statistics.Away.Fouls++;
            });

            BroadcastEvent(new MatchEvent
            {
                Minute = minute,
                Type = MatchEventType.RedCard,
                Team = team,
                Player = player,
                Description = $"RED CARD! {player} ({team}) is sent off!"
            });
            TrackPlayerScore(team, player, -3);
        }
        else
        {
            string incoming = PickPlayer(isHomeTeam);
            BroadcastEvent(new MatchEvent
            {
                Minute = minute,
                Type = MatchEventType.Substitution,
                Team = team,
                Player = $"{incoming} in, {player} out",
                Description = $"Substitution - {team}: {incoming} replaces {player}"
            });
        }
    }

    private string PickPlayer(bool isHomeTeam)
    {
        var roster = isHomeTeam ? _homeRoster : _awayRoster;
        if (roster.Length == 0) return "Player";
        return roster[_rng.Next(roster.Length)];
    }

    /// <summary>Accumulates a simple performance score per player (goal=+3, yellow=-1, red=-3) for Man of the Match.</summary>
    private void TrackPlayerScore(string team, string player, int delta)
    {
        string key = $"{team}::{player}";
        _playerScores[key] = _playerScores.GetValueOrDefault(key) + delta;
    }

    /// <summary>Picks the player with the highest performance score. Null if nobody stood out.</summary>
    private (string Team, string Player)? ComputeManOfTheMatch()
    {
        if (_playerScores.Count == 0) return null;

        var best = _playerScores.OrderByDescending(kv => kv.Value).First();
        if (best.Value <= 0) return null;

        var parts = best.Key.Split("::", 2);
        return (parts[0], parts[1]);
    }

    private void FinishMatch()
    {
        var motm = ComputeManOfTheMatch();

        _broadcast.UpdateState(s =>
        {
            s.IsFinished = true;
            s.ManOfTheMatch = motm?.Player;
            s.ManOfTheMatchTeam = motm?.Team;
        });

        string motmText = motm != null ? $" 🏅 Man of the Match: {motm.Value.Player} ({motm.Value.Team})." : "";

        BroadcastEvent(new MatchEvent
        {
            Minute = 90,
            Type = MatchEventType.FullTime,
            Team = "-",
            Player = motm?.Player ?? "-",
            Description = $"Full-time.{motmText}"
        });
    }

    /// <summary>
    /// The "smart streaming idea": a lightweight live win-probability model.
    /// - Score difference drives the direction (who is more likely to win).
    /// - The further into the match we are, the more confident the model
    ///   becomes in the current scoreline (a 1-0 lead at minute 5 means much
    ///   less than a 1-0 lead at minute 88).
    /// - A softmax turns the three "logits" (home/draw/away) into probabilities
    ///   that always sum to 1.
    /// </summary>
    private WinProbability ComputeProbability(int homeScore, int awayScore, int minute)
    {
        int diff = homeScore - awayScore;
        double urgency = Math.Clamp(minute / 90.0, 0, 1);
        double confidence = 0.35 + urgency * 1.1;

        double homeLogit = diff * confidence;
        double awayLogit = -homeLogit;
        double drawLogit = -Math.Abs(diff) * 0.6 + (1 - urgency) * 0.8;

        double max = Math.Max(homeLogit, Math.Max(awayLogit, drawLogit));
        double eHome = Math.Exp(homeLogit - max);
        double eDraw = Math.Exp(drawLogit - max);
        double eAway = Math.Exp(awayLogit - max);
        double sum = eHome + eDraw + eAway;

        return new WinProbability
        {
            Home = Math.Round(eHome / sum, 3),
            Draw = Math.Round(eDraw / sum, 3),
            Away = Math.Round(eAway / sum, 3)
        };
    }

    private void BroadcastEvent(MatchEvent ev)
    {
        var update = new TickerUpdate
        {
            Type = "event",
            Match = _broadcast.SnapshotState(),
            Event = ev
        };

        string json = JsonSerializer.Serialize(update, JsonDefaults.Options);
        _broadcast.Broadcast(json);
    }

    private void BroadcastTick()
    {
        var update = new TickerUpdate
        {
            Type = "tick",
            Match = _broadcast.SnapshotState(),
            Event = null
        };

        string json = JsonSerializer.Serialize(update, JsonDefaults.Options);
        _broadcast.Broadcast(json);
    }
}
