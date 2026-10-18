using StickFighter.Settings;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The options and bindings: they survive a save and load, and binding an input that's in use
// either swaps (the player's own) or is refused with a warning (the other player's key).
public class SettingsTests
{
    // Stand-ins for DeviceInput's layouts: P1 keys 1..10, P2 keys 101..110; pads buttons 0..9.
    private static Bindings Defaults()
    {
        var defaults = new Bindings();
        foreach (var binding in Bindings.All)
        {
            for (var p = 0; p < 2; p++)
            {
                defaults.SetKey(p, binding, p * 100 + 1 + (int)binding);
                defaults.SetPad(p, binding, PadInput.Button((int)binding));
            }
        }
        defaults.SetPad(0, Binding.HK, PadInput.Stick(5, +1)); // a trigger, as the real layout has
        return defaults;
    }

    [Fact]
    public void ANewSetStartsOnTheDefaults()
    {
        var settings = new UserSettings(Defaults());
        Assert.Equal(1, settings.Bindings.Key(0, Binding.Up));
        Assert.Equal(110, settings.Bindings.Key(1, Binding.HK));
        Assert.Equal(PadInput.Stick(5, +1), settings.Bindings.Pad(0, Binding.HK));
    }

    [Fact]
    public void EverythingRoundTrips()
    {
        var settings = new UserSettings(Defaults())
        {
            MusicVolume = 35,
            EffectsVolume = 0,
            AnnouncerVolume = 100,
            InputDelay = 3,
            Fullscreen = true,
            Vsync = false,
        };
        settings.Bindings.BindKey(0, Binding.LP, 500);
        settings.Bindings.BindKey(1, Binding.Up, 600);
        settings.Bindings.BindPad(0, Binding.MP, PadInput.Stick(4, +1));
        settings.Bindings.BindPad(1, Binding.Left, PadInput.Stick(0, -1));

        var text = settings.Write();
        var loaded = UserSettings.Read(text, Defaults());

        Assert.Equal(35, loaded.MusicVolume);
        Assert.Equal(0, loaded.EffectsVolume);
        Assert.Equal(100, loaded.AnnouncerVolume);
        Assert.Equal(3, loaded.InputDelay);
        Assert.True(loaded.Fullscreen);
        Assert.False(loaded.Vsync);
        for (var p = 0; p < 2; p++)
        {
            foreach (var binding in Bindings.All)
            {
                Assert.Equal(settings.Bindings.Key(p, binding), loaded.Bindings.Key(p, binding));
                Assert.Equal(settings.Bindings.Pad(p, binding), loaded.Bindings.Pad(p, binding));
            }
        }
        Assert.Equal(text, loaded.Write());
    }

    [Fact]
    public void AnEmptyOrDamagedFileKeepsTheDefaults()
    {
        var empty = UserSettings.Read("", Defaults());
        Assert.Equal(80, empty.MusicVolume);
        Assert.True(empty.Vsync);
        Assert.Equal(1, empty.Bindings.Key(0, Binding.Up));

        var damaged = UserSettings.Read("[audio]\nmusic=loud\neffects=250\n[input]\ndelay=9\n[p1.keys]\nUp=\nJump=4\n[p3.keys]\nUp=7\n[p1.pad]\nLP=nonsense\n", Defaults());
        Assert.Equal(80, damaged.MusicVolume);
        Assert.Equal(100, damaged.EffectsVolume); // clamped
        Assert.Equal(UserSettings.MaxInputDelay, damaged.InputDelay);
        Assert.Equal(1, damaged.Bindings.Key(0, Binding.Up));
        Assert.Equal(PadInput.Button((int)Binding.LP), damaged.Bindings.Pad(0, Binding.LP));
    }

    [Fact]
    public void BindingAFreeKeyJustBindsIt()
    {
        var bindings = new Bindings(Defaults());
        Assert.Equal(BindResult.Bound, bindings.BindKey(0, Binding.LP, 500));
        Assert.Equal(500, bindings.Key(0, Binding.LP));
    }

    [Fact]
    public void BindingAPlayersOwnKeySwapsTheTwo()
    {
        var bindings = new Bindings(Defaults());
        var lp = bindings.Key(0, Binding.LP);
        var hk = bindings.Key(0, Binding.HK);

        Assert.Equal(BindResult.Swapped, bindings.BindKey(0, Binding.LP, hk));
        Assert.Equal(hk, bindings.Key(0, Binding.LP));
        Assert.Equal(lp, bindings.Key(0, Binding.HK));
    }

    [Fact]
    public void TheOtherPlayersKeyIsRefused()
    {
        var bindings = new Bindings(Defaults());
        var theirs = bindings.Key(1, Binding.Up);

        Assert.Equal(BindResult.TakenByOtherPlayer, bindings.BindKey(0, Binding.LP, theirs));
        Assert.Equal(1 + (int)Binding.LP, bindings.Key(0, Binding.LP));
        Assert.Equal(theirs, bindings.Key(1, Binding.Up));
    }

    [Fact]
    public void PadsSwapWithinAPlayerAndNeverClashAcrossPlayers()
    {
        var bindings = new Bindings(Defaults());
        var trigger = PadInput.Stick(5, +1);

        Assert.Equal(BindResult.Swapped, bindings.BindPad(0, Binding.LP, trigger));
        Assert.Equal(trigger, bindings.Pad(0, Binding.LP));
        Assert.Equal(PadInput.Button((int)Binding.LP), bindings.Pad(0, Binding.HK));

        // P2's pad is their own: the same input binds without a word.
        Assert.Equal(BindResult.Bound, bindings.BindPad(1, Binding.MP, trigger));
    }

    [Fact]
    public void ResetGoesBackToTheDefaultsForThatPlayerOnly()
    {
        var bindings = new Bindings(Defaults());
        bindings.BindKey(0, Binding.LP, 500);
        bindings.BindKey(1, Binding.LP, 600);
        bindings.BindPad(0, Binding.LP, PadInput.Stick(2, -1));

        bindings.ResetToDefaults(0);

        Assert.Equal(1 + (int)Binding.LP, bindings.Key(0, Binding.LP));
        Assert.Equal(PadInput.Button((int)Binding.LP), bindings.Pad(0, Binding.LP));
        Assert.Equal(600, bindings.Key(1, Binding.LP));
    }

    [Fact]
    public void PadInputsReadBackFromText()
    {
        foreach (var pad in new[] { PadInput.Button(0), PadInput.Button(14), PadInput.Stick(5, +1), PadInput.Stick(1, -1) })
            Assert.Equal(pad, PadInput.Parse(pad.ToString()));
        Assert.Null(PadInput.Parse("axis:5"));
        Assert.Null(PadInput.Parse("key:4"));
    }
}
