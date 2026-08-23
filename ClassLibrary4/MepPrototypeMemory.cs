#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29F.2 - persistent few-shot prototype memory.
    /// Mỗi prototype chỉ được ghi từ dữ liệu đã xác nhận.
    /// </summary>
    internal sealed class MepPrototypeRecord
    {
        public string PrototypeId { get; set; } = "";
        public string ProjectKey { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string Scope { get; set; } = "PROJECT";
        public string Label { get; set; } = "";
        public bool FollowDn { get; set; }
        public string LayerName { get; set; } = "";
        public string MatchMode { get; set; } = "";
        public string BlockKey { get; set; } = "";
        public string GeometryFingerprint { get; set; } = "";
        public float[] Descriptor { get; set; } = Array.Empty<float>();
        public int Confirmations { get; set; }
        public string LastSource { get; set; } = "";
        public string UpdatedUtc { get; set; } = "";
    }

    internal sealed class MepPrototypeMemoryStore
    {
        private readonly object _gate = new object();

        private readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

        private List<MepPrototypeRecord> _cache;
        private DateTime _cacheWriteUtc = DateTime.MinValue;

        public string BaseFolder { get; }
        public string MemoryPath { get; }

        public MepPrototypeMemoryStore()
        {
            string appData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(appData))
                appData = Path.GetTempPath();

            BaseFolder =
                Path.Combine(
                    appData,
                    "TDL_MEP",
                    "AI_PrototypeMemory");

            MemoryPath =
                Path.Combine(
                    BaseFolder,
                    "prototype_memory_v1.json");

            Directory.CreateDirectory(BaseFolder);
        }

        public List<MepPrototypeRecord> LoadForProject(
            string projectKey)
        {
            string project = NormalizeKey(projectKey);

            lock (_gate)
            {
                return LoadAllUnsafe()
                    .Where(x =>
                        x != null &&
                        string.Equals(
                            NormalizeKey(x.ProjectKey),
                            project,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(x =>
                    {
                        MepPrototypeRecord clone = Clone(x);
                        clone.Scope = "PROJECT";
                        return clone;
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// STEP29F.3 - prototype hierarchy.
        /// Project prototypes luôn được dùng. Prototype từ project khác chỉ được
        /// mở khóa khi cùng company/global đã có đủ breadth + confirmations.
        /// </summary>
        public List<MepPrototypeRecord> LoadForHierarchy(
            string projectKey,
            string companyCode)
        {
            string project = NormalizeKey(projectKey);
            string company = NormalizeKey(companyCode);

            lock (_gate)
            {
                List<MepPrototypeRecord> all =
                    LoadAllUnsafe()
                        .Where(x =>
                            x != null &&
                            x.Descriptor != null &&
                            x.Descriptor.Length > 0 &&
                            !string.IsNullOrWhiteSpace(x.Label))
                        .Select(Clone)
                        .ToList();

                List<MepPrototypeRecord> result =
                    new List<MepPrototypeRecord>();

                // PROJECT - ưu tiên tuyệt đối, không cần promotion.
                foreach (MepPrototypeRecord item in all.Where(x =>
                    string.Equals(
                        NormalizeKey(x.ProjectKey),
                        project,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    item.Scope = "PROJECT";
                    result.Add(item);
                }

                // COMPANY - chỉ mở prototype cross-project khi label đã được
                // xác nhận ở >=2 project trong cùng company.
                if (MepMemoryPromotionPolicy.IsUsableCompanyCode(company))
                {
                    IEnumerable<IGrouping<string, MepPrototypeRecord>> companyGroups =
                        all
                            .Where(x =>
                                string.Equals(
                                    NormalizeKey(x.CompanyCode),
                                    company,
                                    StringComparison.OrdinalIgnoreCase))
                            .GroupBy(
                                x =>
                                    NormalizeKey(x.Label) + "|" +
                                    (x.FollowDn ? "1" : "0"),
                                StringComparer.OrdinalIgnoreCase);

                    foreach (IGrouping<string, MepPrototypeRecord> group in companyGroups)
                    {
                        int projects =
                            group
                                .Select(x => NormalizeKey(x.ProjectKey))
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count();

                        int confirmations =
                            group.Sum(x => Math.Max(0, x.Confirmations));

                        if (projects <
                                MepMemoryPromotionPolicy.CompanyPrototypeMinProjects ||
                            confirmations <
                                MepMemoryPromotionPolicy.CompanyPrototypeMinConfirmations)
                        {
                            continue;
                        }

                        foreach (MepPrototypeRecord item in group
                            .Where(x =>
                                !string.Equals(
                                    NormalizeKey(x.ProjectKey),
                                    project,
                                    StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(x => Math.Max(0, x.Confirmations))
                            .ThenByDescending(x => ParseUtc(x.UpdatedUtc))
                            .Take(18))
                        {
                            item.Scope = "COMPANY";
                            result.Add(item);
                        }
                    }
                }

                // GLOBAL VERIFIED - yêu cầu xuất hiện ở nhiều company + project.
                IEnumerable<IGrouping<string, MepPrototypeRecord>> globalGroups =
                    all
                        .Where(x =>
                            MepMemoryPromotionPolicy.IsUsableCompanyCode(
                                NormalizeKey(x.CompanyCode)))
                        .GroupBy(
                            x =>
                                NormalizeKey(x.Label) + "|" +
                                (x.FollowDn ? "1" : "0"),
                            StringComparer.OrdinalIgnoreCase);

                foreach (IGrouping<string, MepPrototypeRecord> group in globalGroups)
                {
                    int companies =
                        group
                            .Select(x => NormalizeKey(x.CompanyCode))
                            .Where(MepMemoryPromotionPolicy.IsUsableCompanyCode)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                    int projects =
                        group
                            .Select(x => NormalizeKey(x.ProjectKey))
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                    int confirmations =
                        group.Sum(x => Math.Max(0, x.Confirmations));

                    if (companies <
                            MepMemoryPromotionPolicy.GlobalPrototypeMinCompanies ||
                        projects <
                            MepMemoryPromotionPolicy.GlobalPrototypeMinProjects ||
                        confirmations <
                            MepMemoryPromotionPolicy.GlobalPrototypeMinConfirmations)
                    {
                        continue;
                    }

                    foreach (MepPrototypeRecord item in group
                        .OrderByDescending(x => Math.Max(0, x.Confirmations))
                        .ThenByDescending(x => ParseUtc(x.UpdatedUtc))
                        .Take(16))
                    {
                        // Không duplicate prototype đã có ở PROJECT/COMPANY.
                        if (result.Any(x =>
                            string.Equals(
                                x.PrototypeId,
                                item.PrototypeId,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        item.Scope = "GLOBAL";
                        result.Add(item);
                    }
                }

                // Giới hạn lookup runtime. PROJECT giữ trước; sau đó COMPANY/GLOBAL.
                return result
                    .OrderByDescending(x =>
                        MepMemoryPromotionPolicy.ScopeRank(x.Scope))
                    .ThenByDescending(x => Math.Max(0, x.Confirmations))
                    .ThenByDescending(x => ParseUtc(x.UpdatedUtc))
                    .Take(1200)
                    .ToList();
            }
        }

        public void Learn(
            string projectKey,
            string companyCode,
            string label,
            bool followDn,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint,
            float[] descriptor,
            string source)
        {
            string project = NormalizeKey(projectKey);
            string normalizedLabel = (label ?? "").Trim();

            if (string.IsNullOrWhiteSpace(project) ||
                string.IsNullOrWhiteSpace(normalizedLabel) ||
                descriptor == null ||
                descriptor.Length == 0)
            {
                return;
            }

            float[] cleanDescriptor =
                NormalizeDescriptor(descriptor);

            if (cleanDescriptor.Length == 0)
                return;

            lock (_gate)
            {
                List<MepPrototypeRecord> all =
                    LoadAllUnsafe();

                string normalizedGeometry =
                    NormalizeKey(geometryFingerprint);

                string normalizedBlock =
                    NormalizeKey(blockKey);

                MepPrototypeRecord existing =
                    all
                        .Where(x =>
                            x != null &&
                            string.Equals(
                                NormalizeKey(x.ProjectKey),
                                project,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                (x.Label ?? "").Trim(),
                                normalizedLabel,
                                StringComparison.OrdinalIgnoreCase) &&
                            x.FollowDn == followDn &&
                            x.Descriptor != null &&
                            x.Descriptor.Length == cleanDescriptor.Length)
                        .OrderByDescending(x =>
                            MetadataAffinity(
                                x,
                                layerName,
                                matchMode,
                                blockKey,
                                geometryFingerprint))
                        .ThenByDescending(x =>
                            Cosine(
                                x.Descriptor,
                                cleanDescriptor))
                        .FirstOrDefault(x =>
                        {
                            double sim =
                                Cosine(
                                    x.Descriptor,
                                    cleanDescriptor);

                            bool exactMetadata =
                                (!string.IsNullOrWhiteSpace(normalizedGeometry) &&
                                 string.Equals(
                                     NormalizeKey(x.GeometryFingerprint),
                                     normalizedGeometry,
                                     StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(normalizedBlock) &&
                                 string.Equals(
                                     NormalizeKey(x.BlockKey),
                                     normalizedBlock,
                                     StringComparison.OrdinalIgnoreCase));

                            return exactMetadata || sim >= 0.985;
                        });

                if (existing == null)
                {
                    existing = new MepPrototypeRecord
                    {
                        PrototypeId = Guid.NewGuid().ToString("N"),
                        ProjectKey = project,
                        CompanyCode = NormalizeKey(companyCode),
                        Scope = "PROJECT",
                        Label = normalizedLabel,
                        FollowDn = followDn,
                        LayerName = NormalizeKey(layerName),
                        MatchMode = NormalizeKey(matchMode),
                        BlockKey = normalizedBlock,
                        GeometryFingerprint = normalizedGeometry,
                        Descriptor = cleanDescriptor,
                        Confirmations = 1,
                        LastSource = source ?? "",
                        UpdatedUtc = DateTime.UtcNow.ToString(
                            "O",
                            CultureInfo.InvariantCulture)
                    };

                    all.Add(existing);
                }
                else
                {
                    int oldCount =
                        Math.Max(
                            1,
                            existing.Confirmations);

                    int newCount =
                        Math.Min(
                            9999,
                            oldCount + 1);

                    float[] oldDescriptor =
                        NormalizeDescriptor(existing.Descriptor);

                    float[] merged =
                        new float[cleanDescriptor.Length];

                    for (int i = 0; i < merged.Length; i++)
                    {
                        double oldValue =
                            i < oldDescriptor.Length
                                ? oldDescriptor[i]
                                : 0.0;

                        merged[i] =
                            (float)(
                                (oldValue * oldCount + cleanDescriptor[i]) /
                                newCount);
                    }

                    existing.Descriptor =
                        NormalizeDescriptor(merged);
                    existing.Confirmations = newCount;
                    existing.CompanyCode = NormalizeKey(companyCode);
                    existing.Scope = "PROJECT";
                    existing.LayerName = NormalizeKey(layerName);
                    existing.MatchMode = NormalizeKey(matchMode);
                    existing.BlockKey = normalizedBlock;
                    existing.GeometryFingerprint = normalizedGeometry;
                    existing.LastSource = source ?? "";
                    existing.UpdatedUtc =
                        DateTime.UtcNow.ToString(
                            "O",
                            CultureInfo.InvariantCulture);
                }

                // Giữ tối đa 64 prototype / label / project để lookup vẫn rất nhẹ.
                List<MepPrototypeRecord> trimmed =
                    new List<MepPrototypeRecord>();

                foreach (IGrouping<string, MepPrototypeRecord> group in
                    all.GroupBy(
                        x =>
                            NormalizeKey(x?.ProjectKey) + "|" +
                            (x?.Label ?? "").Trim().ToUpperInvariant(),
                        StringComparer.OrdinalIgnoreCase))
                {
                    trimmed.AddRange(
                        group
                            .OrderByDescending(x => Math.Max(0, x?.Confirmations ?? 0))
                            .ThenByDescending(x => ParseUtc(x?.UpdatedUtc))
                            .Take(64));
                }

                if (trimmed.Count > 20000)
                {
                    trimmed = trimmed
                        .OrderByDescending(x => ParseUtc(x?.UpdatedUtc))
                        .Take(20000)
                        .ToList();
                }

                SaveAllUnsafe(trimmed);
            }
        }

        private List<MepPrototypeRecord> LoadAllUnsafe()
        {
            try
            {
                DateTime writeUtc =
                    File.Exists(MemoryPath)
                        ? File.GetLastWriteTimeUtc(MemoryPath)
                        : DateTime.MinValue;

                if (_cache != null &&
                    writeUtc == _cacheWriteUtc)
                {
                    return _cache;
                }

                if (!File.Exists(MemoryPath))
                {
                    _cache = new List<MepPrototypeRecord>();
                    _cacheWriteUtc = DateTime.MinValue;
                    return _cache;
                }

                _cache =
                    JsonSerializer.Deserialize<List<MepPrototypeRecord>>(
                        File.ReadAllText(MemoryPath),
                        _jsonOptions) ??
                    new List<MepPrototypeRecord>();

                _cacheWriteUtc = writeUtc;
                return _cache;
            }
            catch
            {
                _cache = new List<MepPrototypeRecord>();
                _cacheWriteUtc = DateTime.MinValue;
                return _cache;
            }
        }

        private void SaveAllUnsafe(
            List<MepPrototypeRecord> values)
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);
                string temp = MemoryPath + ".tmp";

                File.WriteAllText(
                    temp,
                    JsonSerializer.Serialize(
                        values ?? new List<MepPrototypeRecord>(),
                        _jsonOptions));

                if (File.Exists(MemoryPath))
                    File.Delete(MemoryPath);

                File.Move(temp, MemoryPath);

                _cache = values ?? new List<MepPrototypeRecord>();
                _cacheWriteUtc =
                    File.GetLastWriteTimeUtc(MemoryPath);
            }
            catch
            {
            }
        }

        private static MepPrototypeRecord Clone(
            MepPrototypeRecord value)
        {
            if (value == null)
                return null;

            return new MepPrototypeRecord
            {
                PrototypeId = value.PrototypeId ?? "",
                ProjectKey = value.ProjectKey ?? "",
                CompanyCode = value.CompanyCode ?? "",
                Scope = MepMemoryPromotionPolicy.NormalizeScope(value.Scope),
                Label = value.Label ?? "",
                FollowDn = value.FollowDn,
                LayerName = value.LayerName ?? "",
                MatchMode = value.MatchMode ?? "",
                BlockKey = value.BlockKey ?? "",
                GeometryFingerprint = value.GeometryFingerprint ?? "",
                Descriptor = value.Descriptor?.ToArray() ?? Array.Empty<float>(),
                Confirmations = value.Confirmations,
                LastSource = value.LastSource ?? "",
                UpdatedUtc = value.UpdatedUtc ?? ""
            };
        }

        private static float[] NormalizeDescriptor(
            float[] descriptor)
        {
            if (descriptor == null || descriptor.Length == 0)
                return Array.Empty<float>();

            double sumSquares = 0.0;

            for (int i = 0; i < descriptor.Length; i++)
            {
                double v =
                    float.IsNaN(descriptor[i]) ||
                    float.IsInfinity(descriptor[i])
                        ? 0.0
                        : descriptor[i];

                sumSquares += v * v;
            }

            double norm = Math.Sqrt(sumSquares);
            if (norm < 1e-8)
                return Array.Empty<float>();

            float[] result = new float[descriptor.Length];

            for (int i = 0; i < descriptor.Length; i++)
            {
                double v =
                    float.IsNaN(descriptor[i]) ||
                    float.IsInfinity(descriptor[i])
                        ? 0.0
                        : descriptor[i];

                result[i] = (float)(v / norm);
            }

            return result;
        }

        internal static double Cosine(
            float[] a,
            float[] b)
        {
            if (a == null || b == null ||
                a.Length == 0 ||
                a.Length != b.Length)
            {
                return 0.0;
            }

            double dot = 0.0;
            double aa = 0.0;
            double bb = 0.0;

            for (int i = 0; i < a.Length; i++)
            {
                double av = a[i];
                double bv = b[i];
                dot += av * bv;
                aa += av * av;
                bb += bv * bv;
            }

            if (aa < 1e-12 || bb < 1e-12)
                return 0.0;

            return Math.Max(
                0.0,
                Math.Min(
                    1.0,
                    dot / Math.Sqrt(aa * bb)));
        }

        private static double MetadataAffinity(
            MepPrototypeRecord record,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            if (record == null)
                return 0.0;

            double score = 0.0;

            if (!string.IsNullOrWhiteSpace(geometryFingerprint) &&
                string.Equals(
                    NormalizeKey(record.GeometryFingerprint),
                    NormalizeKey(geometryFingerprint),
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 1.0;
            }

            if (!string.IsNullOrWhiteSpace(blockKey) &&
                string.Equals(
                    NormalizeKey(record.BlockKey),
                    NormalizeKey(blockKey),
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.8;
            }

            if (!string.IsNullOrWhiteSpace(layerName) &&
                string.Equals(
                    NormalizeKey(record.LayerName),
                    NormalizeKey(layerName),
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.2;
            }

            if (!string.IsNullOrWhiteSpace(matchMode) &&
                string.Equals(
                    NormalizeKey(record.MatchMode),
                    NormalizeKey(matchMode),
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 0.1;
            }

            return score;
        }

        private static DateTime ParseUtc(string value)
        {
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.MinValue;
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? "")
                .Trim()
                .Replace('/', '\\')
                .ToUpperInvariant();
        }
    }
}