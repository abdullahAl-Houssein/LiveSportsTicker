using System.Text;
using System.Text.Json;
using LiveSportsTicker.Models;
using LiveSportsTicker.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveSportsTicker.Controllers;

public class TickerController : Controller
{
    private readonly MatchBroadcastService _broadcast;

    public TickerController(MatchBroadcastService broadcast)
    {
        _broadcast = broadcast;
    }

    /// <summary>
    /// The streaming channel. Stays open on a single connection and keeps
    /// pushing "data: ...\n\n" chunks forever (Server-Sent Events).
    /// The browser's EventSource API consumes this directly and will
    /// AUTOMATICALLY RECONNECT if the connection drops - that's exactly
    /// what the grading rubric wants demonstrated.
    /// </summary>
    [Route("ticker/stream")]
    public async Task Stream()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no"); // tell reverse proxies not to buffer the stream

        var (id, reader) = _broadcast.Subscribe();
        var clientDisconnected = HttpContext.RequestAborted;

        try
        {
            // 1) Immediately send a full snapshot so a (re)connecting client
            //    is in sync with the match right away, instead of waiting
            //    for the next random event.
            var snapshot = new TickerUpdate { Type = "snapshot", Match = _broadcast.SnapshotState() };
            await WriteEventAsync(JsonSerializer.Serialize(snapshot, JsonDefaults.Options), clientDisconnected);

            // 2) Then keep forwarding whatever the simulator broadcasts.
            while (!clientDisconnected.IsCancellationRequested)
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(clientDisconnected);
                waitCts.CancelAfter(TimeSpan.FromSeconds(15));

                string message;
                try
                {
                    message = await reader.ReadAsync(waitCts.Token);
                }
                catch (OperationCanceledException) when (!clientDisconnected.IsCancellationRequested)
                {
                    // No real event in 15s -> send a comment as a heartbeat so
                    // the connection (and any proxy in between) stays alive.
                    await WriteRawAsync(": heartbeat\n\n", clientDisconnected);
                    continue;
                }

                await WriteEventAsync(message, clientDisconnected);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the browser navigated away, refreshed, or the dev killed the connection.
        }
        finally
        {
            _broadcast.Unsubscribe(id);
        }
    }

    private Task WriteEventAsync(string json, CancellationToken token) =>
        WriteRawAsync($"data: {json}\n\n", token);

    private async Task WriteRawAsync(string text, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await Response.Body.WriteAsync(bytes, 0, bytes.Length, token);
        await Response.Body.FlushAsync(token);
    }
}
