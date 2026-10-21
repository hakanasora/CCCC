using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CCCC
{
    // Only the changed fade/pulse behavior; no clipboard, tray, settings writes,
    // screenshots, material benchmarking or full regression run.
    internal static class AnimationChecks
    {
        private static readonly List<string> results = new List<string>();
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static int passed;
        private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Private).GetValue(value); }
        private static void Frame(ToastWindow toast)
        { typeof(ToastWindow).GetMethod("DrawFrame", Private).Invoke(toast, new object[] { null, EventArgs.Empty }); }
        private static void Check(bool value, string name)
        { if (!value) throw new Exception(name); passed++; results.Add("PASS," + name); }
        private static bool Near(double left, double right) { return Math.Abs(left - right) < .000001; }
        private static void Pump(int milliseconds)
        {
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static Feedback Sample(int count)
        { return new Feedback { Kind = FeedbackKind.Confirmed, Count = count, Title = FeedbackEngine.SuccessTitle(count), Preview = "动画检查 g/y", Foreground = IntPtr.Zero }; }
        private static void Fade()
        {
            WindowFade fade = new WindowFade(); fade.BeginIn(0, 1000);
            fade.Advance(1050); Check(Near(fade.Opacity, .15625), "fadeIn200SmoothstepQuarter");
            fade.Advance(1100); Check(Near(fade.Opacity, .5), "fadeInMidpoint");
            fade.Advance(1150); Check(Near(fade.Opacity, .84375), "fadeInSoftFinish");
            fade.Advance(1200); Check(fade.Opacity == 1 && !fade.Running, "fadeInFinishesAt200");
            fade.BeginOut(1, 2000); fade.Advance(2055);
            Check(Near(fade.Opacity, .84375), "fadeOut220SmoothstepQuarter");
            fade.Advance(2110); Check(Near(fade.Opacity, .5), "fadeOutMidpoint");
            fade.Advance(2220); Check(fade.Opacity == 0 && !fade.Running, "fadeOutFinishesAt220");
            fade.BeginOut(1, 3000); fade.Advance(3110); fade.BeginIn(fade.Opacity, 3110);
            Check(fade.Opacity == .5 && !fade.FadingOut, "reversalPreservesCurrentOpacity");
            fade.Advance(3160); Check(Near(fade.Opacity, .75), "reversalSmoothstepRemainingRange");
            fade.Advance(3210); Check(fade.Opacity == 1 && !fade.Running, "reversalScalesRemainingDuration");
            fade.BeginIn(0, 4000); double prior = 0; bool monotone = true;
            for (int offset = 0; offset <= 200; offset += 10)
            { fade.Advance(4000 + offset); monotone &= fade.Opacity >= prior && fade.Opacity <= 1; prior = fade.Opacity; }
            Check(monotone, "fadeInMonotoneBounded");
            fade.BeginOut(1, 5000); prior = 1; monotone = true;
            for (int offset = 0; offset <= 220; offset += 10)
            { fade.Advance(5000 + offset); monotone &= fade.Opacity <= prior && fade.Opacity >= 0; prior = fade.Opacity; }
            Check(monotone, "fadeOutMonotoneBounded");
            CopyMotion motion = new CopyMotion(); motion.Pulse(0);
            Check(motion.Value(55) == 1 && CopyMotion.CardAmount == .06 && CopyMotion.CounterAmount == .18 && CopyMotion.Return == 155, "repeatPulsePeaksAndTimingUnchanged");
            Check(motion.Value(210) == 0 && !motion.Running, "repeatPulseFinishesAt210");
        }
        private static void EntryAndRepeat()
        {
            Themes.Current = new Appearance { Transparent = false }; Themes.MaterialAvailable = false;
            ToastWindow toast = new ToastWindow(false);
            Field<Grid>(toast, "surface").Opacity = 0;
            try
            {
                toast.CopyPressed(1, IntPtr.Zero, true); toast.Present(Sample(1), true);
                Check(toast.TestPendingEntrance && !toast.TestPendingHit && !toast.TestMotionRunning && toast.Opacity == 0, "hiddenFirstCopyQueuesFadeOnly");
                Check(Field<SampledText>(toast, "title").BuildCount > 0, "firstTitlePreparedBeforeFade");
                Frame(toast);
                Check(!toast.TestMotionRunning && toast.TestScale == 1 && Field<WindowFade>(toast, "windowFade").Running, "firstFrameHasFadeWithoutScale");
                Pump(240); Check(toast.Opacity == 1 && !toast.TestFramesRunning, "firstFadeStopsCallbacks");
                toast.CopyPressed(2, IntPtr.Zero, true);
                Check(toast.TestPendingHit && Field<SampledText>(toast, "times").Text == "×2", "secondKeyQueuesPulseAndPreparesCount");
                Frame(toast); Check(toast.TestMotionRunning && toast.Opacity == 1, "secondKeyStartsPulse");
                Pump(40); Check(toast.TestScale > 1, "secondKeyActuallyEnlargesCard");
                toast.CopyPressed(3, IntPtr.Zero, true); toast.Present(Sample(3), true);
                Check(toast.TestPendingHit && toast.Current.Count == 3, "thirdKeyKeepsRepeatPulse");
                Frame(toast); Pump(240);
                Check(toast.TestScale == 1 && !toast.TestFramesRunning, "repeatSettlesAndStopsCallbacks");

                toast.HideAndClear(); toast.CopyPressed(4, IntPtr.Zero, true); toast.Present(Sample(4), true); Frame(toast);
                Check(!toast.TestMotionRunning && toast.TestScale == 1, "hiddenReappearanceFadeOnlyEvenWhenCountCarries");
                toast.HideAndClear();
                toast.CopyPressed(1, IntPtr.Zero, true); toast.Present(Sample(1), true);
                toast.CopyPressed(2, IntPtr.Zero, true); toast.Present(Sample(2), true);
                Check(toast.TestPendingHit && toast.Current.Count == 2, "secondKeyBeforeFirstFrameMayPulse");
                toast.HideAndClear(); Frame(toast);
                Check(!toast.TestPendingHit && !toast.TestPendingEntrance && !toast.TestFramesRunning, "hideCancelsQueuedRepeat");

                toast.Present(new Feedback { Kind = FeedbackKind.Status, Title = "CCCC 已启动", Preview = "状态提示" }, true); Frame(toast);
                toast.CopyPressed(1, IntPtr.Zero, true); toast.Present(Sample(1), true);
                Check(!toast.TestPendingHit && !toast.TestMotionRunning, "firstKeyOverStatusDoesNotPulse");
                Frame(toast); Pump(240);
                WindowFade fade = Field<WindowFade>(toast, "windowFade");
                toast.Opacity = .4; fade.BeginOut(.4, Native.Now);
                toast.CopyPressed(2, IntPtr.Zero, true); Frame(toast);
                Check(!fade.FadingOut && Near(toast.Opacity, .4) && toast.TestMotionRunning, "repeatDuringFadeOutRestoresWithoutJump");
                toast.HideAndClear();
            }
            finally { toast.Close(); }
        }
        [STAThread]
        private static int Main(string[] args)
        {
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                Fade(); EntryAndRepeat(); results.Add("TOTAL," + passed);
                File.WriteAllLines(Path.Combine(args[0], "animation-check-results.csv"), results.ToArray());
                Console.WriteLine("Animation checks passed: " + passed); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
