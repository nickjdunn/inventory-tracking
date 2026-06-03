using System;
using System.Globalization;
using System.Text;

namespace MerlinAudit
{
    /// <summary>Physical tag-stack layout recorded before an RF bench run.</summary>
    public sealed class RfBenchStackSetup
    {
        public const string ScenarioTagStack = "tag_stack";

        public decimal DistanceInches = 12m;
        public int TagCount = 100;
        public decimal SpacingInches = 0.02m;
        public string DistanceText = "12";
        public string TagCountText = "100";
        public string SpacingText = "0.02";

        public string SummaryLine()
        {
            return DistanceText + " in · " + TagCountText + " tags · "
                + SpacingText + " in apart";
        }

        public void LoadFromConfig(AuditConfig cfg)
        {
            if (cfg == null) return;
            DistanceInches = cfg.BenchStackDistanceIn;
            TagCount = cfg.BenchStackTagCount;
            SpacingInches = cfg.BenchStackSpacingIn;
            DistanceText = FormatDecimal(DistanceInches);
            TagCountText = TagCount.ToString();
            SpacingText = FormatDecimal(SpacingInches);
        }

        public void SaveToConfig(AuditConfig cfg)
        {
            if (cfg == null) return;
            cfg.BenchStackDistanceIn = DistanceInches;
            cfg.BenchStackTagCount = TagCount;
            cfg.BenchStackSpacingIn = SpacingInches;
            cfg.Save();
        }

        public static bool TryParse(
            string distanceIn,
            string tagCountIn,
            string spacingIn,
            out RfBenchStackSetup setup,
            out string error)
        {
            setup = new RfBenchStackSetup();
            error = "";

            if (!TryParseDecimal(distanceIn, out setup.DistanceInches))
            {
                error = "Distance (inches) invalid";
                return false;
            }
            if (setup.DistanceInches < 0 || setup.DistanceInches > 9999.99m)
            {
                error = "Distance out of range";
                return false;
            }

            int tags;
            if (!TryParsePositiveInt(tagCountIn, out tags) || tags < 1 || tags > 100000)
            {
                error = "Tag count invalid (1-100000)";
                return false;
            }
            setup.TagCount = tags;

            if (!TryParseDecimal(spacingIn, out setup.SpacingInches))
            {
                error = "Spacing (inches) invalid";
                return false;
            }
            if (setup.SpacingInches < 0 || setup.SpacingInches > 99.99m)
            {
                error = "Spacing out of range";
                return false;
            }

            setup.DistanceText = NormalizeDecimalText(distanceIn, setup.DistanceInches);
            setup.TagCountText = tags.ToString();
            setup.SpacingText = NormalizeDecimalText(spacingIn, setup.SpacingInches);
            return true;
        }

        private static bool TryParsePositiveInt(string text, out int value)
        {
            value = 0;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) return false;
            }
            try
            {
                value = int.Parse(text, CultureInfo.InvariantCulture);
                return value > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseDecimal(string text, out decimal value)
        {
            value = 0m;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;
            text = text.Replace(',', '.');
            int dots = 0;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.')
                {
                    dots++;
                    if (dots > 1) return false;
                    sb.Append(c);
                }
                else if (char.IsDigit(c))
                {
                    sb.Append(c);
                }
                else
                {
                    return false;
                }
            }
            string norm = sb.ToString();
            if (norm.Length == 0) return false;
            int dot = norm.IndexOf('.');
            if (dot >= 0 && norm.Length - dot - 1 > 2) return false;
            try
            {
                value = decimal.Parse(norm, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeDecimalText(string raw, decimal parsed)
        {
            if (raw != null && raw.Trim().Length > 0) return raw.Trim().Replace(',', '.');
            return FormatDecimal(parsed);
        }

        public static string FormatDecimal(decimal value)
        {
            decimal rounded = decimal.Round(value, 2);
            string s = rounded.ToString(CultureInfo.InvariantCulture);
            if (s.IndexOf('.') >= 0)
            {
                s = s.TrimEnd('0');
                if (s.EndsWith(".")) s = s.Substring(0, s.Length - 1);
            }
            return s;
        }

        public void AppendJson(StringBuilder sb)
        {
            sb.Append("\"stack_setup\":{");
            sb.Append("\"scenario\":\"").Append(ScenarioTagStack).Append('"');
            sb.Append(",\"distance_inches\":").Append(FormatDecimal(DistanceInches));
            sb.Append(",\"tag_count\":").Append(TagCount);
            sb.Append(",\"spacing_inches\":").Append(FormatDecimal(SpacingInches));
            sb.Append(",\"distance_inches_text\":\"").Append(SimpleJson.Escape(DistanceText)).Append('"');
            sb.Append(",\"tag_count_text\":\"").Append(SimpleJson.Escape(TagCountText)).Append('"');
            sb.Append(",\"spacing_inches_text\":\"").Append(SimpleJson.Escape(SpacingText)).Append('"');
            sb.Append(",\"summary\":\"").Append(SimpleJson.Escape(SummaryLine())).Append('"');
            sb.Append('}');
        }
    }
}
