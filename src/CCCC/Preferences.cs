using System;
using System.IO;
using System.Security;

namespace CCCC
{
    // Screen corner where the copy feedback card appears. Bottom-left matches
    // the original placement and stays the default when no setting is stored.
    internal enum ToastCorner { BottomLeft, BottomRight, TopLeft, TopRight }

    internal sealed class Preferences
    {
        internal bool ShowPreview = true;
        internal bool UseMaterial = true;
        internal bool UseTaskbar = ToastProfile.IsExperiment;
        internal ToastCorner Corner = ToastCorner.BottomLeft;
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
                        else if (line.StartsWith("position=", StringComparison.Ordinal))
                        {
                            ToastCorner corner;
                            if (Enum.TryParse(line.Substring(9), true, out corner) &&
                                Enum.IsDefined(typeof(ToastCorner), corner)) Corner = corner;
                        }
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
                    (ToastProfile.IsExperiment ? (UseTaskbar ? "layout=taskbar" : "layout=card") + Environment.NewLine : "") +
                    "position=" + Corner.ToString().ToLowerInvariant() + Environment.NewLine);
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
