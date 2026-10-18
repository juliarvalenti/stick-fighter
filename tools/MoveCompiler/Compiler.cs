using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.MoveCompiler;

public sealed class CompileException : Exception
{
    public CompileException(string message)
        : base(message)
    {
    }
}

public enum Ease
{
    Snap,
    In,
    Out,
    InOut,
    Overshoot,
}

// One move, compiled: its frame data plus one integer pose per frame.
public sealed record CompiledMove(JsonElement Script, string Input, int Startup, int Active, int Recovery,
    string HitboxLimb, int HitboxRadius, List<(string Limb, string Mode, int From, int To)> Hurt,
    (int Frame, string From)? Spawn, List<FloatPose> Frames)
{
    public int Total => Frames.Count;
}

// Turns a move script into per-frame poses.
//
// A script's verbs build one target pose per phase, and Motion brings the whole body into them:
// the wind-up, the weight shift, lean and counter-arm of the strike, and a chamber to come back
// through. Startup eases from the stance into the wind-up, active snaps to the strike on the first
// active frame and holds, and recovery folds the limb back through its chamber in an arc and
// settles into its target (the stance unless a pose says otherwise). Phase lengths come from the
// frame data, so a script never counts frames; `hold:f` waits a fraction of the phase before moving.
public sealed class Compiler
{
    // The opponent's landmarks sit this far in front, at close-normal range.
    public const double OpponentSpacing = 120;

    private readonly Character _character;
    private readonly Rig _rig;
    private readonly Motion _motion;
    private readonly Dictionary<string, FloatPose> _library;
    private readonly Dictionary<string, Vec> _landmarks;

    public Compiler(Character character, JsonElement library)
    {
        _character = character;
        _rig = new Rig(character.Skeleton);
        _motion = new Motion(_rig);
        _library = new Dictionary<string, FloatPose>(StringComparer.Ordinal)
        {
            ["guard"] = FloatPose.From(character.Pose(FighterAction.Stand).Keys[0]),
            ["crouch-guard"] = FloatPose.From(character.Pose(FighterAction.Crouch).Keys[0]),
        };
        foreach (var entry in library.GetProperty("poses").EnumerateObject())
            _library[entry.Name] = ReadPose(entry.Value, entry.Name);

        // Landmarks on a fighter in guard, facing right with its feet at the origin.
        var guard = _library["guard"];
        var head = _rig.Head(guard);
        var neck = _rig.Neck(guard);
        var hip = _rig.Hip(guard);
        var chest = new Vec(0, neck.Y + (hip.Y - neck.Y) * 0.3);
        var knee = _rig.Leg(guard, front: true).Mid;
        _landmarks = new Dictionary<string, Vec>(StringComparer.Ordinal)
        {
            ["head"] = new(0, head.Y),
            ["chest"] = chest,
            ["hip"] = new(0, hip.Y),
            ["knee"] = new(0, knee.Y),
            // Straight down under the body: a leg driven down here comes down on a head below,
            // even from just past it (a crossup).
            ["feet"] = new(0, 0),
            ["opponent-head"] = new(OpponentSpacing, head.Y),
            ["opponent-chest"] = new(OpponentSpacing, chest.Y),
            ["opponent-shin"] = new(OpponentSpacing, knee.Y * 0.6),
        };
    }

    public Rig Rig => _rig;

    public CompiledMove Compile(JsonElement script)
    {
        var input = script.GetProperty("input").GetString() ?? "?";
        try
        {
            return CompileInner(script, input);
        }
        catch (CompileException e)
        {
            throw new CompileException($"move {input}: {e.Message}");
        }
    }

    private CompiledMove CompileInner(JsonElement script, string input)
    {
        var startup = Int(script, "startup");
        var active = Int(script, "active");
        var recovery = Int(script, "recovery");
        if (startup < 1 || active < 1 || recovery < 0)
            throw new CompileException("startup and active must be at least 1, recovery at least 0");

        var stanceName = script.TryGetProperty("stance", out var s) ? s.GetString()! : DefaultStance(input);
        var stance = Pose(stanceName);

        // Verbs, grouped by phase.
        var phases = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["startup"] = new(),
            ["active"] = new(),
            ["recovery"] = new(),
        };
        var current = "startup";
        var index = 0;
        foreach (var item in script.GetProperty("do").EnumerateArray())
        {
            var text = item.GetString()?.Trim() ?? "";
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            var space = text.IndexOf(' ', StringComparison.Ordinal);
            if (colon > 0 && (space < 0 || colon < space) && phases.ContainsKey(text[..colon]) && text.Length > colon + 1 && text[colon + 1] == ' ')
            {
                current = text[..colon];
                text = text[(colon + 1)..].Trim();
            }
            if (text.Length == 0)
                throw new CompileException($"verb {index} is empty");
            phases[current].Add(text);
            index++;
        }

