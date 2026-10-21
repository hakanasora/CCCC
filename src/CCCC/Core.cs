using System;
using System.Globalization;
using System.Text;

namespace CCCC
{
    internal enum ContentKind { Text, Files, Image, Other }
    internal enum FeedbackKind { Confirmed, Retained, Unconfirmed, Updated, Status }

    internal sealed class ClipboardSnapshot
    {
        internal uint Sequence;
        internal uint OwnerProcess;
        internal ContentKind Kind;
        internal bool Supported;
        internal string Preview = "";
        internal string TypeLabel = "";
    }

    internal sealed class Feedback
    {
        internal FeedbackKind Kind;
        internal string Title = "";
        internal string Preview = "";
        internal string TypeLabel = "";
        internal int Count;
        internal IntPtr Foreground;
    }

    // Pure state machine. Time and clipboard observations are supplied by the host.
    internal sealed class FeedbackEngine
    {
        internal const long BurstMilliseconds = 3000;
        internal const long ConfirmMilliseconds = 1500;
        private long lastPress = -10000;
        private long burstStart;
        private IntPtr burstWindow;
        private int count;
        private Attempt pending;
        private ClipboardSnapshot confirmed;
        private uint lastObserved;

        private sealed class Attempt
        {
            internal long Start;
            internal uint Baseline;
            internal uint Process;
            internal IntPtr Window;
            internal int Count;
        }

        internal bool HasPending { get { return pending != null; } }
        internal int Count { get { return count; } }

        internal void Reset(uint sequence)
        {
            pending = null;
            confirmed = null;
            lastObserved = sequence;
            count = 0;
            lastPress = -10000;
            burstWindow = IntPtr.Zero;
        }

        internal void CopyPressed(long now, IntPtr window, uint process, uint baseline)
        {
            if (window != burstWindow || now - lastPress > BurstMilliseconds || count == 0)
            {
                count = 0;
                burstStart = now;
                confirmed = null;
            }
            else if (confirmed != null && confirmed.Sequence != baseline)
            {
                confirmed = null;
            }
            if (count < Int32.MaxValue) count++;
            burstWindow = window;
            lastPress = now;
            pending = new Attempt { Start = now, Baseline = baseline, Process = process,
                Window = window, Count = count };
        }

        internal Feedback Observe(ClipboardSnapshot snapshot, long eventTime, long now, IntPtr foreground)
        {
            if (snapshot == null || snapshot.Sequence == lastObserved) return null;
            lastObserved = snapshot.Sequence;

            if (pending != null)
            {
                // A read can complete after the next key press. Remember a verified
                // earlier result without claiming that the newest key press worked.
                if (eventTime < pending.Start)
                {
                    if (snapshot.Supported && eventTime >= burstStart &&
                        snapshot.Sequence == pending.Baseline &&
                        snapshot.OwnerProcess != 0 && snapshot.OwnerProcess == pending.Process)
                        confirmed = snapshot;
                    return null;
                }
                if (now - pending.Start >= ConfirmMilliseconds)
                    return Expire(now, snapshot.Sequence);
                if (snapshot.Sequence == pending.Baseline) return null;

                Attempt attempt = pending;
                pending = null;
                bool matchesSource = snapshot.OwnerProcess != 0 &&
                    snapshot.OwnerProcess == attempt.Process;
                if (snapshot.Supported && matchesSource)
                {
                    confirmed = snapshot;
                    return FromSnapshot(snapshot, FeedbackKind.Confirmed,
                        SuccessTitle(attempt.Count), attempt.Count, attempt.Window);
                }
                confirmed = null;
                return FromSnapshot(snapshot, FeedbackKind.Updated, "剪贴板已更新", 0, foreground);
            }

            // No Ctrl+C: e.g. a context-menu copy, screenshot, or another app.
            count = 0;
            confirmed = null;
            lastPress = -10000;
            return FromSnapshot(snapshot, snapshot.Supported ? FeedbackKind.Confirmed : FeedbackKind.Updated,
                snapshot.Supported ? "已复制" : "剪贴板已更新", 0, foreground);
        }

        internal Feedback Expire(long now, uint currentSequence)
        {
            if (pending == null)
            {
                if (now - lastPress > BurstMilliseconds) { confirmed = null; count = 0; }
                return null;
            }
            if (now - pending.Start < ConfirmMilliseconds) return null;
            Attempt attempt = pending;
            pending = null;
            if (confirmed != null && confirmed.Sequence == currentSequence &&
                confirmed.Sequence == attempt.Baseline)
                return FromSnapshot(confirmed, FeedbackKind.Retained, "刚才的内容仍在剪贴板",
                    attempt.Count, attempt.Window);
            confirmed = null;
            return new Feedback { Kind = FeedbackKind.Unconfirmed,
                Title = "未检测到新的复制结果", Preview = "可重新选择内容后再试一次",
                TypeLabel = "本次复制尚未确认", Count = attempt.Count, Foreground = attempt.Window };
        }

        private static Feedback FromSnapshot(ClipboardSnapshot value, FeedbackKind kind,
            string title, int count, IntPtr foreground)
        {
            return new Feedback { Kind = kind, Title = title, Preview = value.Preview,
                TypeLabel = value.TypeLabel, Count = count, Foreground = foreground };
        }

        internal static string SuccessTitle(int times)
        {
            switch (times)
            {
                case 1: return "已复制";
                case 2: return "还在呢";
                case 3: return "真的复制好了";
                case 4: return "千真万确复制好了";
                default: return times >= 5 ? "去粘贴吧" : "已复制";
            }
        }
    }

    internal sealed class CopyKeyDetector
    {
        private bool cDown;
        internal bool Feed(int key, bool down, bool control, bool altOrShiftOrWin)
        {
            if (key != 0x43) return false;
            if (!down) { cDown = false; return false; }
            if (cDown) return false;
            cDown = true;
            return control && !altOrShiftOrWin;
        }
        internal void Reset() { cDown = false; }
    }

    internal static class PreviewText
    {
        // Text elements keep emoji and combining marks intact at the 36-character cut.
        internal static string Shorten(string text)
        {
            if (String.IsNullOrEmpty(text)) return "空白文字";
            StringBuilder collapsed = new StringBuilder();
            bool space = false;
            foreach (char c in text)
            {
                if (Char.IsWhiteSpace(c) || Char.IsControl(c)) { space = collapsed.Length > 0; continue; }
                // Remove bidi and other formatting controls from untrusted clipboard text.
                if (Char.GetUnicodeCategory(c) == UnicodeCategory.Format && c != '\u200d') continue;
                if (space) collapsed.Append(' ');
                collapsed.Append(c);
                space = false;
            }
            if (collapsed.Length == 0) return "空白文字";
            string normalized = collapsed.ToString();
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(normalized);
            int count = 0;
            while (elements.MoveNext())
                if (++count > 36) return normalized.Substring(0, elements.ElementIndex) + "…";
            return normalized;
        }
    }
}
