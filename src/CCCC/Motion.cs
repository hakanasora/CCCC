using System;

namespace CCCC
{
    // Whole-window fading is independent of copy pulses. Reversing a fade uses
    // the last displayed opacity, never an unconditional jump to one.
    internal sealed class WindowFade
    {
        internal const double InDuration = 200, OutDuration = 220;
        private long started;
        private double from, duration;
        internal double Opacity { get; private set; }
        internal bool Running { get; private set; }
        internal bool FadingOut { get; private set; }
        internal void Reset(double value)
        { Opacity = Math.Max(0, Math.Min(1, value)); Running = FadingOut = false; }
        internal void BeginIn(double current, long now)
        {
            Reset(current); from = Opacity; started = now;
            duration = InDuration * (1 - from); Running = duration > 0;
        }
        internal void BeginOut(double current, long now)
        {
            Reset(current); from = Opacity; started = now;
            duration = OutDuration; FadingOut = Running = true;
        }
        internal void Advance(long now)
        {
            if (!Running) return;
            double t = Math.Max(0, Math.Min(1, (now - started) / duration));
            double eased = t * t * (3 - 2 * t);
            Opacity = FadingOut ? from * (1 - eased) : from + (1 - from) * eased;
            if (t >= 1) Running = false;
        }
    }

    // One title at a time: completely fade out before replacing the text.
    // Time is supplied by the caller so rapid updates need no extra timer.
    internal sealed class TitleTransition
    {
        internal const long OutDuration = 20, InDuration = 45;
        private enum Phase { Still, Out, In }
        private Phase phase;
        private long started;
        private double from;
        internal string Text { get; private set; }
        internal string Target { get; private set; }
        internal double Opacity { get; private set; }
        internal bool Running { get { return phase != Phase.Still; } }
        internal bool AwaitingResult { get; private set; }

        internal TitleTransition() { Reset(""); }

        internal void Reset(string text)
        {
            Text = Target = text ?? "";
            Opacity = from = 1;
            phase = Phase.Still;
            AwaitingResult = false;
        }

        // Start responding at the key event, while keeping the last verified
        // title. A slow source lets that title fade back in rather than leaving
        // the title blank or prematurely displaying a success message.
        internal void Prepare(long now)
        {
            Advance(now);
            AwaitingResult = true;
            if (phase == Phase.Out) return;
            from = Opacity;
            started = now;
            phase = Phase.Out;
        }

        internal void Request(string text, long now, bool animate)
        {
            text = text ?? "";
            if (!animate) { Reset(text); return; }
            Advance(now);
            AwaitingResult = false;
            if (text == Target) return;
            Target = text;
            // While fading out, replace the destination without restarting.
            if (phase == Phase.Out) return;
            from = Opacity;
            started = now;
            phase = Phase.Out;
        }

        internal void Advance(long now)
        {
            if (phase == Phase.Out)
            {
                double elapsed = Math.Max(0, now - started);
                if (elapsed < OutDuration)
                {
                    Opacity = from * (1 - Smooth(elapsed / OutDuration));
                    return;
                }
                Opacity = 0;
                Text = Target;
                started += OutDuration;
                phase = Phase.In;
            }
            if (phase == Phase.In)
            {
                double elapsed = Math.Max(0, now - started);
                Opacity = Smooth(elapsed / InDuration);
                if (elapsed >= InDuration) { Opacity = 1; phase = Phase.Still; }
            }
        }

        private static double Smooth(double t)
        { t = Math.Min(1, Math.Max(0, t)); return t * t * (3 - 2 * t); }
    }

    // Same bounded analytic curves as the approved interactive preview.
    internal sealed class CopyMotion
    {
        internal const double CardAmount = .06, CounterAmount = .18;
        internal const long Grow = 55, Return = 155, Duration = Grow + Return;
        internal const double Softness = 2.8;
        private long started;
        private double initial;
        internal bool Running { get; private set; }
        internal void Pulse(long now) { initial = Value(now); started = now; Running = true; }
        internal double Value(long now)
        {
            if (!Running) return 0;
            double elapsed = Math.Max(0, now - started);
            if (elapsed >= Duration) { Running = false; return 0; }
            if (elapsed < Grow) { double t = elapsed / Grow; return initial + (1 - initial) * (1 - Math.Pow(1 - t, 3)); }
            double u = Math.Min(1, (elapsed - Grow) / Return);
            return Math.Pow(1 - u, Softness) * (1 + Softness * u);
        }
        internal void Stop() { Running = false; initial = 0; }
    }
}
