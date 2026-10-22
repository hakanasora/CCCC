namespace CCCC
{
    // A build profile keeps the experiment out of the default stable build.
    internal static class ToastProfile
    {
#if TASKBAR_EXPERIMENT
        internal static readonly bool Compact = true;
        internal const string SettingsFile = "settings-taskbar-experiment.txt";
        internal const string Label = "2.4.6 · Taskbar preview 1";
#else
        internal static readonly bool Compact = false;
        internal const string SettingsFile = "settings.txt";
        internal const string Label = "2.4.6";
#endif
    }
}
