using System.Collections.Generic;
using StickFighter.Settings;
using Xunit;

namespace StickFighter.Presentation.Tests;

// Controller hot-plug through a fake device layer: pads are plugged in and pulled out, and the
// match holds paused while a player has lost theirs.
public class PadSlotsTests
{
    // Stands in for Godot's joypads: plugging and unplugging tell the slots, as Main does on
    // Input.JoyConnectionChanged.
    private sealed class FakePads
    {
        public PadSlots Slots { get; } = new();
        public List<PadChange> Changes { get; } = new();

        public void Plug(int device) => Changes.Add(Slots.Changed(device, connected: true));

        public void Unplug(int device) => Changes.Add(Slots.Changed(device, connected: false));
    }

    [Fact]
    public void PadsGoToTheFirstPlayerWithoutOne()
    {
        var pads = new FakePads();
        pads.Plug(3);
        pads.Plug(7);
        pads.Plug(9); // nobody left to take it

        Assert.Equal(3, pads.Slots.PadOf(0));
        Assert.Equal(7, pads.Slots.PadOf(1));
        Assert.Equal(new PadChange(9, true, null), pads.Changes[2]);
    }

    [Fact]
    public void APadThatLeavesAndReturnsGoesBackToItsPlayer()
    {
        var pads = new FakePads();
        pads.Plug(3);
        pads.Plug(7);
        pads.Unplug(3);

        Assert.Equal(0, pads.Changes[2].Player);
        Assert.Null(pads.Slots.PadOf(0));
        Assert.True(pads.Slots.HoldsMatch);

        pads.Plug(3);
        Assert.Equal(3, pads.Slots.PadOf(0));
        Assert.False(pads.Slots.HoldsMatch);
    }

    [Fact]
    public void AWaitingPlayerGetsTheNextPadBeforeAPlayerWhoNeverHadOne()
    {
        var pads = new FakePads();
        pads.Slots.Assign(1, 7); // P1 on the keyboard, P2 on pad 7
        pads.Unplug(7);
        pads.Plug(4);

        Assert.Equal(4, pads.Slots.PadOf(1));
        Assert.Null(pads.Slots.PadOf(0));
        Assert.False(pads.Slots.HoldsMatch);
    }

    [Fact]
    public void UnpluggingAPadNobodyUsesChangesNothing()
    {
        var pads = new FakePads();
        pads.Plug(3);
        pads.Plug(7);
        pads.Plug(9);
        pads.Unplug(9);

        Assert.Null(pads.Changes[3].Player);
        Assert.False(pads.Slots.HoldsMatch);
    }

    [Fact]
    public void DisconnectHoldsTheMatchUntilReassignedOrOnTheKeyboard()
    {
        var pads = new FakePads();
        pads.Plug(3);
        pads.Plug(7);
        pads.Plug(9);

        pads.Unplug(3);
        Assert.True(pads.Slots.HoldsMatch);
        Assert.Equal(0, pads.Slots.Waiting);

        // P1 picks up the spare pad.
        pads.Slots.Assign(0, 9);
        Assert.False(pads.Slots.HoldsMatch);

        // P2 loses theirs and carries on with the keyboard.
        pads.Unplug(7);
        Assert.Equal(1, pads.Slots.Waiting);
        pads.Slots.UseKeyboard(1);
        Assert.False(pads.Slots.HoldsMatch);
        Assert.Null(pads.Slots.PadOf(1));
    }

    [Fact]
    public void TakingAnotherPlayersPadLeavesThemWithout()
    {
        var pads = new FakePads();
        pads.Plug(3);
        pads.Plug(7);

        pads.Slots.Assign(0, 7);
        Assert.Equal(7, pads.Slots.PadOf(0));
        Assert.Null(pads.Slots.PadOf(1));
    }
}
