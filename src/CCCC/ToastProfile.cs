namespace CCCC
{
    // A build profile keeps the experiment out of the default stable build.
    internal static class ToastProfile
    {
#if TASKBAR_EXPERIMENT
        private static bool compact = true;
        internal static readonly bool IsExperiment = true;
        internal const string SettingsFile = "settings-taskbar-experiment.txt";
        internal const string Label = "2.4.6 · Taskbar preview 4";
#else
        private static bool compact = false;
        internal static readonly bool IsExperiment = false;
        internal const string SettingsFile = "settings.txt";
        internal const string Label = "2.4.6";
#endif
        internal static bool Compact { get { return compact; } }
        internal static void SetCompact(bool value)
        { compact = IsExperiment && value; }
    }
}
