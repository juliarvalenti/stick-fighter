using System;
using System.Collections.Generic;

namespace StickFighter.Settings;

// What a player binds: the four directions and the six attack buttons.
public enum Binding
{
    Up,
    Down,
    Left,
    Right,
    LP,
    MP,
    HP,
    LK,
    MK,
    HK,
}

// A pad input: a button, or an axis pushed past halfway one way (a trigger, or a stick direction).
// Plain numbers (Godot's JoyButton and JoyAxis values) so this file needs no Godot.
public readonly record struct PadInput(bool Axis, int Index, int Sign = 1)
{
    public static PadInput Button(int index) => new(false, index);

    public static PadInput Stick(int axis, int sign) => new(true, axis, sign < 0 ? -1 : 1);

    public override string ToString() => Axis ? $"axis:{Index}:{(Sign < 0 ? "-" : "+")}" : $"button:{Index}";

    public static PadInput? Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var index))
        {
            if (parts[0] == "button" && parts.Length == 2)
                return Button(index);
            if (parts[0] == "axis" && parts.Length == 3)
                return Stick(index, parts[2] == "-" ? -1 : 1);
        }
        return null;
    }
}

// What pressing an input while binding did.
public enum BindResult
{
    Bound,
    Swapped, // the player had it on another binding, which now has this binding's old input
    TakenByOtherPlayer, // the other player has it on the shared keyboard: nothing changes, warn
}

// Each player's keyboard keys and pad inputs, one per binding. Keys are Godot physical keycodes and
// pad inputs Godot's button and axis numbers, kept as plain numbers so this is tested without
// Godot. Both players share one keyboard; each has their own pad, so pads never clash across
// players.
public sealed class Bindings
{
    public static readonly Binding[] All = (Binding[])Enum.GetValues(typeof(Binding));

    private readonly Dictionary<Binding, long>[] _keys = { new(), new() };
    private readonly Dictionary<Binding, PadInput>[] _pads = { new(), new() };
    private readonly Bindings? _defaults;

    // `defaults` is what "reset to defaults" goes back to (today's layouts, from DeviceInput), and
    // what a new set starts as.
    public Bindings(Bindings? defaults = null)
    {
        _defaults = defaults;
        for (var p = 0; p < 2; p++)
            ResetToDefaults(p);
    }

    public long Key(int player, Binding binding) => _keys[player].TryGetValue(binding, out var key) ? key : 0;

    public PadInput? Pad(int player, Binding binding) => _pads[player].TryGetValue(binding, out var pad) ? pad : null;

    // The binding a key or pad input is on for this player, if any.
    public Binding? KeyFor(int player, long key) => Find(_keys[player], key);

    public Binding? PadFor(int player, PadInput pad) => Find(_pads[player], pad);

    // Sets a binding outright (the defaults, loading a file): no conflict handling.
    public void SetKey(int player, Binding binding, long key) => _keys[player][binding] = key;

    public void SetPad(int player, Binding binding, PadInput pad) => _pads[player][binding] = pad;

    public void ClearPad(int player) => _pads[player].Clear();

    // Press-to-bind a key. If this player already has it on another binding, the two swap; if the
    // other player has it, nothing changes and the caller warns.
    public BindResult BindKey(int player, Binding binding, long key)
    {
        if (Find(_keys[1 - player], key) is not null)
            return BindResult.TakenByOtherPlayer;
        var result = BindResult.Bound;
        if (Find(_keys[player], key) is { } other && other != binding)
        {
            if (_keys[player].TryGetValue(binding, out var old))
                _keys[player][other] = old;
            else
                _keys[player].Remove(other);
            result = BindResult.Swapped;
        }
        _keys[player][binding] = key;
        return result;
    }

    // Press-to-bind a pad input, swapping within the player's own pad if it's already on another binding.
    public BindResult BindPad(int player, Binding binding, PadInput pad)
    {
        var result = BindResult.Bound;
        if (Find(_pads[player], pad) is { } other && other != binding)
        {
            if (_pads[player].TryGetValue(binding, out var old))
                _pads[player][other] = old;
            else
                _pads[player].Remove(other);
            result = BindResult.Swapped;
        }
        _pads[player][binding] = pad;
        return result;
    }

    public void ResetToDefaults(int player)
    {
        _keys[player].Clear();
        _pads[player].Clear();
        if (_defaults is null)
            return;
        foreach (var binding in All)
        {
            if (_defaults.Key(player, binding) is var key and not 0)
                _keys[player][binding] = key;
            if (_defaults.Pad(player, binding) is { } pad)
                _pads[player][binding] = pad;
        }
    }

    private static Binding? Find<T>(Dictionary<Binding, T> map, T value)
    {
        foreach (var binding in All)
        {
            if (map.TryGetValue(binding, out var bound) && EqualityComparer<T>.Default.Equals(bound, value))
                return binding;
        }
        return null;
    }
}
