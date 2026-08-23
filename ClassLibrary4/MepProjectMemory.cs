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
    /// STEP29F.1 - PROJECT MEMORY.
    ///
    /// Local-first prior theo từng DWG/project. Chỉ ghi từ dữ liệu đã được
    /// user/Legend xác nhận. Không nhận prediction AI làm teacher.
    ///
    /// STEP29F.3 bổ sung SESSION -> PROJECT -> COMPANY -> GLOBAL VERIFIED và conflict guard.
    /// </summary>
    internal sealed class MepProjectMemoryEvidence
    {
        public string Label { get; set; } = "";
        public bool FollowDn { get; set; }
        public double Confidence { get; set; }
        public int SupportCount { get; set; }
        public int SignalCount { get; set; }
        public string Scope { get; set; } = "PROJECT";
        public string Reason { get; set; } = "";
        public bool Conflict { get; set; }
        public string ConflictLabel { get; set; } = "";
        public double ConflictConfidence { get; set; }

        public bool Success =>
            !Conflict &&
            !string.IsNullOrWhiteSpace(Label) &&
            Confidence > 0.0;
    }

    internal sealed class MepProjectMemoryEntry
    {
        public string ProjectKey { get; set; } = "";
        public string CompanyCode { get; set; } = "";
        public string SignalType { get; set; } = "";
        public string SignalValue { get; set; } = "";
        public string Label { get; set; } = "";
        public bool FollowDn { get; set; }
        public int Confirmations { get; set; }
        public string LastSource { get; set; } = "";
        public string UpdatedUtc { get; set; } = "";
    }

    internal sealed class MepProjectMemorySnapshot
    {
        public string ProjectKey { get; set; } = "";
        public List<MepProjectMemoryEntry> Entries { get; set; } =
            new List<MepProjectMemoryEntry>();
    }

    internal sealed class MepProjectMemoryStore
    {
        private readonly object _gate = new object();

        private readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

        private List<MepProjectMemoryEntry> _cache;
        private DateTime _cacheWriteUtc = DateTime.MinValue;

        // STEP29F.3 - SESSION memory chỉ sống trong RAM của phiên plugin.
        // Vẫn ghi PROJECT persistent song song để lần mở CAD sau học lại được.
        private readonly List<MepProjectMemoryEntry> _sessionEntries =
            new List<MepProjectMemoryEntry>();

        public string BaseFolder { get; }
        public string MemoryPath { get; }

        public MepProjectMemoryStore()
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
                    "AI_ProjectMemory");

            MemoryPath =
                Path.Combine(
                    BaseFolder,
                    "project_memory_v1.json");

            Directory.CreateDirectory(BaseFolder);
        }

        public MepProjectMemorySnapshot LoadSnapshot(
            string projectKey)
        {
            string project = NormalizeKey(projectKey);

            List<MepProjectMemoryEntry> all =
                LoadAll();

            return new MepProjectMemorySnapshot
            {
                ProjectKey = project,
                Entries = all
                    .Where(x =>
                        x != null &&
                        string.Equals(
                            NormalizeKey(x.ProjectKey),
                            project,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(Clone)
                    .ToList()
            };
        }

        public void RecordConfirmation(
            string projectKey,
            string companyCode,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint,
            string label,
            bool followDn,
            string source)
        {
            string project = NormalizeKey(projectKey);
            string company = NormalizeKey(companyCode);
            string normalizedLabel = NormalizeLabel(label);

            if (string.IsNullOrWhiteSpace(project) ||
                string.IsNullOrWhiteSpace(normalizedLabel))
            {
                return;
            }

            List<Tuple<string, string>> signals =
                BuildSignals(
                    layerName,
                    matchMode,
                    blockKey,
                    geometryFingerprint);

            if (signals.Count == 0)
                return;

            lock (_gate)
            {
                List<MepProjectMemoryEntry> all =
                    LoadAllUnsafe();

                foreach (Tuple<string, string> signal in signals)
                {
                    string type = signal.Item1;
                    string value = signal.Item2;

                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    UpsertConfirmation(
                        _sessionEntries,
                        project,
                        company,
                        type,
                        value,
                        normalizedLabel,
                        followDn,
                        source);

                    UpsertConfirmation(
                        all,
                        project,
                        company,
                        type,
                        value,
                        normalizedLabel,
                        followDn,
                        source);
                }

                // SESSION chỉ cần history ngắn để tránh phình RAM khi mở CAD lâu.
                if (_sessionEntries.Count > 8000)
                {
                    List<MepProjectMemoryEntry> keep =
                        _sessionEntries
                            .OrderByDescending(x => ParseUtc(x?.UpdatedUtc))
                            .Take(8000)
                            .Select(Clone)
                            .ToList();

                    _sessionEntries.Clear();
                    _sessionEntries.AddRange(keep);
                }

                // Giới hạn file local để tránh phình vô hạn.
                if (all.Count > 30000)
                {
                    all = all
                        .OrderByDescending(x => ParseUtc(x?.UpdatedUtc))
                        .Take(30000)
                        .ToList();
                }

                SaveAllUnsafe(all);
            }
        }

        public MepProjectMemoryEvidence Evaluate(
            MepProjectMemorySnapshot snapshot,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            MepProjectMemoryEvidence result =
                new MepProjectMemoryEvidence();

            if (snapshot?.Entries == null ||
                snapshot.Entries.Count == 0)
            {
                return result;
            }

            List<Tuple<string, string>> signals =
                BuildSignals(
                    layerName,
                    matchMode,
                    blockKey,
                    geometryFingerprint);

            if (signals.Count == 0)
                return result;

            Dictionary<string, VoteBucket> votes =
                new Dictionary<string, VoteBucket>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (Tuple<string, string> signal in signals)
            {
                string type = signal.Item1;
                string value = signal.Item2;
                double reliability = SignalReliability(type);

                foreach (MepProjectMemoryEntry entry in snapshot.Entries)
                {
                    if (entry == null ||
                        !string.Equals(
                            NormalizeKey(entry.SignalType),
                            type,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            NormalizeKey(entry.SignalValue),
                            value,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int support =
                        Math.Max(0, entry.Confirmations);

                    if (!SignalHasEnoughSupport(type, support))
                        continue;

                    string label = NormalizeLabel(entry.Label);
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    string bucketKey =
                        label + "|" +
                        (entry.FollowDn ? "1" : "0");

                    if (!votes.TryGetValue(
                            bucketKey,
                            out VoteBucket bucket))
                    {
                        bucket = new VoteBucket
                        {
                            Label = label,
                            FollowDn = entry.FollowDn
                        };

                        votes[bucketKey] = bucket;
                    }

                    double supportStrength =
                        Math.Min(
                            1.0,
                            0.70 +
                            Math.Max(0, support - 1) * 0.08);

                    bucket.Vote +=
                        reliability * supportStrength;

                    bucket.BestReliability =
                        Math.Max(
                            bucket.BestReliability,
                            reliability);

                    bucket.SupportCount =
                        Math.Max(
                            bucket.SupportCount,
                            support);

                    bucket.SignalTypes.Add(type);
                }
            }

            if (votes.Count == 0)
                return result;

            List<VoteBucket> ordered =
                votes.Values
                    .OrderByDescending(x => x.Vote)
                    .ThenByDescending(x => x.BestReliability)
                    .ThenByDescending(x => x.SupportCount)
                    .ToList();

            VoteBucket best = ordered[0];
            double totalVote =
                Math.Max(
                    1e-6,
                    ordered.Sum(x => Math.Max(0.0, x.Vote)));

            double voteShare =
                Clamp01(best.Vote / totalVote);

            double supportBonus =
                Math.Min(
                    0.035,
                    Math.Max(0, best.SupportCount - 1) * 0.007);

            double confidence =
                Clamp01(
                    best.BestReliability * 0.55 +
                    voteShare * 0.45 +
                    supportBonus);

            // Nếu hai label gần hòa, không dùng project prior để lái Fusion.
            if (ordered.Count > 1)
            {
                double margin =
                    best.Vote - ordered[1].Vote;

                if (margin < 0.12)
                {
                    confidence *= 0.72;
                }
            }

            result.Label = best.Label;
            result.FollowDn = best.FollowDn;
            result.Confidence = confidence;
            result.SupportCount = best.SupportCount;
            result.SignalCount = best.SignalTypes.Count;
            result.Scope = "PROJECT";
            result.Reason =
                "PROJECT MEMORY: " +
                string.Join(
                    "+",
                    best.SignalTypes.OrderBy(x => x)) +
                " | support=" +
                best.SupportCount.ToString(
                    CultureInfo.InvariantCulture) +
                " | conf=" +
                confidence.ToString(
                    "0.000",
                    CultureInfo.InvariantCulture);

            return result;
        }


        /// <summary>
        /// STEP29F.3 - evaluate theo hierarchy:
        /// SESSION -> PROJECT -> COMPANY -> GLOBAL VERIFIED.
        ///
        /// PROJECT/SESSION luôn ưu tiên hơn scope rộng hơn để style riêng của
        /// dự án hiện tại không bị Company/Global ghi đè.
        /// </summary>
        public MepProjectMemoryEvidence EvaluateHierarchy(
            string projectKey,
            string companyCode,
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            string project = NormalizeKey(projectKey);
            string company = NormalizeKey(companyCode);

            List<Tuple<string, string>> signals =
                BuildSignals(
                    layerName,
                    matchMode,
                    blockKey,
                    geometryFingerprint);

            if (signals.Count == 0)
                return new MepProjectMemoryEvidence();

            List<MepProjectMemoryEntry> all;
            List<MepProjectMemoryEntry> session;

            lock (_gate)
            {
                all = LoadAllUnsafe()
                    .Select(Clone)
                    .ToList();

                session = _sessionEntries
                    .Where(x =>
                        x != null &&
                        string.Equals(
                            NormalizeKey(x.ProjectKey),
                            project,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(Clone)
                    .ToList();
            }

            MepProjectMemoryEvidence sessionEvidence =
                EvaluateEntries(
                    session,
                    signals,
                    "SESSION",
                    1,
                    1,
                    0,
                    0.64,
                    true);

            MepProjectMemoryEvidence projectEvidence =
                Evaluate(
                    new MepProjectMemorySnapshot
                    {
                        ProjectKey = project,
                        Entries = all
                            .Where(x =>
                                x != null &&
                                string.Equals(
                                    NormalizeKey(x.ProjectKey),
                                    project,
                                    StringComparison.OrdinalIgnoreCase))
                            .Select(Clone)
                            .ToList()
                    },
                    layerName,
                    matchMode,
                    blockKey,
                    geometryFingerprint);

            MepProjectMemoryEvidence companyEvidence =
                new MepProjectMemoryEvidence();

            if (MepMemoryPromotionPolicy.IsUsableCompanyCode(company))
            {
                List<MepProjectMemoryEntry> companyEntries =
                    all
                        .Where(x =>
                            x != null &&
                            string.Equals(
                                NormalizeKey(x.CompanyCode),
                                company,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(Clone)
                        .ToList();

                companyEvidence =
                    EvaluateEntries(
                        companyEntries,
                        signals,
                        "COMPANY",
                        MepMemoryPromotionPolicy.CompanyMinProjects,
                        MepMemoryPromotionPolicy.CompanyMinConfirmations,
                        0,
                        MepMemoryPromotionPolicy.CompanyMinDominance,
                        false);
            }

            List<MepProjectMemoryEntry> globalEntries =
                all
                    .Where(x =>
                        x != null &&
                        MepMemoryPromotionPolicy.IsUsableCompanyCode(
                            NormalizeKey(x.CompanyCode)))
                    .Select(Clone)
                    .ToList();

            MepProjectMemoryEvidence globalEvidence =
                EvaluateEntries(
                    globalEntries,
                    signals,
                    "GLOBAL",
                    MepMemoryPromotionPolicy.GlobalMinProjects,
                    MepMemoryPromotionPolicy.GlobalMinConfirmations,
                    MepMemoryPromotionPolicy.GlobalMinCompanies,
                    MepMemoryPromotionPolicy.GlobalMinDominance,
                    false);

            // Scope gần nhất thắng. SESSION chỉ được dùng mạnh khi recent evidence
            // đủ rõ; nếu không PROJECT persistent vẫn là chuẩn local.
            MepProjectMemoryEvidence selected = null;

            if (sessionEvidence.Success &&
                sessionEvidence.Confidence >=
                    MepMemoryPromotionPolicy.StrongSessionConfidence)
            {
                selected = sessionEvidence;
            }
            else if (projectEvidence.Success &&
                     projectEvidence.Confidence >=
                        MepMemoryPromotionPolicy.StrongProjectConfidence)
            {
                selected = projectEvidence;
            }
            else if (companyEvidence.Success &&
                     companyEvidence.Confidence >=
                        MepMemoryPromotionPolicy.CompanyMinConfidence)
            {
                selected = companyEvidence;
            }
            else if (globalEvidence.Success &&
                     globalEvidence.Confidence >=
                        MepMemoryPromotionPolicy.GlobalMinConfidence)
            {
                selected = globalEvidence;
            }
            else
            {
                selected =
                    new[]
                    {
                        sessionEvidence,
                        projectEvidence,
                        companyEvidence,
                        globalEvidence
                    }
                    .Where(x => x != null && x.Success)
                    .OrderByDescending(x =>
                        MepMemoryPromotionPolicy.ScopeRank(x.Scope))
                    .ThenByDescending(x => x.Confidence)
                    .FirstOrDefault();
            }

            if (selected == null)
            {
                // Nếu scope rộng tự mâu thuẫn thì expose conflict để UI log,
                // nhưng không cho memory lái Fusion.
                MepProjectMemoryEvidence conflict =
                    new[]
                    {
                        sessionEvidence,
                        projectEvidence,
                        companyEvidence,
                        globalEvidence
                    }
                    .FirstOrDefault(x => x != null && x.Conflict);

                return conflict ?? new MepProjectMemoryEvidence();
            }

            // Project-specific evidence được phép khác Company/Global: đó có thể
            // chính là style riêng dự án. Chỉ broad scopes mâu thuẫn nhau mới
            // bị chặn khi không có local evidence đủ mạnh.
            bool localSelected =
                string.Equals(
                    MepMemoryPromotionPolicy.NormalizeScope(selected.Scope),
                    "SESSION",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    MepMemoryPromotionPolicy.NormalizeScope(selected.Scope),
                    "PROJECT",
                    StringComparison.OrdinalIgnoreCase);

            if (!localSelected)
            {
                MepProjectMemoryEvidence otherBroad =
                    string.Equals(
                        MepMemoryPromotionPolicy.NormalizeScope(selected.Scope),
                        "COMPANY",
                        StringComparison.OrdinalIgnoreCase)
                        ? globalEvidence
                        : companyEvidence;

                if (otherBroad != null &&
                    otherBroad.Success &&
                    otherBroad.Confidence >=
                        MepMemoryPromotionPolicy.CrossScopeConflictConfidence &&
                    selected.Confidence >=
                        MepMemoryPromotionPolicy.CrossScopeConflictConfidence &&
                    !SameDecision(selected, otherBroad))
                {
                    return new MepProjectMemoryEvidence
                    {
                        Conflict = true,
                        Scope = "CONFLICT",
                        Label = selected.Label,
                        Confidence = selected.Confidence,
                        ConflictLabel = otherBroad.Label,
                        ConflictConfidence = otherBroad.Confidence,
                        SupportCount = Math.Max(
                            selected.SupportCount,
                            otherBroad.SupportCount),
                        Reason =
                            "MEMORY CONFLICT COMPANY/GLOBAL: " +
                            selected.Label +
                            " vs " +
                            otherBroad.Label
                    };
                }
            }

            return selected;
        }

        private static MepProjectMemoryEvidence EvaluateEntries(
            List<MepProjectMemoryEntry> entries,
            List<Tuple<string, string>> signals,
            string scope,
            int minProjects,
            int minConfirmations,
            int minCompanies,
            double minDominance,
            bool sessionMode)
        {
            MepProjectMemoryEvidence result =
                new MepProjectMemoryEvidence
                {
                    Scope = scope
                };

            if (entries == null ||
                entries.Count == 0 ||
                signals == null ||
                signals.Count == 0)
            {
                return result;
            }

            Dictionary<string, AggregateBucket> buckets =
                new Dictionary<string, AggregateBucket>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (Tuple<string, string> signal in signals)
            {
                string type = NormalizeKey(signal?.Item1);
                string value = NormalizeKey(signal?.Item2);

                if (string.IsNullOrWhiteSpace(type) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                double reliability = SignalReliability(type);

                foreach (MepProjectMemoryEntry entry in entries)
                {
                    if (entry == null ||
                        !string.Equals(
                            NormalizeKey(entry.SignalType),
                            type,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            NormalizeKey(entry.SignalValue),
                            value,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int support = Math.Max(0, entry.Confirmations);

                    if (!sessionMode &&
                        !SignalHasEnoughSupport(type, support))
                    {
                        continue;
                    }

                    if (sessionMode &&
                        string.Equals(type, "LAYER", StringComparison.OrdinalIgnoreCase) &&
                        support < 2)
                    {
                        continue;
                    }

                    string label = NormalizeLabel(entry.Label);
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    string key =
                        label + "|" +
                        (entry.FollowDn ? "1" : "0");

                    if (!buckets.TryGetValue(
                            key,
                            out AggregateBucket bucket))
                    {
                        bucket = new AggregateBucket
                        {
                            Label = label,
                            FollowDn = entry.FollowDn
                        };

                        buckets[key] = bucket;
                    }

                    string projectKey = NormalizeKey(entry.ProjectKey);
                    string companyCode = NormalizeKey(entry.CompanyCode);

                    double supportStrength =
                        Math.Min(
                            1.0,
                            0.72 +
                            Math.Max(0, support - 1) * 0.07);

                    double vote =
                        reliability * supportStrength;

                    string projectSignalKey =
                        projectKey + "|" + type;

                    if (!bucket.ProjectSignalVotes.TryGetValue(
                            projectSignalKey,
                            out double oldVote) ||
                        vote > oldVote)
                    {
                        bucket.ProjectSignalVotes[projectSignalKey] = vote;
                    }

                    bucket.Confirmations += support;
                    bucket.BestReliability =
                        Math.Max(bucket.BestReliability, reliability);
                    bucket.SignalTypes.Add(type);

                    if (!string.IsNullOrWhiteSpace(projectKey))
                        bucket.Projects.Add(projectKey);

                    if (MepMemoryPromotionPolicy.IsUsableCompanyCode(companyCode))
                        bucket.Companies.Add(companyCode);
                }
            }

            foreach (AggregateBucket bucket in buckets.Values)
            {
                bucket.Vote =
                    bucket.ProjectSignalVotes.Values.Sum();
            }

            List<AggregateBucket> ordered =
                buckets.Values
                    .Where(x =>
                        x.Projects.Count >= Math.Max(1, minProjects) &&
                        x.Confirmations >= Math.Max(1, minConfirmations) &&
                        x.Companies.Count >= Math.Max(0, minCompanies))
                    .OrderByDescending(x => x.Vote)
                    .ThenByDescending(x => x.Projects.Count)
                    .ThenByDescending(x => x.Confirmations)
                    .ToList();

            if (ordered.Count == 0)
                return result;

            AggregateBucket best = ordered[0];
            double total =
                Math.Max(
                    1e-6,
                    ordered.Sum(x => Math.Max(0.0, x.Vote)));

            double dominance =
                Clamp01(best.Vote / total);

            double runnerDominance =
                ordered.Count > 1
                    ? Clamp01(ordered[1].Vote / total)
                    : 0.0;

            if (dominance < minDominance)
            {
                if (ordered.Count > 1 &&
                    dominance >= 0.45 &&
                    runnerDominance >= 0.25)
                {
                    result.Conflict = true;
                    result.Label = best.Label;
                    result.ConflictLabel = ordered[1].Label;
                    result.Confidence = dominance;
                    result.ConflictConfidence = runnerDominance;
                    result.Reason =
                        scope +
                        " MEMORY CONFLICT: " +
                        best.Label +
                        " vs " +
                        ordered[1].Label;
                }

                return result;
            }

            double breadth =
                Math.Min(
                    1.0,
                    0.65 +
                    Math.Max(0, best.Projects.Count - 1) * 0.05 +
                    Math.Max(0, best.Companies.Count - 1) * 0.05);

            double confidence =
                Clamp01(
                    best.BestReliability * 0.45 +
                    dominance * 0.40 +
                    breadth * 0.15);

            if (string.Equals(
                    scope,
                    "GLOBAL",
                    StringComparison.OrdinalIgnoreCase))
            {
                confidence =
                    Math.Min(0.965, confidence);
            }
            else if (string.Equals(
                         scope,
                         "COMPANY",
                         StringComparison.OrdinalIgnoreCase))
            {
                confidence =
                    Math.Min(0.975, confidence);
            }
            else if (string.Equals(
                         scope,
                         "SESSION",
                         StringComparison.OrdinalIgnoreCase))
            {
                confidence =
                    Math.Min(0.995, confidence + 0.015);
            }

            result.Label = best.Label;
            result.FollowDn = best.FollowDn;
            result.Confidence = confidence;
            result.SupportCount = best.Confirmations;
            result.SignalCount = best.SignalTypes.Count;
            result.Scope = scope;
            result.Reason =
                scope +
                " MEMORY VERIFIED: " +
                string.Join(
                    "+",
                    best.SignalTypes.OrderBy(x => x)) +
                " | projects=" +
                best.Projects.Count.ToString(
                    CultureInfo.InvariantCulture) +
                " | companies=" +
                best.Companies.Count.ToString(
                    CultureInfo.InvariantCulture) +
                " | support=" +
                best.Confirmations.ToString(
                    CultureInfo.InvariantCulture) +
                " | dominance=" +
                dominance.ToString(
                    "0.000",
                    CultureInfo.InvariantCulture) +
                " | conf=" +
                confidence.ToString(
                    "0.000",
                    CultureInfo.InvariantCulture);

            return result;
        }

        private static bool SameDecision(
            MepProjectMemoryEvidence a,
            MepProjectMemoryEvidence b)
        {
            if (a == null || b == null)
                return false;

            return
                string.Equals(
                    NormalizeLabel(a.Label),
                    NormalizeLabel(b.Label),
                    StringComparison.OrdinalIgnoreCase) &&
                a.FollowDn == b.FollowDn;
        }

        private static void UpsertConfirmation(
            List<MepProjectMemoryEntry> values,
            string project,
            string company,
            string signalType,
            string signalValue,
            string label,
            bool followDn,
            string source)
        {
            if (values == null)
                return;

            MepProjectMemoryEntry entry =
                values.FirstOrDefault(x =>
                    x != null &&
                    string.Equals(
                        NormalizeKey(x.ProjectKey),
                        project,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        NormalizeKey(x.SignalType),
                        signalType,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        NormalizeKey(x.SignalValue),
                        signalValue,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        NormalizeLabel(x.Label),
                        label,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.FollowDn == followDn);

            if (entry == null)
            {
                entry = new MepProjectMemoryEntry
                {
                    ProjectKey = project,
                    CompanyCode = company,
                    SignalType = signalType,
                    SignalValue = signalValue,
                    Label = label,
                    FollowDn = followDn,
                    Confirmations = 0
                };

                values.Add(entry);
            }

            entry.Confirmations =
                Math.Min(
                    9999,
                    Math.Max(0, entry.Confirmations) + 1);

            entry.CompanyCode = company;
            entry.LastSource = source ?? "";
            entry.UpdatedUtc =
                DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture);
        }

        private List<MepProjectMemoryEntry> LoadAll()
        {
            lock (_gate)
            {
                return LoadAllUnsafe()
                    .Select(Clone)
                    .ToList();
            }
        }

        private List<MepProjectMemoryEntry> LoadAllUnsafe()
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
                    _cache = new List<MepProjectMemoryEntry>();
                    _cacheWriteUtc = DateTime.MinValue;
                    return _cache;
                }

                _cache =
                    JsonSerializer.Deserialize<List<MepProjectMemoryEntry>>(
                        File.ReadAllText(MemoryPath),
                        _jsonOptions) ??
                    new List<MepProjectMemoryEntry>();

                _cacheWriteUtc = writeUtc;
                return _cache;
            }
            catch
            {
                _cache = new List<MepProjectMemoryEntry>();
                _cacheWriteUtc = DateTime.MinValue;
                return _cache;
            }
        }

        private void SaveAllUnsafe(
            List<MepProjectMemoryEntry> all)
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);

                string temp =
                    MemoryPath + ".tmp";

                File.WriteAllText(
                    temp,
                    JsonSerializer.Serialize(
                        all ?? new List<MepProjectMemoryEntry>(),
                        _jsonOptions));

                if (File.Exists(MemoryPath))
                    File.Delete(MemoryPath);

                File.Move(temp, MemoryPath);

                _cache = all ?? new List<MepProjectMemoryEntry>();
                _cacheWriteUtc =
                    File.GetLastWriteTimeUtc(MemoryPath);
            }
            catch
            {
                // Memory không được làm hỏng workflow CAD chính.
            }
        }

        private static List<Tuple<string, string>> BuildSignals(
            string layerName,
            string matchMode,
            string blockKey,
            string geometryFingerprint)
        {
            string layer = NormalizeKey(layerName);
            string mode = NormalizeKey(matchMode);
            string block = NormalizeKey(blockKey);
            string geometry = NormalizeKey(geometryFingerprint);

            List<Tuple<string, string>> result =
                new List<Tuple<string, string>>();

            if (!string.IsNullOrWhiteSpace(geometry))
            {
                result.Add(
                    Tuple.Create(
                        "GEOMETRY",
                        geometry));
            }

            if (!string.IsNullOrWhiteSpace(block))
            {
                result.Add(
                    Tuple.Create(
                        "BLOCK",
                        block));
            }

            if (!string.IsNullOrWhiteSpace(layer) &&
                !string.IsNullOrWhiteSpace(mode))
            {
                result.Add(
                    Tuple.Create(
                        "LAYER_MODE",
                        layer + "|" + mode));
            }

            if (!string.IsNullOrWhiteSpace(layer))
            {
                result.Add(
                    Tuple.Create(
                        "LAYER",
                        layer));
            }

            return result;
        }

        private static bool SignalHasEnoughSupport(
            string type,
            int support)
        {
            if (string.Equals(type, "LAYER", StringComparison.OrdinalIgnoreCase))
                return support >= 3;

            if (string.Equals(type, "LAYER_MODE", StringComparison.OrdinalIgnoreCase))
                return support >= 2;

            return support >= 1;
        }

        private static double SignalReliability(
            string type)
        {
            if (string.Equals(type, "GEOMETRY", StringComparison.OrdinalIgnoreCase))
                return 1.00;

            if (string.Equals(type, "BLOCK", StringComparison.OrdinalIgnoreCase))
                return 0.96;

            if (string.Equals(type, "LAYER_MODE", StringComparison.OrdinalIgnoreCase))
                return 0.74;

            if (string.Equals(type, "LAYER", StringComparison.OrdinalIgnoreCase))
                return 0.60;

            return 0.40;
        }

        private static MepProjectMemoryEntry Clone(
            MepProjectMemoryEntry value)
        {
            if (value == null)
                return null;

            return new MepProjectMemoryEntry
            {
                ProjectKey = value.ProjectKey ?? "",
                CompanyCode = value.CompanyCode ?? "",
                SignalType = value.SignalType ?? "",
                SignalValue = value.SignalValue ?? "",
                Label = value.Label ?? "",
                FollowDn = value.FollowDn,
                Confirmations = value.Confirmations,
                LastSource = value.LastSource ?? "",
                UpdatedUtc = value.UpdatedUtc ?? ""
            };
        }

        private static DateTime ParseUtc(
            string value)
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

        private static string NormalizeLabel(
            string value)
        {
            return (value ?? "").Trim();
        }

        private static string NormalizeKey(
            string value)
        {
            return (value ?? "")
                .Trim()
                .Replace('/', '\\')
                .ToUpperInvariant();
        }

        private static double Clamp01(
            double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                return 0.0;
            }

            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private sealed class AggregateBucket
        {
            public string Label { get; set; } = "";
            public bool FollowDn { get; set; }
            public double Vote { get; set; }
            public double BestReliability { get; set; }
            public int Confirmations { get; set; }

            public HashSet<string> Projects { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> Companies { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> SignalTypes { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, double> ProjectSignalVotes { get; } =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class VoteBucket
        {
            public string Label { get; set; } = "";
            public bool FollowDn { get; set; }
            public double Vote { get; set; }
            public double BestReliability { get; set; }
            public int SupportCount { get; set; }
            public HashSet<string> SignalTypes { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}