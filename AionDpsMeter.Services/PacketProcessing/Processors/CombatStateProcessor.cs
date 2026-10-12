using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.PacketProcessing.Shared;
using AionDpsMeter.Services.Services;
using AionDpsMeter.Services.Services.Entity;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // An entity entering or leaving combat:
    //   <len> 21 8D <entity> <hasDeadline> <inCombat> [<u64 server unix ms>]
    // For bosses with a frenzy (enrage) timer the pull carries the deadline, sent only once.
    [PacketOpcode(PacketOpcodes.CombatState)]
    public sealed class CombatStateProcessor(EntityTracker entityTracker, ServerClock serverClock) : IOpcodeProcessor
    {
        private static readonly TimeSpan MaxFrenzyTimer = TimeSpan.FromHours(1);

        public void Process(Packet packet)
        {
            var reader = new PacketReader(packet.Data);
            reader.ReadVarInt(); // len
            reader.ReadU16();    // opcode

            var entityId = (int)reader.ReadVarInt();
            var hasDeadline = reader.ReadU8() == 1;
            var inCombat = reader.ReadU8() == 1;

            var mob = entityTracker.GetTargetMob(entityId);

            if (!inCombat)
            {
                if (mob is not null) mob.FrenzyAtUtc = null;
                return;
            }

            if (!hasDeadline || reader.Remaining < 8) return;

            var frenzyAt = serverClock.ToLocalUtc((long)reader.ReadU64());
            var remaining = frenzyAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || remaining > MaxFrenzyTimer) return;

            entityTracker.GetOrCreateTargetEntity(entityId).FrenzyAtUtc = frenzyAt;
        }
    }
}
