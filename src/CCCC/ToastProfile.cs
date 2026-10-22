namespace CCCC
{
    // The main-line preview enables the taskbar profile by default. The original
    // card-only build remains available for development and animation checks.
    internal static class ToastProfile
    {
#if TASKBAR_EXPERIMENT
        private static bool compact = true;
        internal static readonly bool IsExperiment = true;
        internal const string SettingsFile = "settings-taskbar-experiment.txt";
        internal const string Label = "2.4.7-preview.1";
#else
        private static bool compact = false;
        internal static readonly bool IsExperiment = false;
        internal const string SettingsFile = "settings.txt";
        internal const string Label = "2.4.7-preview.1 · 普通卡片构建";
#endif
        internal static bool Compact { get { return compact; } }
        internal static void SetCompact(bool value)
        { compact = IsExperiment && value; }
    }
}
