# Live Sports Ticker

Course: Advanced Web Dev - Streaming
Student: Full Name (عدّل هذا الحقل باسمك)

## Demo Video
https://youtu.be/your-link  <!-- ضع هنا رابط يوتيوب بعد الرفع -->

## Streaming
- **Technique:** Server-Sent Events (SSE) over a single open HTTP connection (`GET /ticker/stream`), implemented manually in `TickerController.Stream()` by writing `data: ...\n\n` chunks to `Response.Body` and flushing after each one.
- **What is streamed:** A simulated football match runs forever in the background (`MatchSimulatorService`, an `IHostedService`). It advances the match clock, randomly generates events (kick-off, goals, cards, substitutions, half-time, full-time), and after **every** event the full match state (score, minute, win probability) plus that single event are pushed to every connected browser.
- **Smart idea:** Live win-probability re-estimation. After every event, `ComputeProbability()` recomputes Home/Draw/Away percentages using a softmax over three "logits" driven by the score difference and how far into the match we are — so a 1-0 lead means much more in minute 88 than in minute 5. The percentages animate live on the page.
- **Named events & live stats:** Every goal, yellow card, and red card is attributed to a specific player (`MatchEvent.Player`), picked from an 11-player roster generated per team at kick-off. A full live statistics panel (possession, shots, shots on target, corners, fouls, offsides, yellow/red cards per team) is streamed continuously — drifting slightly every "tick" and updating precisely whenever a goal/card/foul-causing event happens.
- **Reliability features:**
  - A heartbeat comment (`: heartbeat\n\n`) is sent every 15s of inactivity to keep the connection alive through proxies and to detect a dead client quickly.
  - On every (re)connection — including the browser's automatic SSE reconnect after a dropped connection — the server immediately sends a full **snapshot** of the current match so the client is back in sync instantly.
  - `Response.HttpContext.RequestAborted` is used to detect client disconnects and clean up the subscriber list (no leaked connections).

## How to run
```bash
dotnet restore
dotnet run
```
Then open the printed URL (e.g. `http://localhost:5219`) in your browser. The scoreboard, win-probability bar and event feed update live with no page reload.

**To demo a reconnect (for the grading video):** stop the app (`Ctrl+C`) while the page is open — the status badge turns to "Reconnecting…" — then run `dotnet run` again. The page recovers automatically and resyncs via the snapshot message, with no manual refresh.

## Project structure
```
Controllers/
  HomeController.cs     -> serves the Razor page
  TickerController.cs   -> the SSE streaming endpoint
Models/
  MatchModels.cs         -> MatchState, MatchEvent, WinProbability, TickerUpdate
Services/
  MatchBroadcastService.cs  -> shared state + per-client subscriber channels
  MatchSimulatorService.cs  -> background simulation + win-probability model
Views/Home/Index.cshtml  -> the live UI (EventSource + DOM updates)
wwwroot/css/site.css      -> styling
```
