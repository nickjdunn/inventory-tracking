namespace MerlinHandheld
{
    /// <summary>Git-based version baked in at build (see scripts/sync-version.js).</summary>
    public static class AppVersionInfo
    {
        public static string GitHashFromVersion(string version)
        {
            if (version == null) return "";
            int plus = version.IndexOf('+');
            if (plus < 0 || plus >= version.Length - 1) return "";
            return version.Substring(plus + 1).Trim();
        }

        public static string FormatInstalledLabel()
        {
            return "Git " + AppConfig.AppVersion;
        }
    }
}
