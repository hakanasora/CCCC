namespace CCCC
{
    internal static class ToastProfile
    {
#if TASKBAR_EXPERIMENT
        private static bool compact = false;
        internal static readonly bool IsExperiment = true;
        internal const string SettingsFile = "settings-taskbar-experiment.txt";
        internal const string Label = "2.5.0";
#else
        private static bool compact = false;
        internal static readonly bool IsExperiment = false;
        internal const string SettingsFile = "settings.txt";
        internal const string Label = "2.5.0 · 普通卡片构建";
#endif
        internal static bool Compact { get { return compact; } }
        internal static void SetCompact(bool value)
        { compact = IsExperiment && value; }
    }
}
