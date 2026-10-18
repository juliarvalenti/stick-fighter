using System;
using System.Runtime.CompilerServices;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// Everything the match needs, in one value type with no references, so saving a frame
// is a plain copy (`var saved = state;`) and loading is an assignment.
//
// Adding a field: add it to Encode too, in a fixed place, and bump EncodedSize.
// Anything left out of Encode is invisible to the checksum and to desync detection.
public struct GameState
{
    public const int PlayerCount = 2;

    // The rules allow one fireball per player on screen.
    public const int MaxProjectiles = 2;

    public const int EncodedSize =
        4 + 16
        + PlayerCount * InputHistory.EncodedSize
        + PlayerCount * FighterState.EncodedSize
        + MaxProjectiles * ProjectileState.EncodedSize
        + Stage.EncodedSize
        + MatchState.EncodedSize;

    // How far each fighter starts from the middle of the stage.
    public const int StartOffset = 200;

    public int Frame;

    // Mutable struct: call it in place (`state.Rng.NextInt(…)` on a `ref` state).
    // `var r = state.Rng; r.NextInt(…)` advances a copy and the match's RNG never moves.
    public Rng Rng;

    // Each player's recent input, written by Step before anything reads it.
    public InputHistories Inputs;

    public FighterPair Fighters;
    public ProjectileSlots Projectiles;

    // Fixed for the match, but kept here so the checksum covers it.
    public Stage Stage;

    // Rounds, the timer and the score (Versus), or nothing to track (Training).
    public MatchState Match;

    public static GameState NewMatch(ulong seed) => NewMatch(seed, Stage.Training);

    public static GameState NewMatch(ulong seed, in Stage stage, MatchMode mode = MatchMode.Training)
    {
        var state = new GameState { Rng = Rng.Create(seed), Stage = stage };
        PlaceFighters(ref state);
        state.Match = Matches.MatchFlow.Start(mode);
        return state;
    }

    // Both fighters fresh at their start positions: the start of a match, and of every round.
    public static void PlaceFighters(ref GameState state)
    {
        var stage = state.Stage;
        state.Fighters[0] = Fresh(new FixVec2(stage.Center - StartOffset, stage.Floor), 1);
        state.Fighters[1] = Fresh(new FixVec2(stage.Center + StartOffset, stage.Floor), -1);
        state.Projectiles = default;
    }

    private static FighterState Fresh(FixVec2 position, int facing) => new()
    {
        Position = position,
        Facing = facing,
        Health = Characters.Characters.Placeholder.Stats.Health,
        Move = -1,
        LastMoveFrame = -1,
        Grab = -1,
    };

    // The canonical encoding: fixed field order, little-endian, no padding.
    public readonly void Encode(Span<byte> buffer)
    {
        var writer = new StateWriter(buffer);
        writer.Write(Frame);
        writer.Write(Rng.State);
        writer.Write(Rng.Increment);
        for (var i = 0; i < PlayerCount; i++)
            Inputs[i].Encode(ref writer);
        for (var i = 0; i < PlayerCount; i++)
            Fighters[i].Encode(ref writer);
        for (var i = 0; i < MaxProjectiles; i++)
            Projectiles[i].Encode(ref writer);
        Stage.Encode(ref writer);
        Match.Encode(ref writer);
    }

    // 64-bit FNV-1a over the canonical encoding. Same state, same checksum, on every machine.
    public readonly ulong Checksum()
    {
        Span<byte> bytes = stackalloc byte[EncodedSize];
        Encode(bytes);
        var hash = 14695981039346656037UL;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    // True when every encoded field matches.
    public readonly bool StateEquals(in GameState other)
    {
        Span<byte> a = stackalloc byte[EncodedSize];
        Span<byte> b = stackalloc byte[EncodedSize];
        Encode(a);
        other.Encode(b);
        return a.SequenceEqual(b);
    }
}

[InlineArray(GameState.PlayerCount)]
public struct FighterPair
{
    private FighterState _element;
}

[InlineArray(GameState.MaxProjectiles)]
public struct ProjectileSlots
{
    private ProjectileState _element;
}
