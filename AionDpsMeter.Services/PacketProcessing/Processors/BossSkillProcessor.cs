using AionDpsMeter.Services.PacketProcessing.Routing;
using Microsoft.Extensions.Logging;
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
    public sealed class BossSkillProcessor(EntityTracker entityTracker, BossPatternTracker patterns, ILogger<BossSkillProcessor> logger) : IOpcodeProcessor
    {
        private const int MaxCastMs = 60_000;

        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);
            reader.ReadVarInt();               // len
            var opcode = reader.ReadU16();

            var entityId = (int)reader.ReadVarInt();
            if (entityTracker.GetTargetMob(entityId) is not { } mob) return;

            reader.ReadU8();
            var skillCode = (int)reader.ReadU32();
            var at = packet.ReceivedAtUtc;

            // Spawn packet missed (meter started after it came into range): a skill only one boss
            // uses tells us which boss this is - name, Boss Only, records and pattern timers then work.
            // (The pattern table only holds boss skills; players are excluded since PvP makes them targets too.)
            if (mob.MobCode == 0 && entityTracker.GetPlayerEntity(entityId) is null
                && patterns.OnlyBossWithSkill(skillCode) is { } bossCode)
            {
                entityTracker.CreateOrUpdateTargetEntity(entityId, bossCode);
                logger.LogInformation("Recognised {Boss} ({MobCode}) from its skill {Skill}; its spawn packet was missed",
                    mob.Name, bossCode, skillCode);
            }
            if (!mob.IsBoss) return;

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
