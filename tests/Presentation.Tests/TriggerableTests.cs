using System.Linq;
using StickFighter.Menus;
using StickFighter.Sim.Characters;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The move list shows what a player can actually do: the moves the sim's input tables reach.
public class TriggerableTests
{
    private static Character Data => Characters.Placeholder;

    [Fact]
    public void EveryNormalAndTheThrowAreListed()
    {
        foreach (var input in new[]
        {
            "5LP", "5MP", "5HP", "5LK", "5MK", "5HK", "2LP", "2MP", "2HP", "2LK", "2MK", "2HK",
            "j.LP", "j.MP", "j.HP", "j.LK", "j.MK", "j.HK", "LP+LK",
        })
        {
            var index = Data.Moves.IndexOf(Data.FindMove(input)!);
            Assert.True(Triggerable.Moves.Contains(index), $"{input} should be listed");
        }
    }

    [Fact]
    public void TheFireballIsHiddenUntilSpecialsCanTriggerIt()
    {
        var fireball = Data.Moves.IndexOf(Data.FindMove("236P")!);
        Assert.DoesNotContain(fireball, Triggerable.Moves);
        Assert.Equal(Data.Moves.Length - 1, Triggerable.Moves.Count);
        Assert.All(Triggerable.Moves, i => Assert.InRange(i, 0, Data.Moves.Length - 1));
        Assert.Equal(Triggerable.Moves.Count, Triggerable.Moves.Distinct().Count());
    }
}
