namespace StickFighter.Sim.Net;

// What one peer sends the other each frame:
// - its inputs from FirstFrame on, every one the other hasn't acknowledged yet, so a lost message
//   costs nothing but a little delay (the next carries the same inputs);
// - Ack: the last of the other's inputs it has, every frame up to it (it can stop resending those);
// - a checksum of a confirmed frame, if it has one to compare (ChecksumFrame -1 if not).
public sealed record NetMessage(int FirstFrame, Buttons[] Inputs, int Ack, int ChecksumFrame, ulong Checksum);

// One end of a connection. Send never blocks; TryReceive takes what has arrived, oldest first.
public interface ITransport
{
    void Send(NetMessage message);

    bool TryReceive(out NetMessage message);
}
