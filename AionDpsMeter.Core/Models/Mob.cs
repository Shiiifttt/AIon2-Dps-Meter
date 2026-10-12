using AionDpsMeter.Core.Data;
using AionDpsMeter.Core.GameData.Services;

namespace AionDpsMeter.Core.Models
{
    public class Mob : Entity
    {
        private const int BossHpThreshold = 100_000_000;

        public int MobCode { get; set; }
        public long HpTotal { get; set; }
        public long HpCurrent { get; set; }

        // Stagger (break) gauge as last reported by the server; StaggerMax is 0 until the first update.
        public int StaggerMax { get; set; }
        public int StaggerCurrent { get; set; }

        // When the boss frenzies (enrages), in local UTC. Set once at the pull, cleared when it leaves combat.
        public DateTime? FrenzyAtUtc { get; set; }
        //public new string Name => GetMobName();
        //public bool IsBoss => CanBeBoss();

        public new string Name => GameDataProvider.Instance.GetMobName(MobCode);
        public bool IsBoss => GameDataProvider.Instance.IsBoss(MobCode);

        public bool IsDummy => GameDataProvider.Instance.IsDummy(MobCode);

        private string GetMobName()
        {
            if (GameDataProvider.Instance.IsKnownMob(MobCode)) return GameDataProvider.Instance.GetMobName(MobCode);
            if (HpTotal >= BossHpThreshold) return $"Unknown boss {MobCode}";
            return $"Unknown {MobCode}";
        }

        private bool CanBeBoss()
        {
            if (GameDataProvider.Instance.IsKnownMob(MobCode)) return GameDataProvider.Instance.IsBoss(MobCode);
            if (HpTotal >= BossHpThreshold) return true;
            return false;
        }
    }
}
