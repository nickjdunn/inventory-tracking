using System;
using System.Collections;
using System.IO;
using System.Text;

namespace MerlinHandheld
{
    /// <summary>Helpers for APIs missing or different in .NET Compact Framework 3.5.</summary>
    internal static class CfCompat
    {
        public static bool TryParseInt(string text, out int value)
        {
            value = 0;
            if (text == null) return false;
            text = text.Trim();
            if (text.Length == 0) return false;

            int start = 0;
            bool negative = false;
            if (text[0] == '-')
            {
                negative = true;
                start = 1;
                if (text.Length == 1) return false;
            }

            long total = 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') return false;
                total = total * 10 + (c - '0');
                if (total > int.MaxValue) return false;
            }

            if (negative)
            {
                if (total > (long)int.MaxValue + 1) return false;
                value = (int)(-total);
            }
            else
            {
                if (total > int.MaxValue) return false;
                value = (int)total;
            }
            return true;
        }

        public static string[] ReadAllLines(string path)
        {
            var lines = new ArrayList();
            using (var reader = new StreamReader(path, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }
            return (string[])lines.ToArray(typeof(string));
        }

        public static void WriteAllText(string path, string contents)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.Write(contents);
            }
        }
    }
}
