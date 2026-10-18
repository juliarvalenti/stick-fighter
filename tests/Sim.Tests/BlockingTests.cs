using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class BlockingTests
{
    private static Character Data => Characters.Characters.Placeholder;

    // P1 on the left, P2 on the right at the given gap. P2's "back" is Right.
    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static Buttons ButtonFor(string input) => input[1..] switch
    {
        "LP" => Buttons.LightPunch,
        "MP" => Buttons.MediumPunch,
        "HP" => Buttons.HeavyPunch,
        "LK" => Buttons.LightKick,
        "MK" => Buttons.MediumKick,
        _ => Buttons.HeavyKick,
    };

    // P1 does the move at point-blank; P2 holds `defence` throughout. Returns P2 after it all plays out.
    private static FighterState Defend(string input, Buttons defence, out int? advantage)
    {
        var state = Apart(60);
        var hold = input[0] == '2' ? Buttons.Down : Buttons.None;
        int? contact = null, attackerFree = null, defenderFree = null;
        Simulation.Step(ref state, new FrameInput(ButtonFor(input) | hold, defence));
        for (var i = 0; i < 80 && (attackerFree is null || defenderFree is null); i++)
        {
            var frame = state.Frame;
            // Free on a frame means no stun left when the frame began (Act spends a frame per stun point).
            var stunnedBefore = state.Fighters[1].Blockstun > 0 || state.Fighters[1].Hitstun > 0;
            Simulation.Step(ref state, new FrameInput(hold, defence));
            var p2 = state.Fighters[1];
            if (contact is not null && defenderFree is null && !stunnedBefore)
                defenderFree = frame;
            if (contact is null && (p2.Blockstun > 0 || p2.Hitstun > 0))
                contact = frame;
            if (contact is not null && attackerFree is null && state.Fighters[0].Action != (int)FighterAction.Attack)
                attackerFree = frame;
        }
        advantage = defenderFree - attackerFree;
        return state.Fighters[1];
    }

    // On-block advantage from reference/frame-data-baseline; lows blocked crouching, the rest standing.
    [Theory]
    [InlineData("5LP", 3)]
    [InlineData("5MP", 2)]
    [InlineData("5HP", -1)]
    [InlineData("5LK", -1)]
    [InlineData("5MK", -2)]
    [InlineData("5HK", -4)]
    [InlineData("2LP", 2)]
    [InlineData("2MP", 1)]
    [InlineData("2HP", -9)]
    [InlineData("2LK", -1)]
    [InlineData("2MK", -2)]
    [InlineData("2HK", -7)]
    public void OnBlockAdvantageMatchesTheBaseline(string input, int expected)
    {
        var move = Data.FindMove(input)!;
        Assert.Equal(expected, move.Blockstun - move.Active - move.Recovery);

        var defence = Buttons.Right | (move.Guard == Characters.Guard.Low ? Buttons.Down : Buttons.None);
        var p2 = Defend(input, defence, out var advantage);
        Assert.Equal(Data.Stats.Health, p2.Health); // blocked; normals do no chip
        Assert.Equal(expected, advantage);
    }

    [Theory]
    [InlineData("5MP", Buttons.Right)]
    [InlineData("5MP", Buttons.Right | Buttons.Down)]
    [InlineData("2MP", Buttons.Right)]
    [InlineData("2MP", Buttons.Right | Buttons.Down)]
    [InlineData("2MK", Buttons.Right | Buttons.Down)]
    public void MidsBlockEitherWayAndLowsCrouching(string input, Buttons defence)
    {
        var p2 = Defend(input, defence, out _);
        Assert.Equal(Data.Stats.Health, p2.Health);
    }

    [Theory]
    [InlineData("2LK")]
    [InlineData("2MK")]
    [InlineData("2HK")]
    public void ALowHitsAStandingBlocker(string input)
    {
        var p2 = Defend(input, Buttons.Right, out _);
        Assert.True(p2.Health < Data.Stats.Health);
    }

    [Fact]
    public void HoldingForwardDoesNotBlock()
    {
        var p2 = Defend("5MP", Buttons.Left, out _);
        Assert.True(p2.Health < Data.Stats.Health);
    }

    // Jump-ins and overheads are high. There's no jumping yet, so the contact is built by hand.
    [Theory]
    [InlineData(false, true)]  // standing block: blocked
    [InlineData(true, false)]  // crouching block: hit
    public void AHighMustBeBlockedStanding(bool crouching, bool blocked)
    {
        var state = HandMadeContact("j.HP", defenderHolds: Buttons.Right | (crouching ? Buttons.Down : Buttons.None), defenderFacing: -1);
        Assert.Equal(blocked, state.Fighters[1].Health == Data.Stats.Health);
    }

    // Crossup: the attacker has landed on the defender's other side, but the defender still faces the
    // old way. "Back" is away from the attacker by position, not by facing.
    [Theory]
    [InlineData(Buttons.Left, true)]   // away from the attacker: blocked
    [InlineData(Buttons.Right, false)] // back by facing, toward the attacker: hit
    public void ACrossupIsBlockedAwayFromTheAttacker(Buttons hold, bool blocked)
    {
        // P2 at 940 is now to P1's left, still facing right (toward where P1 used to be).
        var state = HandMadeContact("j.HP", defenderHolds: hold, defenderFacing: 1, defenderX: 940);
        Assert.Equal(blocked, state.Fighters[1].Health == Data.Stats.Health);
    }

    [Fact]
    public void ProximityGuardStopsWalkingBackNearAnAttack()
    {
        var state = Apart(150); // within proximity range
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default));
        var x = state.Fighters[1].Position.X;
        Simulation.Step(ref state, new FrameInput(default, Buttons.Right)); // P2 holds back during 5HK's startup
        Assert.Equal(x, state.Fighters[1].Position.X);
        Assert.Equal((int)FighterAction.BlockStand, state.Fighters[1].Action);
    }

    [Fact]
    public void OutOfProximityRangeHoldingBackWalksBack()
    {
        var state = Apart(400);
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default));
        var x = state.Fighters[1].Position.X;
        Simulation.Step(ref state, new FrameInput(default, Buttons.Right));
        Assert.True(state.Fighters[1].Position.X > x);
        Assert.Equal((int)FighterAction.WalkBack, state.Fighters[1].Action);
    }

    [Fact]
    public void BlockstunBlocksActingButBuffers()
    {
        var state = Apart(60);
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyPunch, Buttons.Right));
        while (state.Fighters[1].Blockstun == 0)
            Simulation.Step(ref state, new FrameInput(default, Buttons.Right));
        while (state.Fighters[1].Blockstun > 2)
            Simulation.Step(ref state, new FrameInput(default, Buttons.Right));
        Simulation.Step(ref state, new FrameInput(default, Buttons.LightKick));
        Assert.True(state.Fighters[1].Blockstun > 0 || state.Fighters[1].Action != (int)FighterAction.Attack);
        while (state.Fighters[1].Blockstun > 0)
            Simulation.Step(ref state, default);
        Simulation.Step(ref state, default);
        Assert.Equal((int)FighterAction.Attack, state.Fighters[1].Action);
        Assert.Equal("5LK", Data.Moves[state.Fighters[1].Move].Input);
    }

    [Theory]
    [InlineData(100, 30, false, 70)]
    [InlineData(20, 30, false, 1)]  // can't chip-KO
    [InlineData(1, 30, false, 1)]
    [InlineData(20, 30, true, 0)]   // specials and supers can
    [InlineData(100, 0, false, 100)]
    public void ChipNeverKillsUnlessTheMoveCan(int health, int chip, bool chipKo, int expected)
    {
        Assert.Equal(expected, Hits.AfterChip(health, chip, chipKo));
    }

    // P1 is mid-move on its first active frame of `input`, touching P2; P2 holds `defenderHolds`.
    private static GameState HandMadeContact(string input, Buttons defenderHolds, int defenderFacing, int defenderX = 1060)
    {
        var state = Apart(60);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(defenderX), state.Fighters[1].Position.Y);
        state.Fighters[1].Facing = defenderFacing;
        state.Inputs[1].Record(state.Frame, defenderHolds);
        var move = Data.FindMove(input)!;
        state.Fighters[0].Action = (int)FighterAction.Attack;
        state.Fighters[0].Move = Data.Moves.IndexOf(move);
        state.Fighters[0].ActionFrame = move.FirstActiveFrame;

        var contacts = new Contacts { Count = 1 };
        contacts.Items[0] = new Contact { Kind = ContactKind.Hit, Attacker = 0, Defender = 1 };
        Hits.Apply(ref state, contacts);
        return state;
    }
}
