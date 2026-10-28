using System;
using System.IO;
using System.Security;

namespace CCCC
{
    internal sealed class Preferences
    {
        internal bool ShowPreview = true;
        internal bool UseMaterial = true;
        internal bool UseTaskbar = ToastProfile.IsExperiment;
        private readonly string path;
        internal string LastError { get; private set; }
        internal Preferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCCC", ToastProfile.SettingsFile)) { }
        internal Preferences(string path)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                    foreach (string raw in File.ReadAllLines(path))
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
            catch (IOException error) { LastError = error.GetType().Name; }
            catch (UnauthorizedAccessException error) { LastError = error.GetType().Name; }
            catch (SecurityException error) { LastError = error.GetType().Name; }
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
