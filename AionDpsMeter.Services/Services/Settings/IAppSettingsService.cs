using AionDpsMeter.Core.Windowing;

namespace AionDpsMeter.Services.Services.Settings
{
    public interface IAppSettingsService
    {
        bool IsPacketLoggingEnabled { get; set; }
        bool IsNicknameHidden { get; set; }
        bool BossOnlyCapture { get; set; }
        bool GroupSummonDamage { get; set; }
        bool OneMinuteDummyMode { get; set; }
        bool OnlyMyDummyDamage { get; set; }
        bool MainClickThrough { get; set; }
        /// <summary>"None", "Ctrl", "Shift" or "Alt": held to invert <see cref="MainClickThrough"/> while down.</summary>
        string ClickThroughHoldKey { get; set; }
        bool ShowPlayerDeaths { get; set; }
        bool ShowBreakBars { get; set; }
        bool ShowBossPatterns { get; set; }
        bool TrackPetCollection { get; set; }
        /// <summary>Popup for nearby monsters that drop souls for unfinished pets, and for soul pickups. Needs TrackPetCollection.</summary>
        bool ShowPetAlerts { get; set; }
        bool ShowSyncPopup { get; set; }
        /// <summary>Players in the arena with their CP and HP, shown only while you are in an arena.</summary>
        bool ShowArenaOverlay { get; set; }
        int HistoryRetantionPeriod { get; set; }
        double WindowOpacity { get; set; }
        string? BackgroundImagePath { get; set; }
        bool RelativeProgressBar { get; set; }

        string ToggleVisibilityHotkey { get; set; }

        // Main window position & size
        double? WindowLeft { get; set; }
        double? WindowTop { get; set; }
        double? WindowWidth { get; set; }
        double? WindowHeight { get; set; }
        double PlayerRowScale { get; set; }
        int UiStyle { get; set; }
        /// <summary>Measured sizes of a meter layout (see MeterLayoutMetrics in the UI), or null if never measured.</summary>
        double[]? GetMeterLayoutMetrics(int uiStyle);
        void SetMeterLayoutMetrics(int uiStyle, double[] metrics);
        bool HistoryHideNonClears { get; set; }

        public bool UseClassColors { get; set; }

        double StatCalcCritChance { get; set; }
        double StatCalcBackAttackRate { get; set; }
        double StatCalcFrontAttackRate { get; set; }
        string StatCalcAttackType { get; set; }
        double StatCalcAttackIncreaseCombatPercent { get; set; }
        double StatCalcPartyDamageBoost { get; set; }
        double StatCalcBossDamageTolerance { get; set; }
        double StatCalcPartySmiteBuff { get; set; }
        double StatCalcBossSmiteResist { get; set; }
        OverlaySettings BufOverlaySettings { get; set; }
        OverlaySettings SkillCdOverlaySettings { get; set; }
        bool TryGetWindowBounds(WindowKey key, out WindowBounds? bounds);
        void SetWindowBounds(WindowKey key, WindowBounds bounds);

        event EventHandler SettingsChanged;
    }
}
