// A scripted online match over real UDP, one player per process:
//
//   dotnet run --project tools/NetPlay -- host PORT FRAMES     P1: listens, decides the match
//   dotnet run --project tools/NetPlay -- join IP:PORT FRAMES  P2: connects to the host
//
// Both play the same scripted inputs (each its own player's), at 60 ticks a second, through the
// real NetMatch and RollbackSession, until FRAMES frames are confirmed. Each then prints its final
// checksum and the offline replay's, and exits 0 if they match, 1 if not (or on a refusal,
// forfeit or timeout). CI runs a host and a joiner side by side on localhost.

using System;
using System.Diagnostics;
using System.Threading;
using StickFighter.Net;
using StickFighter.Sim;
using StickFighter.Sim.Net;
using StickFighter.Sim.Replays;

const ulong Seed = 42;

// No input delay, so even localhost's sub-frame latency makes the session predict and roll back.
const int Delay = 0;
const MatchMode Mode = MatchMode.Versus;

if (args.Length != 3 || !int.TryParse(args[2], out var frames) || frames <= 0)
{
    Console.WriteLine("usage: NetPlay host PORT FRAMES | join IP:PORT FRAMES");
    return 2;
}

var host = args[0] == "host";
using var link = host ? UdpLink.Host(int.Parse(args[1])) : UdpLink.Join(args[1]);
var settings = new NetMatch.Settings(Seed, Stage.Training, Mode, Delay, Replay.LoadedDataHash);
var match = new NetMatch(link, host, settings);
var name = host ? "host" : "join";
Console.WriteLine($"{name}: {(host ? $"listening on {link.LocalPort}" : $"joining {args[1]}")}, {frames} frames");

// Ticks at 60 a second, like the game, until every frame is confirmed and then a second more (so
// the other side gets the last inputs and acks), or 60 seconds pass.
var clock = Stopwatch.StartNew();
var tick = 0L;
var doneAt = -1L;
var started = false;
while (clock.Elapsed < TimeSpan.FromSeconds(60))
{
    var due = clock.ElapsedTicks * GameInfo.TicksPerSecond / Stopwatch.Frequency;
    if (tick >= due)
    {
        Thread.Sleep(1);
        continue;
    }
    tick++;

    var s = match.Session;
    var frame = s is null ? 0 : s.Frame + s.InputDelay;
    match.Tick(frame < frames ? Script(frame).For(match.LocalPlayer) : Buttons.None, advance: s is null || s.Frame < frames);

    if (match.Phase is NetPhase.Refused or NetPhase.Forfeited)
        break;

    // The handshake has the joiner refuse a start state it can't reproduce, so reaching here, both
    // sides start from the same frame 0.
    if (!started && match.Agreed is { } agreed)
    {
        started = true;
        Console.WriteLine($"{name}: connected as P{match.LocalPlayer + 1}, seed {agreed.Seed}, {agreed.Mode}; "
            + $"start state {NetMatch.StartChecksum(agreed):x16}{(host ? "" : ", the host's too")}");
    }
    if (doneAt < 0 && match.Session?.ConfirmedFrame == frames)
        doneAt = tick;
    if (doneAt >= 0 && tick - doneAt >= GameInfo.TicksPerSecond)
        break;
}

if (match.Phase == NetPhase.Refused)
{
    Console.WriteLine($"{name}: FAIL refused: {match.RefusedBecause}");
    return 1;
}
if (match.Session is not { } session || session.ConfirmedFrame != frames)
{
    Console.WriteLine($"{name}: FAIL {match.Phase} after {clock.Elapsed.TotalSeconds:F1} s, confirmed {match.Session?.ConfirmedFrame ?? 0} of {frames}");
    return 1;
}

var inputs = new FrameInput[frames];
for (var f = Delay; f < frames; f++)
    inputs[f] = Script(f);
var offline = Replay.Record(Seed, Stage.Training, inputs, Mode).FinalChecksum;
var actual = session.State.Checksum();
Console.WriteLine($"{name}: {frames} frames in {clock.Elapsed.TotalSeconds:F1} s, {session.Rollbacks} rollbacks (deepest {session.DeepestRollback}), "
    + $"{session.StalledTicks} stalls, advantage {session.FrameAdvantage}");
Console.WriteLine($"{name}: final {actual:x16}, offline {offline:x16}: {(actual == offline ? "in sync" : "FAIL out of sync")}");
return actual == offline ? 0 : 1;

// Both players busy: walking in and out, attacks, a crouch, a jump.
static FrameInput Script(int f) => new(
    (f % 120) switch { < 30 => Buttons.Right, 40 => Buttons.MediumPunch, < 60 => Buttons.None, 70 => Buttons.Down | Buttons.LightKick, < 90 => Buttons.Left, 100 => Buttons.Up, _ => Buttons.None },
    (f % 90) switch { < 20 => Buttons.Left, 30 => Buttons.HeavyPunch, < 50 => Buttons.None, 55 => Buttons.LightPunch | Buttons.LightKick, < 70 => Buttons.Right, _ => Buttons.None });
