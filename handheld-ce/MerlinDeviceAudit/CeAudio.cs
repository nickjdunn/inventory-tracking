using System;
using System.Runtime.InteropServices;

namespace MerlinAudit
{
    /// <summary>Short beeps on Windows CE via coredll.</summary>
    internal static class CeAudio
    {
        private const uint BeepOk = 0;
        private const uint BeepError = 0x00000010;
        private const uint BeepQuestion = 0x00000020;

        [DllImport("coredll.dll", SetLastError = true)]
        private static extern bool MessageBeep(uint type);

        public static void Click()
        {
            try { MessageBeep(BeepOk); } catch { }
        }

        public static void FoundTone()
        {
            try { MessageBeep(BeepQuestion); } catch { }
        }

        public static void LostTone()
        {
            try { MessageBeep(BeepError); } catch { }
        }
    }
}
