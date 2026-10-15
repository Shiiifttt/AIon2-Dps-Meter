using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.PacketProcessing.Shared;
using AionDpsMeter.Services.Services.BossPatterns;
using AionDpsMeter.Services.Services.Entity;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // Skills used by an entity (players and mobs alike); only bosses are passed on.
    //   cast bar: <len> 09 38 <entity> <u8> <u32 skill> <u8 seq> <u8 targetKind> <target> <target pos> <varint cast ms> ...
    //   used:     <len> 02 38 <entity> <u8> <u32 skill> <u8 seq> ...
    // targetKind 2 is followed by 16 bytes (x, y, z, ?), 0 by 4. A cast bar is followed by "used"
    // when it finishes; "finished" (06 38) comes after that and isn't needed.
    [PacketOpcode(PacketOpcodes.SkillCast)]
    [PacketOpcode(PacketOpcodes.SkillUse)]
    public sealed class BossSkillProcessor(EntityTracker entityTracker, BossPatternTracker patterns) : IOpcodeProcessor
    {
        private const int MaxCastMs = 60_000;

        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);
            reader.ReadVarInt();               // len
            var opcode = reader.ReadU16();

            var entityId = (int)reader.ReadVarInt();
            if (entityTracker.GetTargetMob(entityId) is not { IsBoss: true } mob) return;

            reader.ReadU8();
            var skillCode = (int)reader.ReadU32();
            var at = packet.ReceivedAtUtc;

            if (opcode == PacketOpcodes.SkillUse)
            {
                patterns.OnSkillUsed(entityId, skillCode, at);
                return;
            }

            reader.ReadU8(); // seq
            var targetKind = reader.ReadU8();
            reader.ReadVarInt(); // target
            reader.Skip(targetKind == 2 ? 16 : 4);
            var castMs = (int)reader.ReadVarInt();
            if (castMs <= 0 || castMs > MaxCastMs) return;

            patterns.OnCastStart(entityId, mob.MobCode, skillCode, castMs, at);
        }
    }
}
