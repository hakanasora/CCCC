using System;
using System.IO;
using System.Security;

namespace CCCC
{
    internal sealed class Preferences
    {
        internal bool ShowPreview = true;
        internal bool UseMaterial = true;
        internal bool UseTaskbar;
        internal enum LoadState { Missing, Loaded, ReadError }
        internal LoadState State { get; private set; }
        private readonly string path;
        internal string LastError { get; private set; }
        internal Preferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCCC", ToastProfile.SettingsFile)) { }
        internal Preferences(string path)
        {
            this.path = path;
            try
            {
                string[] lines = File.ReadAllLines(path);
                State = LoadState.Loaded;
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line == "preview=off") ShowPreview = false;
                    else if (line == "preview=on") ShowPreview = true;
                    else if (line == "material=off") UseMaterial = false;
                    else if (line == "material=on") UseMaterial = true;
                    else if (line == "layout=card") UseTaskbar = false;
                    else if (line == "layout=taskbar") UseTaskbar = true;
                }
            }
            catch (FileNotFoundException) { State = LoadState.Missing; }
            catch (DirectoryNotFoundException) { State = LoadState.Missing; }
            catch (IOException error) { State = LoadState.ReadError; LastError = error.GetType().Name; }
            catch (UnauthorizedAccessException error) { State = LoadState.ReadError; LastError = error.GetType().Name; }
            catch (SecurityException error) { State = LoadState.ReadError; LastError = error.GetType().Name; }
        }
        internal void ImportLegacy(Preferences legacy)
        {
            if (State != LoadState.Missing || legacy.State != LoadState.Loaded) return;
            ShowPreview = legacy.ShowPreview;
            UseMaterial = legacy.UseMaterial;
            UseTaskbar = false;
        }
        internal bool InitializeFirstRun(LoadState legacyState, StartupRegistration startup)
        {
            if (State == LoadState.ReadError) return false;
            if (State != LoadState.Missing) return true;
            if (legacyState == LoadState.ReadError) return false;
            if (legacyState != LoadState.Missing) return true;
            try
            {
                StartupState existing = startup.Read();
                if (!Save()) return false;
                if (!existing.Exists) startup.SetEnabled(true);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            catch (ArgumentException) { return false; }
        }
        internal bool Save()
        {
            string temporary = null;
            LastError = null;
            try
            {
                string target = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, (ShowPreview ? "preview=on" : "preview=off") + Environment.NewLine +
                    (UseMaterial ? "material=on" : "material=off") + Environment.NewLine +
                    (ToastProfile.IsExperiment ? (UseTaskbar ? "layout=taskbar" : "layout=card") + Environment.NewLine : ""));
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                State = LoadState.Loaded;
                return true;
            }
            catch (IOException error) { LastError = error.GetType().Name; return false; }
            catch (UnauthorizedAccessException error) { LastError = error.GetType().Name; return false; }
            catch (SecurityException error) { LastError = error.GetType().Name; return false; }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (SecurityException) { }
                }
            }
        }
    }

}
