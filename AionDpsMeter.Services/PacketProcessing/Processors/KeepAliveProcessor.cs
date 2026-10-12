using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.PacketProcessing.Shared;
using AionDpsMeter.Services.Services;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // 0E 00 36 <u64 server unix ms>, sent several times a second.
    [PacketOpcode(PacketOpcodes.KeepAlive)]
    public sealed class KeepAliveProcessor(ServerClock serverClock) : IOpcodeProcessor
    {
        private const int KeepAliveLength = 11;

        public void Process(Packet packet)
        {
            if (packet.Data.Length != KeepAliveLength) return;

            var reader = new PacketReader(packet.Data);
            reader.ReadVarInt(); // len
            reader.ReadU16();    // opcode
            var serverMs = (long)reader.ReadU64();

            serverClock.Update(serverMs, packet.ReceivedAt);
        }
    }
}
