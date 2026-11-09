using System;
using System.IO;
using System.Globalization;
using System.Security;

namespace CCCC
{
    internal sealed class Preferences
    {
        internal bool ShowPreview = true;
        internal bool UseMaterial = true;
        internal bool UseTaskbar = ToastProfile.IsExperiment;
        internal ToastPosition Position = ToastPosition.Automatic;
        internal double CustomX, CustomY;
        internal string CustomMonitor = "";
        internal bool HasCustomPosition;
        private readonly string path;
        internal string LastError { get; private set; }
        internal Preferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCCC", ToastProfile.SettingsFile)) { }
        internal Preferences(string path)
        {
            this.path = path;
            bool hasX = false, hasY = false;
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
                            ToastPosition position;
                            if (ToastPlacement.TryParse(line.Substring(9), out position)) Position = position;
                        }
                        else if (line.StartsWith("custom-x=", StringComparison.Ordinal))
                            hasX = TryCoordinate(line.Substring(9), out CustomX);
                        else if (line.StartsWith("custom-y=", StringComparison.Ordinal))
                            hasY = TryCoordinate(line.Substring(9), out CustomY);
                        else if (line.StartsWith("custom-monitor=", StringComparison.Ordinal)) CustomMonitor = line.Substring(15);
                    }
            }
            catch (IOException error) { LastError = error.GetType().Name; }
            catch (UnauthorizedAccessException error) { LastError = error.GetType().Name; }
            catch (SecurityException error) { LastError = error.GetType().Name; }
            HasCustomPosition = hasX && hasY;
            if (Position == ToastPosition.Custom && !HasCustomPosition) Position = ToastPosition.Automatic;
        }

        private static bool TryCoordinate(string text, out double value)
        { return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && ToastPlacement.ValidFraction(value); }

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
                    "position=" + ToastPlacement.Key(Position) + Environment.NewLine +
                    (HasCustomPosition && ToastPlacement.ValidFraction(CustomX) && ToastPlacement.ValidFraction(CustomY) ?
                        "custom-x=" + CustomX.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                        "custom-y=" + CustomY.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                        "custom-monitor=" + CustomMonitor.Replace("\r", "").Replace("\n", "") + Environment.NewLine : ""));
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
