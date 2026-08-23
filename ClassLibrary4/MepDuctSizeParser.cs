#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassLibrary4
{
    public sealed class MepDuctSizeInfo
    {
        public string Shape { get; set; } = "";
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double DiameterMm { get; set; }
        public string CanonicalSize { get; set; } = "";
        public string SystemCode { get; set; } = "";
        public string FireRating { get; set; } = "";
        public string RawText { get; set; } = "";
        public bool HasStrongDuctContext { get; set; }

        public bool IsRectangular =>
            string.Equals(
                Shape,
                "RECT",
                StringComparison.OrdinalIgnoreCase);

        public bool IsRound =>
            string.Equals(
                Shape,
                "ROUND",
                StringComparison.OrdinalIgnoreCase);

        public double MaxDimensionMm =>
            IsRound
                ? DiameterMm
                : Math.Max(WidthMm, HeightMm);

        public double PerimeterMeters
        {
            get
            {
                if (IsRectangular)
                    return 2.0 * (WidthMm + HeightMm) / 1000.0;

                if (IsRound)
                    return Math.PI * DiameterMm / 1000.0;

                return 0.0;
            }
        }
    }

    /// <summary>
    /// STEP30B-D1 - parser riêng cho ống gió.
    /// Không dùng DN parser của pipe để tránh lẫn ống nước/PCCC với duct.
    /// </summary>
    public static class MepDuctSizeParser
    {
        private static readonly Regex RectRegex =
            new Regex(
                @"(?ix)
                (?<![A-Z0-9])
                (?:W\s*)?
                (?<w>\d{2,4}(?:[.,]\d+)?)
                \s*
                (?:X|×|\*)
                \s*
                (?:H\s*)?
                (?<h>\d{2,4}(?:[.,]\d+)?)
                (?!\d)",
                RegexOptions.Compiled);

        private static readonly Regex RoundStrongRegex =
            new Regex(
                @"(?ix)
                (?<![A-Z0-9])
                (?:Ø|Φ|DIA(?:METER)?\s*[:=]?|DIA\s*|ROUND\s*[:=]?)
                \s*
                (?<d>\d{2,4}(?:[.,]\d+)?)
                (?!\d)",
                RegexOptions.Compiled);

        private static readonly Regex RoundWeakRegex =
            new Regex(
                @"(?ix)
                (?<![A-Z0-9])
                D\s*
                (?<d>\d{2,4}(?:[.,]\d+)?)
                (?!\d)",
                RegexOptions.Compiled);

        private static readonly Regex EiRegex =
            new Regex(
                @"(?ix)
                \bEI\s*[-_:]?\s*
                (?<ei>15|20|30|45|60|90|120|150|180|240)
                \b",
                RegexOptions.Compiled);

        public static bool TryParse(
            string raw,
            out MepDuctSizeInfo result)
        {
            return TryParse(
                raw,
                "",
                out result);
        }

        public static bool TryParse(
            string raw,
            string layer,
            out MepDuctSizeInfo result)
        {
            result =
                new MepDuctSizeInfo
                {
                    RawText = raw ?? ""
                };

            string text =
                NormalizeForSearch(raw);

            string layerText =
                NormalizeForSearch(layer);

            string combined =
                (text + " " + layerText)
                    .Trim();

            bool strongContext =
                HasDuctContext(combined);

            Match rect =
                RectRegex.Match(
                    raw ?? "");

            if (rect.Success &&
                TryReadNumber(
                    rect.Groups["w"].Value,
                    out double w) &&
                TryReadNumber(
                    rect.Groups["h"].Value,
                    out double h) &&
                IsReasonableDimension(w) &&
                IsReasonableDimension(h))
            {
                result.Shape = "RECT";
                result.WidthMm = w;
                result.HeightMm = h;
                result.CanonicalSize =
                    FormatMm(w) +
                    "x" +
                    FormatMm(h);
                result.SystemCode =
                    InferSystemCode(combined);
                result.FireRating =
                    ParseFireRating(combined);
                result.HasStrongDuctContext =
                    true;

                return true;
            }

            Match round =
                RoundStrongRegex.Match(
                    raw ?? "");

            if (round.Success &&
                TryReadNumber(
                    round.Groups["d"].Value,
                    out double d) &&
                IsReasonableDimension(d))
            {
                // "Ø60" có thể là pipe. Chỉ nhận là duct nếu text/layer có
                // dấu hiệu ACMV hoặc kích thước khá lớn.
                bool accept =
                    strongContext ||
                    d >= 180.0;

                if (!accept)
                    return false;

                result.Shape = "ROUND";
                result.DiameterMm = d;
                result.CanonicalSize =
                    "Ø" +
                    FormatMm(d);
                result.SystemCode =
                    InferSystemCode(combined);
                result.FireRating =
                    ParseFireRating(combined);
                result.HasStrongDuctContext =
                    strongContext;

                return true;
            }

            if (strongContext)
            {
                Match weak =
                    RoundWeakRegex.Match(
                        raw ?? "");

                if (weak.Success &&
                    TryReadNumber(
                        weak.Groups["d"].Value,
                        out double weakD) &&
                    IsReasonableDimension(weakD))
                {
                    result.Shape = "ROUND";
                    result.DiameterMm = weakD;
                    result.CanonicalSize =
                        "Ø" +
                        FormatMm(weakD);
                    result.SystemCode =
                        InferSystemCode(combined);
                    result.FireRating =
                        ParseFireRating(combined);
                    result.HasStrongDuctContext =
                        true;

                    return true;
                }
            }

            return false;
        }

        public static string ParseFireRating(
            string raw)
        {
            string text =
                NormalizeForSearch(raw);

            Match match =
                EiRegex.Match(text);

            if (!match.Success)
                return "";

            return
                "EI" +
                match.Groups["ei"].Value;
        }

        public static string InferSystemCode(
            string raw)
        {
            string s =
                NormalizeForSearch(raw);

            if (ContainsAny(
                    s,
                    "HUT KHOI",
                    "SMOKE EXHAUST",
                    "SMOKE",
                    "SEF",
                    "SKE",
                    "OG HUT KHOI"))
            {
                return "SMOKE";
            }

            if (ContainsAny(
                    s,
                    "GIO TUOI",
                    "FRESH AIR",
                    "FRESH",
                    "OA",
                    "FA",
                    "OG TUOI"))
            {
                return "FA";
            }

            if (ContainsAny(
                    s,
                    "GIO THAI",
                    "EXHAUST AIR",
                    "EXHAUST",
                    "OG THAI",
                    "EA"))
            {
                return "EA";
            }

            if (ContainsAny(
                    s,
                    "GIO HOI",
                    "RETURN AIR",
                    "RETURN",
                    "OG HOI",
                    "RA"))
            {
                return "RA";
            }

            if (ContainsAny(
                    s,
                    "GIO CAP",
                    "SUPPLY AIR",
                    "SUPPLY",
                    "OG CAP",
                    "SA"))
            {
                return "SA";
            }

            if (ContainsAny(
                    s,
                    "PRESSURIZATION",
                    "STAIR PRESS",
                    "TANG AP",
                    "PA"))
            {
                return "PA";
            }

            if (ContainsAny(
                    s,
                    "DUCT",
                    "ONG GIO",
                    "OG LANH",
                    "ACMV",
                    "HVAC"))
            {
                return "DUCT";
            }

            return "";
        }

        public static bool HasDuctContext(
            string raw)
        {
            string s =
                NormalizeForSearch(raw);

            return
                ContainsAny(
                    s,
                    "DUCT",
                    "ONG GIO",
                    "OG ",
                    "OG-",
                    "OG_",
                    "ACMV",
                    "HVAC",
                    "SUPPLY AIR",
                    "RETURN AIR",
                    "EXHAUST AIR",
                    "FRESH AIR",
                    "HUT KHOI",
                    "GIO CAP",
                    "GIO HOI",
                    "GIO THAI",
                    "GIO TUOI",
                    "SMOKE");
        }

        public static string NormalizeForSearch(
            string value)
        {
            string s =
                (value ?? "")
                    .Replace("\\P", " ")
                    .Replace("\\p", " ")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim()
                    .ToUpperInvariant();

            s =
                RemoveVietnameseDiacritics(s);

            s =
                Regex.Replace(
                    s,
                    @"[\{\}\[\]\(\),;]+",
                    " ");

            s =
                Regex.Replace(
                    s,
                    @"\s+",
                    " ")
                    .Trim();

            return s;
        }

        private static bool IsReasonableDimension(
            double value)
        {
            return
                value >= 50.0 &&
                value <= 5000.0;
        }

        private static bool TryReadNumber(
            string raw,
            out double value)
        {
            return
                double.TryParse(
                    (raw ?? "")
                        .Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private static string FormatMm(
            double value)
        {
            if (Math.Abs(
                    value -
                    Math.Round(value)) <
                0.001)
            {
                return
                    Math.Round(value)
                        .ToString(
                            "0",
                            CultureInfo.InvariantCulture);
            }

            return
                value.ToString(
                    "0.#",
                    CultureInfo.InvariantCulture);
        }

        private static bool ContainsAny(
            string source,
            params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                tokens == null)
            {
                return false;
            }

            foreach (string token in tokens)
            {
                string normalizedToken =
                    NormalizeForSearch(token);

                if (string.IsNullOrWhiteSpace(
                        normalizedToken))
                {
                    continue;
                }

                // Token rất ngắn như SA/RA/EA/FA cần word-boundary.
                if (normalizedToken.Length <= 3)
                {
                    if (Regex.IsMatch(
                            source,
                            @"(?<![A-Z0-9])" +
                            Regex.Escape(
                                normalizedToken) +
                            @"(?![A-Z0-9])"))
                    {
                        return true;
                    }
                }
                else if (source.Contains(
                    normalizedToken,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string RemoveVietnameseDiacritics(
            string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            string normalized =
                input.Normalize(
                    NormalizationForm.FormD);

            StringBuilder sb =
                new StringBuilder(
                    normalized.Length);

            foreach (char c in normalized)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(c);

                if (category !=
                    UnicodeCategory.NonSpacingMark)
                {
                    if (c == 'Đ')
                        sb.Append('D');
                    else if (c == 'đ')
                        sb.Append('d');
                    else
                        sb.Append(c);
                }
            }

            return
                sb
                    .ToString()
                    .Normalize(
                        NormalizationForm.FormC);
        }
    }
}
