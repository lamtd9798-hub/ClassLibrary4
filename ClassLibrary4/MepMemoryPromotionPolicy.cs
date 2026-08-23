#nullable disable
using System;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29F.3 - chính sách promotion cho Memory.
    /// Tách threshold khỏi BOCTACHUI để các bước sau có thể tune/test độc lập.
    /// </summary>
    internal static class MepMemoryPromotionPolicy
    {
        public const int CompanyMinProjects = 2;
        public const int CompanyMinConfirmations = 4;
        public const double CompanyMinDominance = 0.82;
        public const double CompanyMinConfidence = 0.86;

        public const int GlobalMinCompanies = 2;
        public const int GlobalMinProjects = 4;
        public const int GlobalMinConfirmations = 12;
        public const double GlobalMinDominance = 0.88;
        public const double GlobalMinConfidence = 0.90;

        public const double StrongProjectConfidence = 0.86;
        public const double StrongSessionConfidence = 0.90;
        public const double CrossScopeConflictConfidence = 0.88;

        public const int CompanyPrototypeMinProjects = 2;
        public const int CompanyPrototypeMinConfirmations = 4;
        public const int GlobalPrototypeMinCompanies = 2;
        public const int GlobalPrototypeMinProjects = 4;
        public const int GlobalPrototypeMinConfirmations = 10;

        public static int ScopeRank(string scope)
        {
            string normalized = NormalizeScope(scope);

            if (normalized == "SESSION") return 4;
            if (normalized == "PROJECT") return 3;
            if (normalized == "COMPANY") return 2;
            if (normalized == "GLOBAL") return 1;
            return 0;
        }

        public static string NormalizeScope(string scope)
        {
            string value = (scope ?? "").Trim().ToUpperInvariant();

            if (value == "SESSION" ||
                value == "PROJECT" ||
                value == "COMPANY" ||
                value == "GLOBAL")
            {
                return value;
            }

            return "PROJECT";
        }

        public static bool IsUsableCompanyCode(string companyCode)
        {
            string value = (companyCode ?? "").Trim();

            return !string.IsNullOrWhiteSpace(value) &&
                   !string.Equals(value, "LOCAL", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(value, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
        }

        public static double RequiredPrototypeSimilarity(string scope)
        {
            string normalized = NormalizeScope(scope);

            if (normalized == "PROJECT") return 0.87;
            if (normalized == "SESSION") return 0.87;
            if (normalized == "COMPANY") return 0.91;
            if (normalized == "GLOBAL") return 0.93;
            return 0.93;
        }

        public static double RequiredPrototypeMargin(string scope)
        {
            string normalized = NormalizeScope(scope);

            if (normalized == "PROJECT" || normalized == "SESSION")
                return 0.035;

            if (normalized == "COMPANY")
                return 0.045;

            return 0.055;
        }
    }
}
