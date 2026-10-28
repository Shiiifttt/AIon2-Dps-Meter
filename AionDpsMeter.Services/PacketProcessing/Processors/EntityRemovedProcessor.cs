using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.PacketProcessing.Shared;
using AionDpsMeter.Services.PacketProcessing.Shared.Exceptions;
using AionDpsMeter.Services.Services.Entity;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // An entity left your range or despawned: <len> 42 36 <entity> <u8 0> <u8 reason>.
    // Pairs with the spawn packet (41 36): in a 3 h log every one followed a spawn of the same entity
    // and the entity was never hit afterwards. Monsters come into range at up to ~150 m.
    [PacketOpcode(PacketOpcodes.EntityRemoved)]
    public sealed class EntityRemovedProcessor(EntityTracker entityTracker) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);
            try
            {
                reader.ReadVarInt(); // len
                reader.ReadU16();    // opcode
                entityTracker.NotifyEntityRemoved((int)reader.ReadVarInt());
            }
            catch (PacketParseException) { }
        }
    }
}
