#nullable disable
using System;
using System.Collections.Generic;

namespace ClassLibrary4
{
    /// <summary>
    /// Maps model-specific labels to a stable MEP vocabulary. Physical label files remain
    /// model-specific because their output index ordering is part of each ONNX contract.
    /// </summary>
    internal static class MepCanonicalLabelMap
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SMOKE_DETECTOR"] = "BÁO KHÓI",
                ["DETECTOR_SMOKE"] = "BÁO KHÓI",
                ["BAO KHOI"] = "BÁO KHÓI",
                ["HEAT_DETECTOR"] = "BÁO NHIỆT",
                ["DETECTOR_HEAT"] = "BÁO NHIỆT",
                ["BAO NHIET"] = "BÁO NHIỆT",
                ["SPRINKLER"] = "SPRINKLER",
                ["SPRINKLER_HEAD"] = "SPRINKLER",
                ["BUTTERFLY_VALVE"] = "VAN BƯỚM",
                ["VAN BUOM"] = "VAN BƯỚM",
                ["CHECK_VALVE"] = "VAN 1 CHIỀU",
                ["NON_RETURN_VALVE"] = "VAN 1 CHIỀU",
                ["VCD"] = "VCD",
                ["VOLUME_CONTROL_DAMPER"] = "VCD",
                ["FD"] = "FD",
                ["FIRE_DAMPER"] = "FD",
                ["FSD"] = "FSD",
                ["FIRE_SMOKE_DAMPER"] = "FSD",
                ["SUPPLY_GRILLE"] = "MIỆNG GIÓ CẤP",
                ["RETURN_GRILLE"] = "MIỆNG GIÓ HỒI",
                ["EXHAUST_GRILLE"] = "MIỆNG GIÓ THẢI",
                ["DIFFUSER"] = "MIỆNG GIÓ"
            };

        public static string Canonicalize(string engineName, string rawLabel)
        {
            string raw = (rawLabel ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            string key = raw
                .ToUpperInvariant()
                .Replace('-', '_')
                .Replace(' ', '_');

            if (Map.TryGetValue(key, out string canonical))
                return canonical;

            // HVAC taxonomy already carries canonical labels. Do not force it into
            // an ONNX/YOLO physical label order.
            return raw.Trim().ToUpperInvariant();
        }
    }
}