        string? hitboxLimb = null;
        var hitboxRadius = 0;
        var hurt = new List<(string, string, int, int)>();
        (int, string)? spawn = null;

        var startupFrames = startup - 1;
        var firstActive = startupFrames;
        var phaseRanges = new Dictionary<string, (int From, int To)>(StringComparer.Ordinal)
        {
            ["startup"] = (0, startupFrames - 1),
            ["active"] = (firstActive, firstActive + active - 1),
            ["recovery"] = (firstActive + active, firstActive + active + recovery - 1),
        };

        var targets = new Dictionary<string, FloatPose>(StringComparer.Ordinal);
        var eases = new Dictionary<string, Ease?>(StringComparer.Ordinal);
        var holds = new Dictionary<string, double>(StringComparer.Ordinal);
        var leaned = new HashSet<string>(StringComparer.Ordinal);
        var stepped = new HashSet<string>(StringComparer.Ordinal);
        var moved = new HashSet<string>(StringComparer.Ordinal);
        string? strikeLimb = null;

        var from = stance;
        foreach (var phase in new[] { "startup", "active", "recovery" })
        {
            var length = phase switch { "startup" => startupFrames, "active" => active, _ => recovery };
            var target = phase == "recovery" ? stance.Clone() : from.Clone();
            Ease? ease = null;
            var hold = 0.0;

            foreach (var verb in phases[phase])
            {
                var (name, args) = Split(verb);
                switch (name)
                {
                    case "pose":
                        target = Pose(Arg(args, 0, verb)).Clone();
                        moved.Add(phase);
                        break;
                    case "reach":
                        Reach(target, Arg(args, 0, verb), Arg(args, 1, verb), args.Length > 2 ? Number(args[2], verb) : null);
                        moved.Add(phase);
                        break;
                    case "lean":
                        target.A[FloatPose.Spine] = Number(Arg(args, 0, verb), verb) * Angle.UnitsPerTurn / 360.0;
                        leaned.Add(phase);
                        break;
                    case "crouch":
                        Crouch(target, Math.Clamp(Number(Arg(args, 0, verb), verb), 0, 1));
                        moved.Add(phase);
                        break;
                    case "step":
                        Step(target, Number(Arg(args, 0, verb), verb));
                        stepped.Add(phase);
                        moved.Add(phase);
                        break;
                    case "hold":
                        hold = Math.Clamp(Number(Arg(args, 0, verb), verb), 0, 1);
                        break;
                    case "ease":
                        ease = Arg(args, 0, verb) switch
                        {
                            "snap" => Ease.Snap,
                            "in" => Ease.In,
                            "out" => Ease.Out,
                            "inout" => Ease.InOut,
                            "overshoot" => Ease.Overshoot,
                            var other => throw new CompileException($"'{verb}': unknown ease '{other}'"),
                        };
                        break;
                    case "hitbox":
                        if (phase != "active")
                            throw new CompileException($"'{verb}': a hitbox belongs in the active phase");
                        hitboxLimb = HitboxLimb(Arg(args, 0, verb), verb);
                        strikeLimb = Arg(args, 0, verb);
                        hitboxRadius = args.Length > 1 ? (int)Number(args[1], verb) : (int)_character.Skeleton.LimbRadius.RoundToInt();
                        break;
                    case "hurt":
                        var range = phaseRanges[phase];
                        if (range.To >= range.From)
                            hurt.Add((HitboxLimb(Arg(args, 0, verb), verb), HurtMode(Arg(args, 1, verb), verb), range.From, range.To));
                        break;
                    case "spawn":
                        if (Arg(args, 0, verb) != "projectile")
                            throw new CompileException($"'{verb}': only projectiles can be spawned");
                        var source = args.FirstOrDefault(a => a.StartsWith("from:", StringComparison.Ordinal))?[5..] ?? "front-hand";
                        spawn = (phaseRanges[phase].From, SpawnJoint(source, verb));
                        break;
                    default:
                        throw new CompileException($"unknown verb '{name}' in '{verb}'");
                }
            }

            targets[phase] = target;
            eases[phase] = ease;
            holds[phase] = hold;
            if (length > 0)
                from = target;
        }

