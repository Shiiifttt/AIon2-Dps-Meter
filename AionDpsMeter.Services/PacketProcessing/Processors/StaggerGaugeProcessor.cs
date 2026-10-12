using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.PacketProcessing.Shared;
using AionDpsMeter.Services.Services.Session;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // Boss stagger (break) gauge. Counts down from max as players hit; staggers at 0.
    //   update: <len> 05 E0 <entity> 03 01 <u32 max> <u32 current> 02
    //   broken: <len> 05 E0 <entity> 00 03 02
    // The gauge refills to max ~5s after a break (the stagger debuff lasts 5000 ms).
    [PacketOpcode(PacketOpcodes.StaggerGauge)]
    public sealed class StaggerGaugeProcessor(CombatSessionManager sessionManager) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);
            reader.ReadVarInt(); // len
            reader.ReadU16();    // opcode

            var entityId = (int)reader.ReadVarInt();
            var kind = reader.ReadU8();
            var sub = reader.ReadU8();

            if (kind == 0x03 && sub == 0x01 && reader.Remaining >= 8)
            {
                var max = (int)reader.ReadU32();
                var current = (int)reader.ReadU32();
                if (max <= 0 || current < 0 || current > max) return;
                sessionManager.ProcessStaggerGauge(entityId, current, max, broken: false, DateTime.Now);
            }
            else if (kind == 0x00 && sub == 0x03)
            {
                sessionManager.ProcessStaggerGauge(entityId, 0, 0, broken: true, DateTime.Now);
            }
        }
    }
}
