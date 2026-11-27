using AionDpsMeter.Services.Extensions;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Arena;
using AionDpsMeter.Services.Services.Entity;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    [PacketOpcode(PacketOpcodes.RemainHp)]
    internal class RemainHpProcessor(EntityTracker entityTracker, ArenaTracker arenaTracker) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var data = packet.Data;
            int offset = 3;
            var entityIdInfo = data.ReadVarInt(offset);
            offset += entityIdInfo.Length;
            offset += data.ReadVarInt(offset).Length;
            offset += data.ReadVarInt(offset).Length;
            offset += data.ReadVarInt(offset).Length;
            var hpCurrent = data.ReadUInt64Le(offset);
            entityTracker.UpdateTargetEntityHpCurrent(entityIdInfo.Value, (long)hpCurrent);

            if (TryParseVitals(data, out int entityId, out long? hp, out long? hpMax))
                arenaTracker.OnHp(entityId, hp, hpMax);
        }

        // <len> 008D <entity varint> <varint group mask>
        //   mask & 1: <varint count> { <u8 kind> <u32 value> }   mana and other resources
        //   mask & 2: <varint count> { <u8 kind> <u64 value> }   kind 0 = current HP, 7 = max HP
        private static bool TryParseVitals(byte[] data, out int entityId, out long? hp, out long? hpMax)
        {
            hp = hpMax = null;
            entityId = 0;

            int offset = data.ReadVarInt().Length + 2;
            var entity = data.ReadVarInt(offset);
            if (entity.Length <= 0) return false;
            entityId = entity.Value;
            offset += entity.Length;

            var mask = data.ReadVarInt(offset);
            if (mask.Length <= 0) return false;
            offset += mask.Length;

            if ((mask.Value & 1) != 0)
            {
                var count = data.ReadVarInt(offset);
                if (count.Length <= 0) return false;
                offset += count.Length + count.Value * 5;
            }

            if ((mask.Value & 2) == 0) return false;

            var hpCount = data.ReadVarInt(offset);
            if (hpCount.Length <= 0) return false;
            offset += hpCount.Length;

            for (int i = 0; i < hpCount.Value; i++)
            {
                if (offset + 9 > data.Length) break;
                byte kind = data[offset];
                long value = (long)data.ReadUInt64Le(offset + 1);
                offset += 9;
                if (kind == 0) hp = value;
                else if (kind == 7) hpMax = value;
            }

            return hp.HasValue || hpMax.HasValue;
        }
    }
}
