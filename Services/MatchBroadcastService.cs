using System.Collections.Concurrent;
using System.Threading.Channels;
using LiveSportsTicker.Models;

namespace LiveSportsTicker.Services;

/// <summary>
/// Holds the single "current match" shared state and a list of live SSE
/// subscribers. The background simulator writes to it; the TickerController
/// reads from it. This is what makes the stream a true multi-client
/// continuous push channel instead of a one-shot response.
/// </summary>
public class MatchBroadcastService
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();
    private readonly object _lock = new();

    public MatchState CurrentState { get; private set; } = new();

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    /// <summary>Thread-safe read/modify access to the shared match state.</summary>
    public void UpdateState(Action<MatchState> mutate)
    {
        lock (_lock)
        {
            mutate(CurrentState);
        }
    }

    public MatchState SnapshotState()
    {
        lock (_lock)
        {
            return new MatchState
            {
                HomeTeam = CurrentState.HomeTeam,
                AwayTeam = CurrentState.AwayTeam,
                HomeScore = CurrentState.HomeScore,
                AwayScore = CurrentState.AwayScore,
                Minute = CurrentState.Minute,
                IsFinished = CurrentState.IsFinished,
                Probability = CurrentState.Probability,
                ManOfTheMatch = CurrentState.ManOfTheMatch,
                ManOfTheMatchTeam = CurrentState.ManOfTheMatchTeam,
                Statistics = new MatchStatistics
                {
                    Home = new TeamStats
                    {
                        Possession = CurrentState.Statistics.Home.Possession,
                        Shots = CurrentState.Statistics.Home.Shots,
                        ShotsOnTarget = CurrentState.Statistics.Home.ShotsOnTarget,
                        Corners = CurrentState.Statistics.Home.Corners,
                        Fouls = CurrentState.Statistics.Home.Fouls,
                        Offsides = CurrentState.Statistics.Home.Offsides,
                        YellowCards = CurrentState.Statistics.Home.YellowCards,
                        RedCards = CurrentState.Statistics.Home.RedCards
                    },
                    Away = new TeamStats
                    {
                        Possession = CurrentState.Statistics.Away.Possession,
                        Shots = CurrentState.Statistics.Away.Shots,
                        ShotsOnTarget = CurrentState.Statistics.Away.ShotsOnTarget,
                        Corners = CurrentState.Statistics.Away.Corners,
                        Fouls = CurrentState.Statistics.Away.Fouls,
                        Offsides = CurrentState.Statistics.Away.Offsides,
                        YellowCards = CurrentState.Statistics.Away.YellowCards,
                        RedCards = CurrentState.Statistics.Away.RedCards
                    }
                }
            };
        }
    }

    /// <summary>Pushes one JSON message to every currently connected client.</summary>
    public void Broadcast(string json)
    {
        foreach (var kv in _subscribers)
        {
            kv.Value.Writer.TryWrite(json);
        }
    }

    public int SubscriberCount => _subscribers.Count;
}
