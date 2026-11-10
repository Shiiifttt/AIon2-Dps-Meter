using Microsoft.Win32;
using Serilog;

namespace AionDpsMeter.UI.Services
{
    /// <summary>
    /// "Start with Windows": an entry under HKCU\...\Run (per user, no admin needed) that launches this exe.
    /// The registry entry is the setting itself, so turning it off elsewhere (Task Manager's Startup tab
    /// removes or disables it) is respected.
    /// </summary>
    public static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AionDpsMeter";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                    return key?.GetValue(ValueName) is string { Length: > 0 };
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not read the startup entry");
                    return false;
                }
            }
        }

        /// <returns>Whether the change took effect.</returns>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (enabled)
                {
                    if (Command() is not { } command) return false;
                    key.SetValue(ValueName, command);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not change the startup entry");
                return false;
            }
        }

        /// <summary>
        /// Each version is unpacked into its own folder, so point an existing entry at the copy running now
        /// (otherwise Windows would keep starting the old version).
        /// </summary>
        public static void RefreshPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key?.GetValue(ValueName) is not string { Length: > 0 } current) return;
                if (Command() is { } command && !string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                    key.SetValue(ValueName, command);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not update the startup entry");
            }
        }

        private static string? Command()
            => Environment.ProcessPath is { Length: > 0 } exe ? $"\"{exe}\"" : null;
    }
}
