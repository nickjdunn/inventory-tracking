using System;
using System.Collections;

namespace MerlinAudit
{
    internal sealed class KnownAppEntry
    {
        public readonly string Pattern;
        public readonly string Name;
        public readonly string Role;

        public KnownAppEntry(string pattern, string name, string role)
        {
            Pattern = pattern;
            Name = name;
            Role = role;
        }
    }

    /// <summary>Reference catalog for Nordic ID Merlin software commonly found on the gun.</summary>
    internal static class KnownAppsCatalog
    {
        private static readonly KnownAppEntry[] Entries = new KnownAppEntry[]
        {
            new KnownAppEntry("nid rfid demo", "Nordic RFID Demo", "UHF inventory demo — verify trigger reads"),
            new KnownAppEntry("rfid demo", "Nordic RFID Demo", "UHF inventory demo"),
            new KnownAppEntry("nid rfid wedge", "NID RFID Wedge", "Posts RFID reads as keyboard input"),
            new KnownAppEntry("nid rfid reader", "NID RFID Reader", "UHF reader service / UI"),
            new KnownAppEntry("nid scanner", "NID Scanner", "Integrated scanner control"),
            new KnownAppEntry("nid wedge", "NID Wedge", "Keyboard wedge configuration"),
            new KnownAppEntry("wedge", "Wedge app", "Barcode/RFID wedge settings"),
            new KnownAppEntry("nid autostart", "NID Autostart", "Boot-time service launcher"),
            new KnownAppEntry("nid indicators", "NID Indicators", "LED / status indicators"),
            new KnownAppEntry("nid indcators", "NID Indicators", "LED / status indicators (typo variant)"),
            new KnownAppEntry("nid keypad", "NID Keypad", "Physical keypad mapping"),
            new KnownAppEntry("nid link watchdog", "NID Link Watchdog", "Connectivity watchdog"),
            new KnownAppEntry("nid menu", "NID Menu", "Device system menu shell"),
            new KnownAppEntry("nid touch screen", "NID Touch Screen", "Touch calibration / driver"),
            new KnownAppEntry("nid trigger", "NID Trigger Button Switch", "Gun trigger → RFID trigger routing"),
            new KnownAppEntry("merlininventory", "Merlin Inventory", "This project's inventory CAB"),
            new KnownAppEntry("merlinstream", "Merlin Stream Test", "Stream test CAB"),
            new KnownAppEntry("merlindeviceaudit", "Merlin Device Audit", "Diagnostics audit CAB"),
            new KnownAppEntry("nurapi", "NUR API", "Nordic UHF RFID SDK"),
        };

        public static ArrayList MatchInstalledFiles(ArrayList installedFiles)
        {
            var hits = new ArrayList();
            var seen = new Hashtable();

            for (int i = 0; i < installedFiles.Count; i++)
            {
                FileEntry fe = (FileEntry)installedFiles[i];
                string probe = (fe.Name + " " + fe.Path).ToLower();
                for (int e = 0; e < Entries.Length; e++)
                {
                    KnownAppEntry entry = Entries[e];
                    if (probe.IndexOf(entry.Pattern) < 0) continue;
                    string key = entry.Name.ToLower();
                    if (seen.Contains(key)) continue;
                    seen[key] = true;
                    hits.Add(new KnownAppHit(entry, fe));
                }
            }
            return hits;
        }
    }

    internal sealed class KnownAppHit
    {
        public readonly string Name;
        public readonly string Role;
        public readonly string Path;
        public readonly long SizeBytes;

        public KnownAppHit(KnownAppEntry entry, FileEntry file)
        {
            Name = entry.Name;
            Role = entry.Role;
            Path = file.Path;
            SizeBytes = file.SizeBytes;
        }
    }

    internal sealed class FileEntry
    {
        public string Path = "";
        public string Name = "";
        public string Ext = "";
        public long SizeBytes;
        public string ModifiedUtc = "";
        public string Kind = "";
    }
}
