using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;

namespace CCCC
{
    internal static class PositionChecks
    {
        private static int checks;
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); checks++; }
        private static bool Near(double a, double b) { return Math.Abs(a - b) < .00001; }
        private static void PointIs(Point actual, double x, double y, string name)
        { Check(Near(actual.X, x) && Near(actual.Y, y), name + ": " + actual); }

        [STAThread]
        internal static int Main()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CCCC-position-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Rect work = new Rect(0, 0, 1920, 1040);
                ToastPosition[] presets = ToastPlacement.Presets;
                double[] x = { -12, -12, 1508, 1508, 748, 748, 748 };
                double[] y = { 908, -12, 908, -12, -12, 448, 908 };
                Check(presets.Length == 7, "seven presets");
                for (int i = 0; i < presets.Length; i++)
                {
                    PointIs(ToastPlacement.Resolve(presets[i], work, 424, 144, 1, 0, 0), x[i], y[i], "preset " + presets[i]);
                    PointIs(ToastPlacement.Resolve(presets[i], new Rect(-1920, -200, 1920, 1040), 424, 144, 1, 0, 0),
                        x[i] - 1920, y[i] - 200, "negative monitor " + presets[i]);
                    PointIs(ToastPlacement.Resolve(presets[i], new Rect(0, 0, 2880, 1560), 636, 216, 1.5, 0, 0),
                        x[i] * 1.5, y[i] * 1.5, "150% DPI " + presets[i]);
                }
                PointIs(ToastPlacement.Resolve(ToastPosition.Automatic, work, 424, 144, 1, 0, 0), -12, 908, "legacy card default preserved");
                PointIs(ToastPlacement.Resolve(ToastPosition.Custom, work, 424, 144, 1, 0, 0), -32, -32, "custom top left visible edge");
                PointIs(ToastPlacement.Resolve(ToastPosition.Custom, work, 424, 144, 1, 1, 1), 1528, 928, "custom bottom right visible edge");
                PointIs(ToastPlacement.Resolve(ToastPosition.Custom, work, 424, 144, 1, .5, .5), 748, 448, "custom center");
                PointIs(ToastPlacement.Normalize(work, new Rect(748, 448, 424, 144), 1), .5, .5, "normalized center");
                PointIs(ToastPlacement.Normalize(work, new Rect(-5000, 5000, 424, 144), 1), 0, 1, "offscreen drag clamped");
                PointIs(ToastPlacement.Resolve(ToastPosition.Custom, new Rect(0, 0, 100, 60), 424, 144, 1, 1, 1), -32, -32, "tiny work area remains reachable");
                foreach (double scale in new double[] { 1, 1.25, 1.5, 2, 2.5 })
                    foreach (bool compact in new bool[] { false, true })
                    {
                        double width = Math.Round((compact ? 289 : 424) * scale);
                        double height = Math.Round((compact ? 96 : 144) * scale);
                        double bleed = Math.Round(32 * scale);
                        foreach (ToastPosition preset in presets)
                        {
                            Point p = ToastPlacement.Resolve(preset, work, width, height, scale, 0, 0);
                            Rect visible = new Rect(p.X + bleed, p.Y + bleed, width - 2 * bleed, height - 2 * bleed);
                            Check(work.Contains(visible), "visible preset fits work area " + preset + "/" + compact + "/" + scale);
                        }
                        Point custom = ToastPlacement.Resolve(ToastPosition.Custom, work, width, height, scale, .23, .81);
                        Point restored = ToastPlacement.Normalize(work, new Rect(custom.X, custom.Y, width, height), scale);
                        Check(Math.Abs(restored.X - .23) < .001 && Math.Abs(restored.Y - .81) < .001,
                            "custom round trip across layouts and DPI " + compact + "/" + scale);
                    }

                string settings = Path.Combine(directory, "settings.txt");
                Preferences preferences = new Preferences(settings);
                Check(preferences.Position == ToastPosition.Automatic && !preferences.HasCustomPosition, "new install defaults");
                File.WriteAllText(settings, "preview=off\nmaterial=off\nlayout=card\n");
                preferences = new Preferences(settings);
                Check(!preferences.ShowPreview && !preferences.UseMaterial && !preferences.UseTaskbar &&
                    preferences.Position == ToastPosition.Automatic, "old settings migrate without resetting options");
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                preferences.CustomX = .23123456789; preferences.CustomY = .81987654321;
                preferences.CustomMonitor = @"\\.\DISPLAY2"; preferences.HasCustomPosition = true;
                foreach (ToastPosition position in Enum.GetValues(typeof(ToastPosition)))
                {
                    preferences.Position = position;
                    Check(preferences.Save(), "save " + position);
                    Preferences loaded = new Preferences(settings);
                    Check(loaded.Position == position && loaded.HasCustomPosition && loaded.CustomMonitor == preferences.CustomMonitor &&
                        Near(loaded.CustomX, preferences.CustomX) && Near(loaded.CustomY, preferences.CustomY), "restart restores " + position);
                }
                preferences.Position = ToastPosition.Custom; Check(preferences.Save(), "saved custom location");
                using (FileStream locked = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    preferences.CustomX = .1;
                    Check(!preferences.Save() && preferences.LastError != null, "failed position save reported");
                }
                Check(Near(new Preferences(settings).CustomX, .23123456789), "failed save preserves previous position");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed save leaves no temporary files");
                foreach (string invalid in new string[] { "NaN", "Infinity", "-Infinity", "-0.1", "1.1", "bad", "0,5", "" })
                {
                    File.WriteAllText(settings, "position=custom\ncustom-x=" + invalid + "\ncustom-y=0.5\n");
                    Preferences loaded = new Preferences(settings);
                    Check(!loaded.HasCustomPosition && loaded.Position == ToastPosition.Automatic, "invalid custom coordinate " + invalid);
                }
                File.WriteAllText(settings, "position=custom\ncustom-x=0.5\n");
                Check(new Preferences(settings).Position == ToastPosition.Automatic, "incomplete custom coordinates ignored");
                File.WriteAllText(settings, "position=unknown\ncustom-x=0\ncustom-y=1\n");
                Check(new Preferences(settings).Position == ToastPosition.Automatic && new Preferences(settings).HasCustomPosition,
                    "unknown position ignored and edge coordinates accepted");
                Console.WriteLine("PASS: " + checks + " position assertions; no windows shown or user settings modified.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
