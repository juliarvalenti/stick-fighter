using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Characters;

// Parses and validates character data once at startup. Bad data fails loudly, naming the file,
// the move or pose, and the key or field, so a broken edit can't quietly change a match.
// Only integers are read; a fractional number in the file is an error.
public static class CharacterLoader
{
    private static readonly string[] AngleNames =
    {
        "spine", "neck", "backShoulder", "backElbow", "frontShoulder", "frontElbow",
        "backHip", "backKnee", "frontHip", "frontKnee",
    };

    // In FighterAction order; Attack has no stance pose (it plays its move's track).
    private static readonly string[] PoseNames =
    {
        "stand", "crouch", "walkForward", "walkBack", "hitstun", "blockStand", "blockCrouch", "crouchDown",
        "thrown", "throwHold", "knockdown", "preJump", "jump", "landing", "dashForward", "dashBack", "dizzy",
    };

    // `json` is the character (stats, skeleton, stance poses); `movesJson` is its compiled moves
    // (tools/MoveCompiler's output), or null for a character with no moves yet.
    public static Character Load(string json, string? movesJson, string source)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var movesDoc = movesJson is null ? null : JsonDocument.Parse(movesJson);
            var root = doc.RootElement;
            var hash = new DataHasher();

            var name = Str(root, "name");
            hash.Add(name);
            var stats = ReadStats(Obj(root, "stats"), hash);
            var arc = Movement.Jumping.ArcFrames(stats.JumpVelocity, stats.Gravity);
            if (arc != stats.AirborneFrames)
                throw new CharacterDataException($"stats: the jump arc (jumpVelocity, gravity) lands after {arc} frames, but airborneFrames is {stats.AirborneFrames}");
            if (stats.ForwardDashRecovery >= stats.ForwardDashFrames || stats.BackDashRecovery >= stats.BackDashFrames)
                throw new CharacterDataException("stats: a dash's recovery must leave it at least one frame of movement");
            if (stats.BackDashInvincibleFrames > stats.BackDashFrames)
                throw new CharacterDataException("stats: backDashInvincibleFrames is longer than the back-dash");
            if (stats.DashInputWindow > InputHistory.MaxWindow)
                throw new CharacterDataException($"stats: dashInputWindow is at most {InputHistory.MaxWindow} (the input history's depth)");
            // A motion's window plus the buffer it can be pressed in has to fit the input history.
            if (stats.MotionWindow + Combat.Normals.BufferWindow > InputHistory.MaxWindow)
                throw new CharacterDataException($"stats: motionWindow plus the buffer is at most {InputHistory.MaxWindow} (the input history's depth)");
            if (stats.QuickRiseWindow > InputHistory.MaxWindow)
                throw new CharacterDataException($"stats: quickRiseWindow is at most {InputHistory.MaxWindow} (the input history's depth)");
            var skeleton = ReadSkeleton(Obj(root, "skeleton"), hash);

            var poses = Obj(root, "poses");
            var tracks = ImmutableArray.CreateBuilder<PoseTrack>(PoseNames.Length);
            var pushHeights = ImmutableArray.CreateBuilder<Fix>(PoseNames.Length);
            foreach (var poseName in PoseNames)
            {
                var pose = Obj(poses, poseName);
                tracks.Add(ReadTrack(Obj(pose, "keys", isArray: true), Int(pose, "length"), $"pose {poseName}", hash));

                // A stance may set its own pushbox height (crouching ones do); otherwise it's the standing one.
                var pushHeight = OptionalInt(pose, "pushHeight", hash);
                if (pushHeight is <= 0)
                    throw new CharacterDataException($"pose {poseName}: 'pushHeight' must be positive, found {pushHeight}");
                pushHeights.Add(pushHeight is { } own ? Fix.FromInt(own) : stats.PushHeight);
            }

            var moves = ImmutableArray.CreateBuilder<Move>();
            if (movesDoc is not null)
            {
                foreach (var move in Obj(movesDoc.RootElement, "moves", isArray: true).EnumerateArray())
                    moves.Add(ReadMove(move, hash));
            }
            for (var i = 0; i < moves.Count; i++)
            {
                // The buffer reaches back across a hitstop (Combat.Hitstop), so the longest freeze
                // plus the buffer window has to fit in the input history.
                if (moves[i].HitstopOnHit + Combat.Normals.BufferWindow > InputHistory.MaxWindow)
                    throw new CharacterDataException($"move {moves[i].Input}: hitstop {moves[i].HitstopOnHit} plus the {Combat.Normals.BufferWindow}-frame buffer is more than the input history holds ({InputHistory.MaxWindow})");
                for (var j = i + 1; j < moves.Count; j++)
                {
                    if (moves[i].Input == moves[j].Input)
                        throw new CharacterDataException($"move {moves[j].Input}: input is used by two moves");
                }
            }

