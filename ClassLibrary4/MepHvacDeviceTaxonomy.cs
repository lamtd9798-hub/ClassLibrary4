#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClassLibrary4
{
    public sealed class MepHvacDeviceDefinition
    {
        public string CanonicalLabel { get; set; } = "";
        public string Group { get; set; } = "";
        public bool FollowDuctSize { get; set; }
        public string[] Aliases { get; set; } = Array.Empty<string>();
    }

    public sealed class MepHvacDeviceMatch
    {
        public bool Success { get; set; }
        public string CanonicalLabel { get; set; } = "";
        public string Group { get; set; } = "";
        public bool FollowDuctSize { get; set; }
        public double Confidence { get; set; }
        public string Evidence { get; set; } = "";
    }

    /// <summary>
    /// STEP30B-D2 - taxonomy HVAC dùng chung cho ONNX/YOLO/Block/Layer.
    /// Không phụ thuộc AutoCAD.
    /// </summary>
    public static class MepHvacDeviceTaxonomy
    {
        private static readonly List<MepHvacDeviceDefinition> Definitions =
            new List<MepHvacDeviceDefinition>
            {
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "VCD",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "VCD",
                        "VOLUME CONTROL DAMPER",
                        "VOLUME DAMPER",
                        "VAN DIEU CHINH LUU LUONG",
                        "VAN CHINH LUU LUONG",
                        "BALANCING DAMPER"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "OBD",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "OBD",
                        "OPPOSED BLADE DAMPER",
                        "VAN CANH DOI"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "FIRE DAMPER",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "FD",
                        "FIRE DAMPER",
                        "VAN CHAN LUA",
                        "VAN NGAN LUA"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "FIRE SMOKE DAMPER",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "FSD",
                        "FIRE SMOKE DAMPER",
                        "SMOKE FIRE DAMPER",
                        "VAN CHAN LUA NGAN KHOI",
                        "VAN NGAN LUA NGAN KHOI"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "MOTORIZED DAMPER",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "MD",
                        "MOTORIZED DAMPER",
                        "MOTOR DAMPER",
                        "VAN GIO CO DONG CO"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "BACKDRAFT DAMPER",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "NRD",
                        "BDD",
                        "BACKDRAFT DAMPER",
                        "NON RETURN DAMPER",
                        "BACK DRAFT DAMPER",
                        "VAN MOT CHIEU GIO",
                        "VAN NGAN GIÓ NGUOC"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "SMOKE DAMPER",
                    Group = "DAMPER",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "SD",
                        "SMOKE DAMPER",
                        "VAN NGAN KHOI"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "SUPPLY DIFFUSER",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "SUPPLY DIFFUSER",
                        "CEILING DIFFUSER",
                        "SQUARE DIFFUSER",
                        "DIFFUSER",
                        "MIENG GIO CAP",
                        "MIENG THOI",
                        "CD"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "RETURN GRILLE",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "RETURN GRILLE",
                        "RETURN AIR GRILLE",
                        "MIENG GIO HOI",
                        "GRILLE HOI",
                        "RAG"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "EXHAUST GRILLE",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "EXHAUST GRILLE",
                        "EXHAUST AIR GRILLE",
                        "MIENG GIO THAI",
                        "GRILLE THAI",
                        "EAG"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "SUPPLY GRILLE",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "SUPPLY GRILLE",
                        "SUPPLY AIR GRILLE",
                        "MIENG GIO CAP",
                        "SAG"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "LINEAR GRILLE",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "LINEAR GRILLE",
                        "LINEAR BAR GRILLE",
                        "MIENG GIO LINEAR",
                        "LG"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "SLOT DIFFUSER",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "SLOT DIFFUSER",
                        "LINEAR SLOT DIFFUSER",
                        "MIENG GIO KHE",
                        "LSD"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "ROUND DIFFUSER",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "ROUND DIFFUSER",
                        "MIENG GIO TRON"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "JET NOZZLE",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "JET NOZZLE",
                        "JET DIFFUSER",
                        "MIENG GIO JET"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "LOUVER",
                    Group = "AIR_TERMINAL",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "LOUVER",
                        "LOUVRE",
                        "CỬA CHỚP",
                        "CUA CHOP"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "VAV",
                    Group = "EQUIPMENT",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "VAV",
                        "VARIABLE AIR VOLUME",
                        "VAV BOX"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "CAV",
                    Group = "EQUIPMENT",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "CAV",
                        "CONSTANT AIR VOLUME",
                        "CAV BOX"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "SILENCER",
                    Group = "EQUIPMENT",
                    FollowDuctSize = true,
                    Aliases = new[]
                    {
                        "SILENCER",
                        "SOUND ATTENUATOR",
                        "TIEU AM",
                        "HOP TIEU AM"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "FCU",
                    Group = "EQUIPMENT",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "FCU",
                        "FAN COIL",
                        "FAN COIL UNIT"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "AHU",
                    Group = "EQUIPMENT",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "AHU",
                        "AIR HANDLING UNIT"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "PAU",
                    Group = "EQUIPMENT",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "PAU",
                        "PRIMARY AIR UNIT",
                        "PRECOOLED AIR UNIT"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "FAHU",
                    Group = "EQUIPMENT",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "FAHU",
                        "FRESH AIR HANDLING UNIT"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "EXHAUST FAN",
                    Group = "EQUIPMENT",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "EF",
                        "EXHAUST FAN",
                        "QUAT HUT"
                    }
                },
                new MepHvacDeviceDefinition
                {
                    CanonicalLabel = "SUPPLY FAN",
                    Group = "EQUIPMENT",
                    FollowDuctSize = false,
                    Aliases = new[]
                    {
                        "SF",
                        "SUPPLY FAN",
                        "QUAT CAP"
                    }
                }
            };

        public static IReadOnlyList<MepHvacDeviceDefinition> All =>
            Definitions;

        public static bool TryResolveLabel(
            string raw,
            out MepHvacDeviceMatch match)
        {
            return TryResolve(
                raw,
                "",
                "",
                "",
                out match);
        }

        public static bool TryResolve(
            string rawLabel,
            string blockName,
            string layer,
            string nearbyText,
            out MepHvacDeviceMatch match)
        {
            match =
                new MepHvacDeviceMatch();

            string label =
                Normalize(rawLabel);

            string block =
                Normalize(blockName);

            string layerKey =
                Normalize(layer);

            string text =
                Normalize(nearbyText);

            MepHvacDeviceDefinition best =
                null;

            double bestScore = 0.0;
            string bestEvidence = "";

            foreach (MepHvacDeviceDefinition def
                in Definitions)
            {
                if (def == null)
                    continue;

                double score = 0.0;
                string evidence = "";

                double labelScore =
                    BestAliasScore(
                        label,
                        def);

                if (labelScore > 0.0)
                {
                    score +=
                        labelScore * 0.62;

                    evidence +=
                        "label;";
                }

                double blockScore =
                    BestAliasScore(
                        block,
                        def);

                if (blockScore > 0.0)
                {
                    score +=
                        blockScore * 0.20;

                    evidence +=
                        "block;";
                }

                double layerScore =
                    BestAliasScore(
                        layerKey,
                        def);

                if (layerScore > 0.0)
                {
                    score +=
                        layerScore * 0.10;

                    evidence +=
                        "layer;";
                }

                double textScore =
                    BestAliasScore(
                        text,
                        def);

                if (textScore > 0.0)
                {
                    score +=
                        textScore * 0.18;

                    evidence +=
                        "text;";
                }

                // Exact AI label là bằng chứng mạnh nhất.
                if (!string.IsNullOrWhiteSpace(label) &&
                    IsExactAlias(
                        label,
                        def))
                {
                    score =
                        Math.Max(
                            score,
                            0.96);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = def;
                    bestEvidence = evidence;
                }
            }

            if (best == null ||
                bestScore < 0.58)
            {
                return false;
            }

            match.Success = true;
            match.CanonicalLabel =
                best.CanonicalLabel;
            match.Group =
                best.Group;
            match.FollowDuctSize =
                best.FollowDuctSize;
            match.Confidence =
                Math.Min(
                    0.995,
                    bestScore);
            match.Evidence =
                string.IsNullOrWhiteSpace(
                    bestEvidence)
                    ? "HVAC taxonomy"
                    : "HVAC taxonomy: " +
                      bestEvidence.TrimEnd(';');

            return true;
        }

        public static string CanonicalizeLabel(
            string raw)
        {
            if (TryResolveLabel(
                    raw,
                    out MepHvacDeviceMatch match))
            {
                return
                    match.CanonicalLabel;
            }

            return
                (raw ?? "").Trim();
        }

        private static double BestAliasScore(
            string source,
            MepHvacDeviceDefinition def)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                def == null)
            {
                return 0.0;
            }

            double best = 0.0;

            string canonical =
                Normalize(
                    def.CanonicalLabel);

            best =
                Math.Max(
                    best,
                    MatchAlias(
                        source,
                        canonical));

            foreach (string alias
                in def.Aliases ?? Array.Empty<string>())
            {
                best =
                    Math.Max(
                        best,
                        MatchAlias(
                            source,
                            Normalize(alias)));
            }

            return best;
        }

        private static bool IsExactAlias(
            string source,
            MepHvacDeviceDefinition def)
        {
            if (def == null)
                return false;

            string canonical =
                Normalize(
                    def.CanonicalLabel);

            if (string.Equals(
                    source,
                    canonical,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return
                (def.Aliases ??
                 Array.Empty<string>())
                    .Select(Normalize)
                    .Any(a =>
                        string.Equals(
                            source,
                            a,
                            StringComparison.OrdinalIgnoreCase));
        }

        private static double MatchAlias(
            string source,
            string alias)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(alias))
            {
                return 0.0;
            }

            if (string.Equals(
                    source,
                    alias,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            // Từ viết tắt ngắn phải là token riêng,
            // tránh FD match nhầm trong một từ dài.
            if (alias.Length <= 4 &&
                !alias.Contains(' '))
            {
                bool token =
                    Regex.IsMatch(
                        source,
                        @"(?<![A-Z0-9])" +
                        Regex.Escape(alias) +
                        @"(?![A-Z0-9])",
                        RegexOptions.IgnoreCase);

                return
                    token
                        ? 0.92
                        : 0.0;
            }

            if (source.Contains(
                    alias,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0.88;
            }

            return 0.0;
        }

        private static string Normalize(
            string value)
        {
            return
                MepDuctSizeParser
                    .NormalizeForSearch(
                        value ?? "");
        }
    }
}
