#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace ClassLibrary4
{
    internal enum LicenseRequestState
    {
        Success,
        Rejected,
        NetworkError
    }

    internal sealed class LicenseApiResult
    {
        public LicenseRequestState State { get; set; }
        public string Message { get; set; } = "";
        public string Plan { get; set; } = "";
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public DateTimeOffset ServerTimeUtc { get; set; }
    }

    internal sealed class LicenseCacheData
    {
        public string LicenseKey { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string Plan { get; set; } = "";
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public DateTimeOffset LastOnlineCheckUtc { get; set; }
        public DateTimeOffset LastServerTimeUtc { get; set; }
    }

    internal sealed class LicenseClientConfig
    {
        public string SupabaseProjectUrl { get; set; } = "";
        public string SupabasePublishableKey { get; set; } = "";
        public string LicenseFunctionName { get; set; } = "license";
        public int HttpTimeoutSeconds { get; set; } = 12;

        public void Normalize()
        {
            SupabaseProjectUrl =
                (SupabaseProjectUrl ?? "").Trim().TrimEnd('/');

            SupabasePublishableKey =
                (SupabasePublishableKey ?? "").Trim();

            LicenseFunctionName =
                string.IsNullOrWhiteSpace(LicenseFunctionName)
                    ? "license"
                    : LicenseFunctionName.Trim().Trim('/');

            if (HttpTimeoutSeconds < 3)
                HttpTimeoutSeconds = 3;
            else if (HttpTimeoutSeconds > 60)
                HttpTimeoutSeconds = 60;
        }

        public bool IsConfigured =>
            Uri.TryCreate(
                SupabaseProjectUrl,
                UriKind.Absolute,
                out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeHttp) &&
            !string.IsNullOrWhiteSpace(SupabasePublishableKey) &&
            !string.IsNullOrWhiteSpace(LicenseFunctionName);
    }

    public static class OnlineLicenseManager
    {
        private const string LicenseConfigFileName =
            "license_client_config.json";

        private const int OfflineGraceDays = 3;

        // Giữ nguyên version/salt để cache license cũ vẫn đọc được sau khi nâng cấp.
        private const string CacheSignatureSalt =
            "TDL-MEP-LICENSE-CACHE-20260811-V1";

        private static readonly HttpClient Http =
            new HttpClient
            {
                // Timeout quản lý theo từng request bằng CancellationTokenSource.
                Timeout = Timeout.InfiniteTimeSpan
            };

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

        // Chỉ kiểm tra online một lần trong mỗi phiên AutoCAD.
        private static bool _sessionActivated;

        public static bool EnsureActivated()
        {
            if (_sessionActivated)
                return true;

            string machineId = GetMachineId();
            LicenseCacheData? cache = LoadCache(machineId);
            string initialKey = cache?.LicenseKey ?? "";
            string initialMessage = "";

            if (cache != null &&
                !string.IsNullOrWhiteSpace(cache.LicenseKey))
            {
                LicenseApiResult validation = RequestLicense(
                    "validate",
                    cache.LicenseKey,
                    machineId);

                if (validation.State == LicenseRequestState.Success)
                {
                    SaveCache(
                        cache.LicenseKey,
                        machineId,
                        validation);

                    _sessionActivated = true;
                    return true;
                }

                if (validation.State == LicenseRequestState.NetworkError &&
                    IsOfflineGraceValid(cache))
                {
                    _sessionActivated = true;
                    return true;
                }

                initialMessage = validation.Message;

                if (validation.State == LicenseRequestState.Rejected)
                    DeleteCache();
            }

            LicenseActivationWindow activationWindow =
                new LicenseActivationWindow(
                    machineId,
                    initialKey,
                    initialMessage);

            bool? result = activationWindow.ShowDialog();
            bool activated =
                result == true && activationWindow.IsActivated;

            if (activated)
                _sessionActivated = true;

            return activated;
        }

        internal static LicenseApiResult Activate(
            string licenseKey,
            string machineId)
        {
            string normalizedKey = NormalizeDisplayKey(licenseKey);

            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return new LicenseApiResult
                {
                    State = LicenseRequestState.Rejected,
                    Message = "Vui lòng nhập key kích hoạt."
                };
            }

            LicenseApiResult result = RequestLicense(
                "activate",
                normalizedKey,
                machineId);

            if (result.State == LicenseRequestState.Success)
            {
                SaveCache(
                    normalizedKey,
                    machineId,
                    result);
            }

            return result;
        }

        internal static string GetMachineId()
        {
            string machineGuid = "";

            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry64);

                using RegistryKey? key = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography");

                machineGuid = Convert.ToString(
                    key?.GetValue("MachineGuid"),
                    CultureInfo.InvariantCulture) ?? "";
            }
            catch
            {
                try
                {
                    using RegistryKey? key =
                        Registry.LocalMachine.OpenSubKey(
                            @"SOFTWARE\Microsoft\Cryptography");

                    machineGuid = Convert.ToString(
                        key?.GetValue("MachineGuid"),
                        CultureInfo.InvariantCulture) ?? "";
                }
                catch
                {
                    machineGuid = "";
                }
            }

            string fingerprint =
                machineGuid.Trim() + "|" +
                Environment.MachineName.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(machineGuid))
            {
                fingerprint += "|" +
                    Environment.OSVersion.VersionString;
            }

            return Sha256Hex(fingerprint).ToUpperInvariant();
        }

        internal static string GetShortMachineCode(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return "UNKNOWN";

            string value = machineId.Trim().ToUpperInvariant();

            if (value.Length < 16)
                return value;

            return value.Substring(0, 8) + "-" +
                value.Substring(8, 8);
        }

        internal static string FormatExpiry(DateTimeOffset? expiresAtUtc)
        {
            if (!expiresAtUtc.HasValue)
                return "Vĩnh viễn";

            return expiresAtUtc.Value
                .ToLocalTime()
                .ToString(
                    "dd/MM/yyyy HH:mm",
                    CultureInfo.InvariantCulture);
        }

        private static LicenseApiResult RequestLicense(
            string action,
            string licenseKey,
            string machineId)
        {
            LicenseClientConfig config = LoadClientConfig();

            if (!config.IsConfigured)
            {
                return new LicenseApiResult
                {
                    State = LicenseRequestState.NetworkError,
                    Message =
                        "Thiếu cấu hình license_client_config.json. " +
                        "Hãy kiểm tra file này nằm cùng thư mục DLL của plugin."
                };
            }

            string endpoint =
                config.SupabaseProjectUrl +
                "/functions/v1/" +
                config.LicenseFunctionName;

            string appVersion = GetCurrentAppVersion();

            string json = JsonSerializer.Serialize(
                new
                {
                    action,
                    license_key = NormalizeDisplayKey(licenseKey),
                    machine_id = machineId,
                    app_version = appVersion
                },
                JsonOptions);

            using HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    endpoint);

            request.Headers.TryAddWithoutValidation(
                "apikey",
                config.SupabasePublishableKey);

            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/json");

            // Legacy anon key là JWT (eyJ...). Publishable key mới
            // sb_publishable_* KHÔNG gửi làm Bearer.
            if (config.SupabasePublishableKey.StartsWith(
                    "eyJ",
                    StringComparison.Ordinal))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        config.SupabasePublishableKey);
            }

            request.Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

            using CancellationTokenSource cts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(
                        config.HttpTimeoutSeconds));

            try
            {
                using HttpResponseMessage response =
                    Http.SendAsync(
                            request,
                            HttpCompletionOption.ResponseContentRead,
                            cts.Token)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();

                string responseBody =
                    response.Content
                        .ReadAsStringAsync(cts.Token)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();

                if (response.IsSuccessStatusCode)
                    return ParseLicenseResponse(responseBody);

                int statusCode = (int)response.StatusCode;

                // 429/5xx là lỗi tạm thời của mạng/server -> giữ quyền offline grace.
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    statusCode >= 500)
                {
                    return new LicenseApiResult
                    {
                        State = LicenseRequestState.NetworkError,
                        Message =
                            "Máy chủ kích hoạt đang tạm thời không sẵn sàng " +
                            "(" + statusCode.ToString(
                                CultureInfo.InvariantCulture) + ")."
                    };
                }

                LicenseApiResult rejected =
                    ParseLicenseResponse(responseBody);

                rejected.State = LicenseRequestState.Rejected;

                if (string.IsNullOrWhiteSpace(rejected.Message))
                {
                    rejected.Message =
                        "Key không hợp lệ hoặc yêu cầu bị từ chối " +
                        "(" + statusCode.ToString(
                            CultureInfo.InvariantCulture) + ").";
                }

                return rejected;
            }
            catch (OperationCanceledException)
            {
                return new LicenseApiResult
                {
                    State = LicenseRequestState.NetworkError,
                    Message =
                        "Kết nối máy chủ kích hoạt quá thời gian. " +
                        "Vui lòng kiểm tra Internet rồi thử lại."
                };
            }
            catch (HttpRequestException)
            {
                return new LicenseApiResult
                {
                    State = LicenseRequestState.NetworkError,
                    Message =
                        "Không kết nối được máy chủ kích hoạt. " +
                        "Vui lòng kiểm tra Internet rồi thử lại."
                };
            }
            catch
            {
                return new LicenseApiResult
                {
                    State = LicenseRequestState.NetworkError,
                    Message =
                        "Không kết nối được máy chủ kích hoạt. " +
                        "Vui lòng kiểm tra Internet rồi thử lại."
                };
            }
        }

        private static string GetCurrentAppVersion()
        {
            try
            {
                Version? version =
                    Assembly.GetExecutingAssembly()
                        .GetName()
                        .Version;

                return version?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static LicenseClientConfig LoadClientConfig()
        {
            LicenseClientConfig config = new LicenseClientConfig();

            try
            {
                string? assemblyPath =
                    Assembly.GetExecutingAssembly().Location;

                string baseFolder =
                    !string.IsNullOrWhiteSpace(assemblyPath)
                        ? Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory
                        : AppContext.BaseDirectory;

                string configPath =
                    Path.Combine(
                        baseFolder,
                        LicenseConfigFileName);

                if (File.Exists(configPath))
                {
                    config =
                        JsonSerializer.Deserialize<LicenseClientConfig>(
                            File.ReadAllText(
                                configPath,
                                Encoding.UTF8),
                            JsonOptions) ??
                        new LicenseClientConfig();
                }
            }
            catch
            {
                config = new LicenseClientConfig();
            }

            // Cho phép IT/deployment override mà không cần sửa file/DLL.
            string? envUrl =
                Environment.GetEnvironmentVariable(
                    "TDL_MEP_SUPABASE_URL");

            string? envKey =
                Environment.GetEnvironmentVariable(
                    "TDL_MEP_SUPABASE_PUBLISHABLE_KEY");

            string? envFunction =
                Environment.GetEnvironmentVariable(
                    "TDL_MEP_LICENSE_FUNCTION");

            if (!string.IsNullOrWhiteSpace(envUrl))
                config.SupabaseProjectUrl = envUrl;

            if (!string.IsNullOrWhiteSpace(envKey))
                config.SupabasePublishableKey = envKey;

            if (!string.IsNullOrWhiteSpace(envFunction))
                config.LicenseFunctionName = envFunction;

            config.Normalize();
            return config;
        }

        private static LicenseApiResult ParseLicenseResponse(string json)
        {
            bool ok = false;
            string message = "";
            string plan = "";
            string expires = "";
            string serverTime = "";

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(json)
                            ? "{}"
                            : json);

                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("ok", out JsonElement okElement))
                    {
                        if (okElement.ValueKind == JsonValueKind.True)
                            ok = true;
                        else if (okElement.ValueKind == JsonValueKind.False)
                            ok = false;
                    }

                    message = GetJsonString(root, "message");
                    plan = GetJsonString(root, "plan");
                    expires = GetJsonString(root, "expires_at");
                    serverTime = GetJsonString(root, "server_time");
                }
            }
            catch
            {
                // Server trả nội dung không phải JSON -> xử lý như rejected.
            }

            DateTimeOffset parsedServerTime;

            if (!DateTimeOffset.TryParse(
                    serverTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsedServerTime))
            {
                parsedServerTime = DateTimeOffset.UtcNow;
            }

            DateTimeOffset? expiry = null;

            if (DateTimeOffset.TryParse(
                    expires,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsedExpiry))
            {
                expiry = parsedExpiry.ToUniversalTime();
            }

            return new LicenseApiResult
            {
                State = ok
                    ? LicenseRequestState.Success
                    : LicenseRequestState.Rejected,

                Message = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : (ok
                        ? "Kích hoạt thành công."
                        : "Key không hợp lệ hoặc đã hết hạn."),

                Plan = plan,
                ExpiresAtUtc = expiry,
                ServerTimeUtc = parsedServerTime.ToUniversalTime()
            };
        }

        private static string GetJsonString(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out JsonElement value))
            {
                return "";
            }

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            if (value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined)
            {
                return "";
            }

            return value.ToString();
        }

        private static string NormalizeDisplayKey(string? value)
        {
            return (value ?? "")
                .Trim()
                .Replace(" ", "")
                .ToUpperInvariant();
        }

        private static bool IsOfflineGraceValid(LicenseCacheData cache)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (cache.ExpiresAtUtc.HasValue &&
                now > cache.ExpiresAtUtc.Value)
            {
                return false;
            }

            if (now > cache.LastOnlineCheckUtc.AddDays(
                    OfflineGraceDays))
            {
                return false;
            }

            // Ngăn chỉnh đồng hồ máy lùi quá xa để kéo dài thời gian dùng.
            if (now < cache.LastServerTimeUtc.AddMinutes(-10))
                return false;

            return true;
        }

        private static string GetCacheFilePath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "ThaiDinhLam",
                "MEPTool");

            return Path.Combine(
                folder,
                "license.cache");
        }

        private static void SaveCache(
            string licenseKey,
            string machineId,
            LicenseApiResult result)
        {
            try
            {
                string path = GetCacheFilePath();
                string? folder = Path.GetDirectoryName(path);

                if (!string.IsNullOrWhiteSpace(folder) &&
                    !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                DateTimeOffset serverTime =
                    result.ServerTimeUtc == default
                        ? DateTimeOffset.UtcNow
                        : result.ServerTimeUtc.ToUniversalTime();

                string payload = BuildCachePayload(
                    NormalizeDisplayKey(licenseKey),
                    machineId,
                    result.Plan,
                    result.ExpiresAtUtc,
                    DateTimeOffset.UtcNow,
                    serverTime);

                string signature = ComputeCacheSignature(
                    payload,
                    machineId);

                File.WriteAllText(
                    path,
                    payload +
                    "signature=" + signature + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Không chặn phiên đang chạy nếu Windows không ghi được cache.
            }
        }

        private static LicenseCacheData? LoadCache(string machineId)
        {
            try
            {
                string path = GetCacheFilePath();

                if (!File.Exists(path))
                    return null;

                string[] lines =
                    File.ReadAllLines(
                        path,
                        Encoding.UTF8);

                Dictionary<string, string> values =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (string line in lines)
                {
                    int separator = line.IndexOf('=');

                    if (separator <= 0)
                        continue;

                    values[line.Substring(0, separator)] =
                        line.Substring(separator + 1);
                }

                string key = GetValue(values, "key");
                string cachedMachine = GetValue(values, "machine");
                string plan = GetValue(values, "plan");
                string expiresText = GetValue(values, "expires");
                string checkedText = GetValue(values, "last_online");
                string serverText = GetValue(values, "server_time");
                string signature = GetValue(values, "signature");

                if (!string.Equals(
                        cachedMachine,
                        machineId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!TryParseUtc(
                        checkedText,
                        out DateTimeOffset lastOnline) ||
                    !TryParseUtc(
                        serverText,
                        out DateTimeOffset lastServer))
                {
                    return null;
                }

                DateTimeOffset? expiry = null;

                if (!string.IsNullOrWhiteSpace(expiresText))
                {
                    if (!TryParseUtc(
                            expiresText,
                            out DateTimeOffset parsedExpiry))
                    {
                        return null;
                    }

                    expiry = parsedExpiry;
                }

                string payload = BuildCachePayload(
                    key,
                    cachedMachine,
                    plan,
                    expiry,
                    lastOnline,
                    lastServer);

                string expectedSignature = ComputeCacheSignature(
                    payload,
                    machineId);

                if (!ConstantTimeEquals(
                        signature,
                        expectedSignature))
                {
                    return null;
                }

                return new LicenseCacheData
                {
                    LicenseKey = key,
                    MachineId = cachedMachine,
                    Plan = plan,
                    ExpiresAtUtc = expiry,
                    LastOnlineCheckUtc = lastOnline,
                    LastServerTimeUtc = lastServer
                };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildCachePayload(
            string key,
            string machine,
            string plan,
            DateTimeOffset? expires,
            DateTimeOffset lastOnline,
            DateTimeOffset serverTime)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine("version=1");
            builder.AppendLine("key=" + (key ?? ""));
            builder.AppendLine("machine=" + (machine ?? ""));
            builder.AppendLine("plan=" + (plan ?? ""));

            builder.AppendLine(
                "expires=" +
                (expires.HasValue
                    ? expires.Value
                        .ToUniversalTime()
                        .ToString(
                            "O",
                            CultureInfo.InvariantCulture)
                    : ""));

            builder.AppendLine(
                "last_online=" +
                lastOnline
                    .ToUniversalTime()
                    .ToString(
                        "O",
                        CultureInfo.InvariantCulture));

            builder.AppendLine(
                "server_time=" +
                serverTime
                    .ToUniversalTime()
                    .ToString(
                        "O",
                        CultureInfo.InvariantCulture));

            return builder.ToString();
        }

        private static string GetValue(
            Dictionary<string, string> values,
            string key)
        {
            return values.TryGetValue(
                    key,
                    out string? value)
                ? value
                : "";
        }

        private static bool TryParseUtc(
            string value,
            out DateTimeOffset result)
        {
            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out result))
            {
                result = result.ToUniversalTime();
                return true;
            }

            result = default;
            return false;
        }

        private static string ComputeCacheSignature(
            string payload,
            string machineId)
        {
            byte[] key = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    CacheSignatureSalt + "|" + machineId));

            using HMACSHA256 hmac =
                new HMACSHA256(key);

            return BytesToHex(
                hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(payload ?? "")));
        }

        private static string Sha256Hex(string value)
        {
            return BytesToHex(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value ?? "")));
        }

        private static string BytesToHex(byte[] bytes)
        {
            return Convert.ToHexString(bytes)
                .ToLowerInvariant();
        }

        private static bool ConstantTimeEquals(
            string? left,
            string? right)
        {
            if (left == null ||
                right == null ||
                left.Length != right.Length)
            {
                return false;
            }

            byte[] leftBytes = Encoding.ASCII.GetBytes(left);
            byte[] rightBytes = Encoding.ASCII.GetBytes(right);

            return CryptographicOperations.FixedTimeEquals(
                leftBytes,
                rightBytes);
        }

        private static void DeleteCache()
        {
            try
            {
                string path = GetCacheFilePath();

                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Bỏ qua lỗi dọn cache; server vẫn là nguồn xác thực chính.
            }
        }
    }

    internal sealed class LicenseActivationWindow : Window
    {
        private readonly string _machineId;
        private readonly WpfTextBox _keyTextBox;
        private readonly System.Windows.Controls.TextBlock _statusText;
        private readonly WpfButton _activateButton;

        internal bool IsActivated { get; private set; }

        internal LicenseActivationWindow(
            string machineId,
            string initialKey,
            string initialMessage)
        {
            _machineId = machineId;

            Title = "KÍCH HOẠT THÁI ĐÌNH LÂM - MEP TOOL";
            Width = 500;
            Height = 385;
            MinWidth = 500;
            MinHeight = 385;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            Background = System.Windows.Media.Brushes.White;

            System.Windows.Controls.Grid root =
                new System.Windows.Controls.Grid();

            root.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition
                {
                    Height = new GridLength(72)
                });

            root.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition
                {
                    Height = new GridLength(
                        1,
                        GridUnitType.Star)
                });

            System.Windows.Controls.Border header =
                new System.Windows.Controls.Border
                {
                    Background =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                0,
                                120,
                                215)),

                    Padding =
                        new Thickness(
                            20,
                            12,
                            20,
                            10)
                };

            System.Windows.Controls.StackPanel headerContent =
                new System.Windows.Controls.StackPanel();

            headerContent.Children.Add(
                new System.Windows.Controls.TextBlock
                {
                    Text = "KÍCH HOẠT BẢN QUYỀN ONLINE",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold
                });

            headerContent.Children.Add(
                new System.Windows.Controls.TextBlock
                {
                    Text =
                        "Key được khóa theo máy và kiểm tra hạn dùng từ máy chủ.",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    Margin = new Thickness(0, 5, 0, 0)
                });

            header.Child = headerContent;
            root.Children.Add(header);

            System.Windows.Controls.StackPanel body =
                new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(22, 16, 22, 16)
                };

            System.Windows.Controls.Grid.SetRow(
                body,
                1);

            body.Children.Add(
                CreateLabel("Mã máy:"));

            System.Windows.Controls.DockPanel machinePanel =
                new System.Windows.Controls.DockPanel
                {
                    Margin = new Thickness(0, 4, 0, 13)
                };

            WpfButton copyMachineButton =
                new WpfButton
                {
                    Content = "SAO CHÉP",
                    Width = 88,
                    Height = 28,
                    Margin = new Thickness(8, 0, 0, 0),
                    FontWeight = FontWeights.Bold
                };

            System.Windows.Controls.DockPanel.SetDock(
                copyMachineButton,
                System.Windows.Controls.Dock.Right);

            WpfTextBox machineTextBox =
                new WpfTextBox
                {
                    Text =
                        OnlineLicenseManager.GetShortMachineCode(
                            machineId),
                    IsReadOnly = true,
                    Height = 28,
                    VerticalContentAlignment =
                        VerticalAlignment.Center,
                    Background =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                242,
                                242,
                                242))
                };

            copyMachineButton.Click += delegate
            {
                try
                {
                    machineTextBox.SelectAll();
                    machineTextBox.Copy();
                    machineTextBox.Select(0, 0);
                }
                catch
                {
                }
            };

            machinePanel.Children.Add(copyMachineButton);
            machinePanel.Children.Add(machineTextBox);
            body.Children.Add(machinePanel);

            body.Children.Add(
                CreateLabel("Nhập key kích hoạt:"));

            _keyTextBox =
                new WpfTextBox
                {
                    Text = initialKey ?? "",
                    Height = 34,
                    Margin = new Thickness(0, 4, 0, 10),
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    CharacterCasing =
                        System.Windows.Controls.CharacterCasing.Upper,
                    VerticalContentAlignment =
                        VerticalAlignment.Center
                };

            _keyTextBox.KeyDown +=
                KeyTextBox_KeyDown;

            body.Children.Add(_keyTextBox);

            _statusText =
                new System.Windows.Controls.TextBlock
                {
                    Text =
                        string.IsNullOrWhiteSpace(initialMessage)
                            ? "Cần có Internet khi kích hoạt lần đầu."
                            : initialMessage,

                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 42,

                    Foreground =
                        string.IsNullOrWhiteSpace(initialMessage)
                            ? System.Windows.Media.Brushes.DimGray
                            : System.Windows.Media.Brushes.Firebrick,

                    Margin = new Thickness(0, 0, 0, 10)
                };

            body.Children.Add(_statusText);

            System.Windows.Controls.Grid buttonGrid =
                new System.Windows.Controls.Grid();

            buttonGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });

            buttonGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition
                {
                    Width = new GridLength(110)
                });

            _activateButton =
                new WpfButton
                {
                    Content = "KÍCH HOẠT ONLINE",
                    Height = 40,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background =
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                0,
                                120,
                                215)),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    BorderThickness = new Thickness(0)
                };

            _activateButton.Click +=
                ActivateButton_Click;

            WpfButton closeButton =
                new WpfButton
                {
                    Content = "ĐÓNG",
                    Height = 40,
                    FontWeight = FontWeights.Bold
                };

            closeButton.Click += delegate
            {
                DialogResult = false;
            };

            System.Windows.Controls.Grid.SetColumn(
                _activateButton,
                0);

            System.Windows.Controls.Grid.SetColumn(
                closeButton,
                1);

            buttonGrid.Children.Add(_activateButton);
            buttonGrid.Children.Add(closeButton);
            body.Children.Add(buttonGrid);

            root.Children.Add(body);
            Content = root;

            Loaded += delegate
            {
                _keyTextBox.Focus();
                _keyTextBox.SelectAll();
            };
        }

        private static System.Windows.Controls.TextBlock CreateLabel(
            string text)
        {
            return new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black
            };
        }

        private void KeyTextBox_KeyDown(
            object sender,
            WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
                return;

            e.Handled = true;
            TryActivate();
        }

        private void ActivateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            TryActivate();
        }

        private void TryActivate()
        {
            string key =
                (_keyTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                SetStatus(
                    "Vui lòng nhập key kích hoạt.",
                    false);

                return;
            }

            _activateButton.IsEnabled = false;
            _keyTextBox.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;

            SetStatus(
                "Đang kết nối máy chủ và kiểm tra key...",
                true);

            LicenseApiResult result;

            try
            {
                result =
                    OnlineLicenseManager.Activate(
                        key,
                        _machineId);
            }
            finally
            {
                Cursor = System.Windows.Input.Cursors.Arrow;
                _activateButton.IsEnabled = true;
                _keyTextBox.IsEnabled = true;
            }

            if (result.State == LicenseRequestState.Success)
            {
                IsActivated = true;

                MessageBox.Show(
                    result.Message + Environment.NewLine +
                    "Hạn sử dụng: " +
                    OnlineLicenseManager.FormatExpiry(
                        result.ExpiresAtUtc),
                    "Kích hoạt thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                return;
            }

            SetStatus(
                result.Message,
                false);

            _keyTextBox.Focus();
            _keyTextBox.SelectAll();
        }

        private void SetStatus(
            string message,
            bool neutral)
        {
            _statusText.Text = message ?? "";

            _statusText.Foreground =
                neutral
                    ? System.Windows.Media.Brushes.DimGray
                    : System.Windows.Media.Brushes.Firebrick;

            _statusText.UpdateLayout();
        }
    }
}