            return new Character(name, stats, skeleton, tracks.MoveToImmutable(), pushHeights.MoveToImmutable(), moves.ToImmutable(), hash.Value);
        }
        catch (CharacterDataException e)
        {
            throw new FormatException($"{source}: {e.Message}", e);
        }
        catch (JsonException e)
        {
            throw new FormatException($"{source}: not valid JSON: {e.Message}", e);
        }
    }

    private static Stats ReadStats(JsonElement s, DataHasher h) => new(
        Health: Positive(s, "health", h),
        Stun: Positive(s, "stun", h),
        WalkForward: Fix.FromRatio(Positive(s, "walkForward", h), 1000),
        WalkBack: Fix.FromRatio(Positive(s, "walkBack", h), 1000),
        ForwardDashFrames: Positive(s, "forwardDashFrames", h),
        BackDashFrames: Positive(s, "backDashFrames", h),
        BackDashInvincibleFrames: Int(s, "backDashInvincibleFrames", h),
        PreJumpFrames: Positive(s, "preJumpFrames", h),
        AirborneFrames: Positive(s, "airborneFrames", h),
        LandingFrames: Positive(s, "landingFrames", h),
        PushHalfWidth: Fix.FromInt(Positive(s, "pushHalfWidth", h)),
        PushHeight: Fix.FromInt(Positive(s, "pushHeight", h)),
        ProximityGuard: Fix.FromInt(Positive(s, "proximityGuard", h)),
        CrouchTransitionFrames: Positive(s, "crouchTransitionFrames", h),
        PushShare: Fix.FromRatio(PerMille(s, "pushShare", h), 1000),
        ThrowRange: Fix.FromInt(Positive(s, "throwRange", h)),
        TechFrame: Positive(s, "techFrame", h),
        TechWindow: Positive(s, "techWindow", h),
        TechSeparation: Fix.FromInt(Int(s, "techSeparation", h)),
        ThrowDistance: Fix.FromInt(Positive(s, "throwDistance", h)),
        ThrowFollowThrough: Int(s, "throwFollowThrough", h),
        KnockdownFrames: Positive(s, "knockdownFrames", h),
        WakeupThrowInvulnerable: Int(s, "wakeupThrowInvulnerable", h),
        JumpVelocity: Fix.FromRatio(Positive(s, "jumpVelocity", h), 1000),
        Gravity: Fix.FromRatio(Positive(s, "gravity", h), 1000),
        JumpForward: Fix.FromRatio(Positive(s, "jumpForward", h), 1000),
        JumpBack: Fix.FromRatio(Positive(s, "jumpBack", h), 1000),
        ForwardDashDistance: Fix.FromInt(Positive(s, "forwardDashDistance", h)),
        BackDashDistance: Fix.FromInt(Positive(s, "backDashDistance", h)),
        ForwardDashRecovery: Int(s, "forwardDashRecovery", h),
        BackDashRecovery: Int(s, "backDashRecovery", h),
        DashInputWindow: Positive(s, "dashInputWindow", h),
        CrouchHitstunBonus: Int(s, "crouchHitstunBonus", h),
        LowLifeReduction: Flag(s, "lowLifeReduction", h),
        AirHitRise: Fix.FromRatio(Int(s, "airHitRise", h), 1000),
        AirHitDrift: Fix.FromRatio(Int(s, "airHitDrift", h), 1000),
        QuickRiseFrames: Positive(s, "quickRiseFrames", h),
        QuickRiseWindow: Positive(s, "quickRiseWindow", h),
        DizzyFrames: Positive(s, "dizzyFrames", h),
        StunRecoveryDelay: Int(s, "stunRecoveryDelay", h),
        StunRecoveryRate: Int(s, "stunRecoveryRate", h),
        MotionWindow: Positive(s, "motionWindow", h),
        MotionButtonLeniency: Int(s, "motionButtonLeniency", h),
        ProjectileSpeedLight: Fix.FromRatio(Positive(s, "projectileSpeedLight", h), 1000),
        ProjectileSpeedMedium: Fix.FromRatio(Positive(s, "projectileSpeedMedium", h), 1000),
        ProjectileSpeedHeavy: Fix.FromRatio(Positive(s, "projectileSpeedHeavy", h), 1000));

    // A share from 1 to 1000.
    private static int PerMille(JsonElement s, string key, DataHasher h)
    {
        var value = Positive(s, key, h);
        if (value > 1000)
            throw new CharacterDataException($"'{key}' is per mille, at most 1000, found {value}");
        return value;
    }

    private static SkeletonSize ReadSkeleton(JsonElement s, DataHasher h) => new(
        Spine: Fix.FromInt(Positive(s, "spine", h)),
        Neck: Fix.FromInt(Positive(s, "neck", h)),
        UpperArm: Fix.FromInt(Positive(s, "upperArm", h)),
        Forearm: Fix.FromInt(Positive(s, "forearm", h)),
        Thigh: Fix.FromInt(Positive(s, "thigh", h)),
        Shin: Fix.FromInt(Positive(s, "shin", h)),
        HeadRadius: Fix.FromInt(Positive(s, "headRadius", h)),
        TorsoRadius: Fix.FromInt(Positive(s, "torsoRadius", h)),
        LimbRadius: Fix.FromInt(Positive(s, "limbRadius", h)));

    private static Move ReadMove(JsonElement m, DataHasher h)
    {
        var input = Str(m, "input");
        try
        {
            h.Add(input);
            var name = Str(m, "name");
            h.Add(name);
            var startup = Positive(m, "startup", h);
            var active = Positive(m, "active", h);
            var recovery = Int(m, "recovery", h);
            var hitbox = Obj(m, "hitbox");
            var limb = Parse<Limb>(hitbox, "limb", h);
            var total = startup - 1 + active + recovery;

            var hurt = ImmutableArray.CreateBuilder<HurtModifier>();
            if (m.TryGetProperty("hurt", out var hurtList))
            {
                foreach (var entry in hurtList.EnumerateArray())
                {
                    var from = Int(entry, "from", h);
                    var to = Int(entry, "to", h);
                    if (from < 0 || to < from || to >= total)
                        throw new CharacterDataException($"hurt frames {from}..{to} are outside the move (0..{total - 1})");
                    hurt.Add(new HurtModifier(Parse<Limb>(entry, "limb", h), Parse<HurtMode>(entry, "mode", h), from, to));
                }
            }

            Spawn? spawn = null;
            if (m.TryGetProperty("spawn", out var spawnEntry))
            {
                var frame = Int(spawnEntry, "frame", h);
                if (frame < 0 || frame >= total)
                    throw new CharacterDataException($"spawn frame {frame} is outside the move (0..{total - 1})");
                spawn = new Spawn(frame, Parse<Joint>(spawnEntry, "from", h));
            }

            var move = new Move(
                Name: name,
                Input: input,
                Startup: startup,
                Active: active,
                Recovery: recovery,
                Damage: Int(m, "damage", h),
                Stun: Int(m, "stun", h),
                Kind: m.TryGetProperty("kind", out _) ? Parse<MoveKind>(m, "kind", h) : MoveKind.Normal,
                Strength: Parse<Strength>(m, "strength", h),
                Guard: Parse<Guard>(m, "guard", h),
                PushbackHit: Int(m, "pushbackHit", h),
                PushbackBlock: Int(m, "pushbackBlock", h),
                Chip: OptionalInt(m, "chip", h) ?? 0,
                ChipKo: Flag(m, "chipKo", h),
                JuggleStart: OptionalInt(m, "juggleStart", h) ?? 1,
                JuggleIncrease: OptionalInt(m, "juggleIncrease", h) ?? 1,
                JuggleLimit: OptionalInt(m, "juggleLimit", h) ?? 0,
                HardKnockdown: Flag(m, "hardKnockdown", h),
                HitstunOverride: OptionalInt(m, "hitstun", h),
                BlockstunOverride: OptionalInt(m, "blockstun", h),
                HitboxLimb: limb,
                HitboxRadius: Fix.FromInt(Positive(hitbox, "radius", h)),
                ActiveBonus: Fix.FromInt(Int(hitbox, "activeBonus", h)),
                Hurt: hurt.ToImmutable(),
                Spawn: spawn,
                Track: ReadTrack(Obj(m, "keys", isArray: true), total, "pose", h));
            return move;
        }
        catch (CharacterDataException e)
        {
            throw new CharacterDataException($"move {input}: {e.Message}");
        }
    }

    private static PoseTrack ReadTrack(JsonElement keys, int length, string what, DataHasher h)
    {
        if (length < 1)
            throw new CharacterDataException($"{what}: length must be at least 1");
        h.Add(length);

        var builder = ImmutableArray.CreateBuilder<PoseKey>();
        var index = 0;
        foreach (var k in keys.EnumerateArray())
        {
            try
            {
                var frame = Int(k, "frame", h);
                if (index == 0 && frame != 0)
                    throw new CharacterDataException("the first key must be on frame 0");
                if (index > 0 && frame <= builder[^1].Frame)
                    throw new CharacterDataException($"frame {frame} is not after the previous key's frame {builder[^1].Frame}");
                if (frame >= length)
                    throw new CharacterDataException($"frame {frame} is past the end (length {length})");

                var hip = k.TryGetProperty("hip", out var hipValue) && hipValue.ValueKind == JsonValueKind.Array && hipValue.GetArrayLength() == 2
                    ? new FixVec2(Fix.FromInt(IntValue(hipValue[0], "hip[0]", h)), Fix.FromInt(IntValue(hipValue[1], "hip[1]", h)))
                    : throw new CharacterDataException("'hip' must be [x, y]");

                var angles = Obj(k, "angles");
                var a = new Angle[AngleNames.Length];
                for (var i = 0; i < AngleNames.Length; i++)
                {
                    var units = Int(angles, AngleNames[i], h);
                    if (units <= -Angle.UnitsPerTurn || units >= Angle.UnitsPerTurn)
                        throw new CharacterDataException($"angle {AngleNames[i]} = {units} is more than a full turn");
                    a[i] = Angle.FromUnits(units);
                }

                builder.Add(new PoseKey(frame, hip, a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9]));
                index++;
            }
            catch (CharacterDataException e)
            {
                throw new CharacterDataException($"{what} key {index}: {e.Message}");
            }
        }

        if (builder.Count == 0)
            throw new CharacterDataException($"{what}: needs at least one key");
        return new PoseTrack(length, builder.ToImmutable());
    }

    private static JsonElement Obj(JsonElement e, string key, bool isArray = false)
    {
        var kind = isArray ? JsonValueKind.Array : JsonValueKind.Object;
        if (!e.TryGetProperty(key, out var value) || value.ValueKind != kind)
            throw new CharacterDataException($"'{key}' is missing or not {(isArray ? "an array" : "an object")}");
        return value;
    }

    private static string Str(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text)
            throw new CharacterDataException($"'{key}' is missing or not a string");
        return text;
    }

    private static int Int(JsonElement e, string key, DataHasher? h = null)
    {
        if (!e.TryGetProperty(key, out var value))
            throw new CharacterDataException($"'{key}' is missing");
        return IntValue(value, key, h);
    }

    private static int? OptionalInt(JsonElement e, string key, DataHasher h)
    {
        if (!e.TryGetProperty(key, out var value))
        {
            h.Add(-1);
            return null;
        }
        return IntValue(value, key, h);
    }

    // An optional true/false; missing means false.
    private static bool Flag(JsonElement e, string key, DataHasher h)
    {
        var value = false;
        if (e.TryGetProperty(key, out var element))
        {
            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new CharacterDataException($"'{key}' must be true or false");
            value = element.ValueKind == JsonValueKind.True;
        }
        h.Add(value ? 1 : 0);
        return value;
    }

    private static int IntValue(JsonElement value, string key, DataHasher? h)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new CharacterDataException($"'{key}' must be a whole number, found {value.GetRawText()}");
        h?.Add(number);
        return number;
    }

    private static int Positive(JsonElement e, string key, DataHasher h)
    {
        var value = Int(e, key, h);
        if (value <= 0)
            throw new CharacterDataException($"'{key}' must be positive, found {value}");
        return value;
    }

    private static T Parse<T>(JsonElement e, string key, DataHasher h)
        where T : struct, Enum
    {
        var text = Str(e, key);
        if (!Enum.TryParse<T>(text, ignoreCase: true, out var value))
            throw new CharacterDataException($"'{key}' = '{text}' is not one of {string.Join(", ", Enum.GetNames<T>())}");
        h.Add(Convert.ToInt32(value, CultureInfo.InvariantCulture));
        return value;
    }

    private sealed class CharacterDataException : Exception
    {
        public CharacterDataException(string message)
            : base(message)
        {
        }
    }

    // 64-bit FNV-1a over every value that was read, in reading order. Whitespace and comments
    // don't affect it; any change to a number, name or key does.
    private sealed class DataHasher
    {
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(int value)
        {
            for (var i = 0; i < 4; i++)
            {
                Value ^= (byte)(value >> (8 * i));
                Value *= 1099511628211UL;
            }
        }

        public void Add(string text)
        {
            Add(text.Length);
            foreach (var c in text)
                Add(c);
        }
    }
}
