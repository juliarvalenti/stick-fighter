namespace StickFighter.Settings;

// What a pad connecting or disconnecting did, for the screen to show.
public readonly record struct PadChange(int Device, bool Connected, int? Player);

// Which pad each player plays on, as pads come and go. A pad that connects goes to a player whose
// pad was pulled out, if one is waiting, otherwise to the first player without one. A player whose
// pad disconnects is waiting for one: in a match that holds the match paused until their pad comes
// back, they take another pad, or they carry on with the keyboard.
//
// Device ids are Godot's joypad ids, as plain numbers so the rule is tested without Godot (a fake
// device layer just calls Changed).
public sealed class PadSlots
{
    private readonly int?[] _pads = new int?[2];
    private readonly bool[] _waiting = new bool[2];

    public int? PadOf(int player) => _pads[player];

    // The first player waiting for their pad back, if any.
    public int? Waiting => _waiting[0] ? 0 : _waiting[1] ? 1 : null;

    // A match holds paused while any player is waiting for their pad.
    public bool HoldsMatch => Waiting is not null;

    public PadChange Changed(int device, bool connected) =>
        new(device, connected, connected ? Connect(device) : Disconnect(device));

    private int? Connect(int device)
    {
        if (OwnerOf(device) is { } owner)
            return owner;
        var player = Waiting ?? (_pads[0] is null ? 0 : _pads[1] is null ? 1 : null);
        if (player is { } p)
            Assign(p, device);
        return player;
    }

    private int? Disconnect(int device)
    {
        if (OwnerOf(device) is not { } player)
            return null;
        _pads[player] = null;
        _waiting[player] = true;
        return player;
    }

    // A player takes a pad (pressing a button on one while waiting, say), leaving whoever had it padless.
    public void Assign(int player, int device)
    {
        if (OwnerOf(device) is { } other && other != player)
            _pads[other] = null;
        _pads[player] = device;
        _waiting[player] = false;
    }

    // A waiting player carries on with the keyboard instead.
    public void UseKeyboard(int player) => _waiting[player] = false;

    private int? OwnerOf(int device) => _pads[0] == device ? 0 : _pads[1] == device ? 1 : null;
}