        if (hitboxLimb is null || strikeLimb is null)
            throw new CompileException("no hitbox: add 'hitbox:<limb>' to the active phase");

        // The script's key poses, with the whole body brought in (see Motion).
        var airborne = stanceName == "jump-tuck";
        var strike = new Strike(strikeLimb, Tier(script), Grounded: !airborne, Crouching: stanceName == "crouch-guard");
        var windup = targets["startup"];
        var hit = targets["active"];
        var home = targets["recovery"];
        var scriptWindup = startupFrames > 0 ? windup.Clone() : stance;
        var scriptHit = hit.Clone();
        if (startupFrames > 0)
        {
            _motion.Windup(windup, strike, moved.Contains("startup"), leaned.Contains("startup"));
            windup = KeepFeetPlanted(stance, windup, windup, 1, airborne);
        }
        else
        {
            windup = stance;
        }
        _motion.Into(hit, stance, strike, leaned.Contains("active"), stepped.Contains("active"));
        Motion.KeepTuck(windup, stance, strike);
        Motion.KeepTuck(hit, stance, strike);

        // Startup winds up from the stance; active snaps out (or eases, if the script says) and holds.
        var frames = new List<FloatPose>();
        Segment(frames, stance, windup, startupFrames, holds["startup"], eases["startup"] ?? Ease.InOut, airborne, Overlap.Lag);
        var activeStart = frames.Count;
        Segment(frames, windup, hit, active, 0, eases["active"] ?? Ease.Snap, airborne, Overlap.None);

        // The body never moves the hit: on every active frame the striking hand or foot is exactly
        // where the script alone puts it, even when the script eases through the active frames.
        var aimed = new List<FloatPose>();
        Segment(aimed, scriptWindup, scriptHit, active, 0, eases["active"] ?? Ease.Snap, airborne, Overlap.None);
        for (var i = 0; i < active; i++)
            _motion.Aim(frames[activeStart + i], strikeLimb, _motion.Ends(aimed[i], strikeLimb));

        // Recovery holds for `hold`, pulls the limb back into its chamber fast, then eases home
        // through it and settles. Too short for that, it eases straight home.
        var held = (int)Math.Floor(holds["recovery"] * recovery);
        for (var i = 0; i < held; i++)
            frames.Add(hit.Clone());
        var back = recovery - held;
        if (back >= 3 && eases["recovery"] is null)
        {
            var retract = KeepFeetPlanted(hit, home, _motion.Retract(hit, home, strike), 0.5, airborne);
            var quick = (int)Math.Ceiling(back * 0.5);
            var first = frames.Count;
            Segment(frames, hit, retract, quick, 0, Ease.InOut, airborne, Overlap.Lead);
            Segment(frames, retract, home, back - quick, 0, Ease.Overshoot, airborne, Overlap.Lag);
            _motion.ArcBack(frames, first, back, hit, retract, home, strike);
        }
        else
        {
            Segment(frames, hit, home, back, 0, eases["recovery"] ?? Ease.Out, airborne, Overlap.Lead);
        }

        return new CompiledMove(script, input, startup, active, recovery, hitboxLimb, hitboxRadius, hurt, spawn, frames);
    }

    // How elbows, knees and the head move against their parents in a segment.
    private enum Overlap
    {
        None,
        Lag, // half a frame behind, catching up on the last frame: the trailing forearm or shin
        Lead, // half a frame ahead: a limb folds before it swings, so a straight limb never sweeps
    }

    // Frames easing from one key pose to the next, reaching it on the last.
    private void Segment(List<FloatPose> frames, FloatPose from, FloatPose to, int length, double hold, Ease ease,
        bool airborne, Overlap overlap)
    {
        for (var i = 0; i < length; i++)
        {
            var t = Progress(i, length, hold, ease);
            var children = overlap switch
            {
                Overlap.Lag when i < length - 1 => (t + (i == 0 ? 0 : Progress(i - 1, length, hold, ease))) / 2,
                Overlap.Lead when i < length - 1 => (t + Progress(i + 1, length, hold, ease)) / 2,
                _ => t,
            };
            frames.Add(KeepFeetPlanted(from, to, Motion.Blend(from, to, t, children), t, airborne));
        }
    }

    private static int Tier(JsonElement script) =>
        script.TryGetProperty("strength", out var s) ? s.GetString() switch
        {
            "light" => 0,
            "medium" => 1,
            "heavy" => 2,
            var other => throw new CompileException($"strength '{other}' must be light, medium or heavy"),
        } : 0;

    public FloatPose Pose(string name) =>
        _library.TryGetValue(name, out var pose) ? pose : throw new CompileException($"no pose named '{name}' (have: {string.Join(", ", _library.Keys)})");

    // j.X is airborne; a single down direction (1X, 2X, 3X) is crouching; motions (236P) start standing.
    public static string DefaultStance(string input) =>
        input.StartsWith("j.", StringComparison.Ordinal) ? "jump-tuck"
        : input.Length > 1 && input[0] is '1' or '2' or '3' && char.IsLetter(input[1]) ? "crouch-guard"
        : "guard";

    private void Reach(FloatPose pose, string limb, string landmark, double? amount)
    {
        if (!_landmarks.TryGetValue(landmark, out var mark))
            throw new CompileException($"no landmark '{landmark}' (have: {string.Join(", ", _landmarks.Keys)})");
        if (amount is < 0 or > 1)
            throw new CompileException($"reach amount {amount} must be between 0 and 1");

        foreach (var (arm, front) in Limbs(limb))
        {
            var root = arm ? _rig.Neck(pose) : _rig.Hip(pose);
            var length = arm ? _rig.UpperArm + _rig.Forearm : _rig.Thigh + _rig.Shin;
            var target = amount is { } a ? root + (mark - root).Unit * (a * length) : mark;
            if (!_rig.Solve(pose, arm, front, target))
                throw new CompileException($"reach:{limb} {landmark} is out of reach ({(mark - root).Length:0} px from the {(arm ? "shoulder" : "hip")}, limb is {length:0} px); give an amount");
        }
    }

    private static IEnumerable<(bool Arm, bool Front)> Limbs(string limb) => limb switch
    {
        "front-hand" => new[] { (true, true) },
        "back-hand" => new[] { (true, false) },
        "both-hands" => new[] { (true, true), (true, false) },
        "front-foot" => new[] { (false, true) },
        "back-foot" => new[] { (false, false) },
        _ => throw new CompileException($"can't reach with '{limb}' (front-hand, back-hand, both-hands, front-foot, back-foot)"),
    };

    // 0 is the standing hip height, 1 the crouch's. Feet stay planted where they are.
    private void Crouch(FloatPose pose, double amount)
    {
        var stand = _library["guard"].HipY;
        var crouch = _library["crouch-guard"].HipY;
        var back = _rig.Leg(pose, front: false).End;
        var front = _rig.Leg(pose, front: true).End;
        pose.HipY = stand + (crouch - stand) * amount;
        Plant(pose, back, front);
    }

    // Shifts the body forward or back over planted feet.
    private void Step(FloatPose pose, double px)
    {
        var back = _rig.Leg(pose, front: false).End;
        var front = _rig.Leg(pose, front: true).End;
        pose.HipX += px;
        Plant(pose, back, front);
    }

    private void Plant(FloatPose pose, Vec back, Vec front)
    {
        if (!_rig.Solve(pose, arm: false, front: false, back) || !_rig.Solve(pose, arm: false, front: true, front))
            throw new CompileException("the feet can't stay planted (the hip moved too far)");
    }

    // Blending joint angles swings the feet, so a foot that's on the floor at both ends of a blend
    // is put back on the floor, sliding straight between its two spots.
    // Also stops easing from overshooting a straight limb into a backwards bend. Only overshoot is
    // clamped: if a pose itself bends a joint backwards, it's left alone so the checks report it.
    private FloatPose KeepFeetPlanted(FloatPose from, FloatPose to, FloatPose blended, double t, bool airborne)
    {
        foreach (var (joint, sign) in new[]
        {
            (FloatPose.FrontElbow, -1), (FloatPose.BackElbow, -1), (FloatPose.FrontKnee, 1), (FloatPose.BackKnee, 1),
        })
        {
            if (from.A[joint] * sign >= 0 && to.A[joint] * sign >= 0 && blended.A[joint] * sign < 0)
                blended.A[joint] = 0;
        }
        if (airborne)
            return blended;

        t = Math.Clamp(t, 0, 1);
        foreach (var front in new[] { false, true })
        {
            var a = _rig.Leg(from, front).End;
            var b = _rig.Leg(to, front).End;
            if (Math.Abs(a.Y) <= 1 && Math.Abs(b.Y) <= 1)
                _rig.Solve(blended, arm: false, front, new Vec(a.X + (b.X - a.X) * t, 0));
            else if (a.Y <= 1 && b.Y <= 1 && _rig.Leg(blended, front).End is { Y: > 0 } below)
                _rig.Solve(blended, arm: false, front, new Vec(below.X, 0)); // a blend between valid poses never dips through the floor
        }
        return blended;
    }

    // Progress through a phase on frame i: the target is reached on the phase's last frame.
    public static double Progress(int i, int length, double hold, Ease ease)
    {
        if (ease == Ease.Snap)
            return 1;
        var held = (int)Math.Floor(hold * length);
        if (i < held)
            return 0;
        var t = (i - held + 1) / (double)(length - held);
        return ease switch
        {
            Ease.In => t * t,
            Ease.Out => 1 - (1 - t) * (1 - t),
            Ease.Overshoot => 1 + 2.2 * Math.Pow(t - 1, 3) + 1.2 * Math.Pow(t - 1, 2),
            _ => t * t * (3 - 2 * t),
        };
    }

    private static string HitboxLimb(string limb, string verb) => limb switch
    {
        "front-hand" or "both-hands" => nameof(Limb.FrontForearm),
        "back-hand" => nameof(Limb.BackForearm),
        "front-foot" => nameof(Limb.FrontShin),
        "back-foot" => nameof(Limb.BackShin),
        "head" => nameof(Limb.Neck),
        "torso" => nameof(Limb.Spine),
        _ => throw new CompileException($"'{verb}': unknown limb '{limb}'"),
    };

    private static string HurtMode(string mode, string verb) => mode switch
    {
        "shrink" => nameof(Sim.Characters.HurtMode.Shrink),
        "ignore-projectiles" => nameof(Sim.Characters.HurtMode.IgnoreProjectiles),
        _ => throw new CompileException($"'{verb}': hurt mode must be shrink or ignore-projectiles"),
    };

    private static string SpawnJoint(string limb, string verb) => limb switch
    {
        "front-hand" or "both-hands" => nameof(Joint.FrontHand),
        "back-hand" => nameof(Joint.BackHand),
        "front-foot" => nameof(Joint.FrontFoot),
        _ => throw new CompileException($"'{verb}': can't spawn from '{limb}'"),
    };

    private FloatPose ReadPose(JsonElement e, string name)
    {
        var hip = e.GetProperty("hip");
        var pose = new FloatPose { HipX = hip[0].GetInt32(), HipY = hip[1].GetInt32() };
        var angles = e.GetProperty("angles");
        for (var i = 0; i < 10; i++)
        {
            if (!angles.TryGetProperty(FloatPose.Names[i], out var value))
                throw new CompileException($"library pose '{name}' is missing '{FloatPose.Names[i]}'");
            pose.A[i] = value.GetInt32();
        }
        return pose;
    }

    private static (string Name, string[] Args) Split(string verb)
    {
        var colon = verb.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
            throw new CompileException($"'{verb}' isn't a verb (expected name:args)");
        var args = verb[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (verb[..colon], args);
    }

    private static string Arg(string[] args, int i, string verb) =>
        i < args.Length ? args[i] : throw new CompileException($"'{verb}' is missing an argument");

    private static double Number(string text, string verb) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new CompileException($"'{verb}': '{text}' isn't a number");

    private static int Int(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : throw new CompileException($"'{key}' is missing or not a whole number");
}
