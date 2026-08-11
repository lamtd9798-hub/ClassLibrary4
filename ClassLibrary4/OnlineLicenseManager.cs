#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        public string Message { get; set; }
        public string Plan { get; set; }
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public DateTimeOffset ServerTimeUtc { get; set; }
    }

    internal sealed class LicenseCacheData
    {
        public string LicenseKey { get; set; }
        public string MachineId { get; set; }
        public string Plan { get; set; }
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public DateTimeOffset LastOnlineCheckUtc { get; set; }
        public DateTimeOffset LastServerTimeUtc { get; set; }
    }

    public static class OnlineLicenseManager
    {
        // Chỉ chứa Project URL và Publishable key công khai.
        // KHÔNG đặt sb_secret hoặc service_role trong file DLL này.
        private const string SupabaseProjectUrl =
            "https://qjcjljbkkmzzsqjnmyzn.supabase.co";

        private const string SupabasePublishableKey =
            "sb_publishable_YdPKZ38JjLr237rGk1MxrA_bT2AMG7_";

        private const string LicenseFunctionName = "license";
        private const int OfflineGraceDays = 3;
        private const int HttpTimeoutMilliseconds = 12000;
        private const string CacheSignatureSalt =
            "TDL-MEP-LICENSE-CACHE-20260811-V1";

        // Chỉ kiểm tra online một lần trong mỗi phiên AutoCAD.
        // Lệnh HIENBANG kiểm tra trước; constructor BOCTACHUI gọi lại sẽ
        // nhận true ngay, không phát sinh thêm một request mạng.
        private static bool _sessionActivated = false;

        public static bool EnsureActivated()
        {
            if (_sessionActivated)
                return true;

            string machineId = GetMachineId();
            LicenseCacheData cache = LoadCache(machineId);
            string initialKey = cache != null ? cache.LicenseKey : "";
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
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography"))
                {
                    machineGuid = Convert.ToString(
                        key != null ? key.GetValue("MachineGuid") : null,
                        CultureInfo.InvariantCulture) ?? "";
                }
            }
            catch
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Cryptography"))
                    {
                        machineGuid = Convert.ToString(
                            key != null ? key.GetValue("MachineGuid") : null,
                            CultureInfo.InvariantCulture) ?? "";
                    }
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
                .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        private static LicenseApiResult RequestLicense(
            string action,
            string licenseKey,
            string machineId)
        {
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12;

                string endpoint =
                    SupabaseProjectUrl.TrimEnd('/') +
                    "/functions/v1/" + LicenseFunctionName;

                string appVersion = "";
                try
                {
                    Version version = Assembly
                        .GetExecutingAssembly()
                        .GetName()
                        .Version;
                    appVersion = version != null
                        ? version.ToString()
                        : "";
                }
                catch
                {
                    appVersion = "";
                }

                string body =
                    "{" +
                    "\"action\":\"" + JsonEscape(action) + "\"," +
                    "\"license_key\":\"" +
                        JsonEscape(NormalizeDisplayKey(licenseKey)) + "\"," +
                    "\"machine_id\":\"" +
                        JsonEscape(machineId) + "\"," +
                    "\"app_version\":\"" +
                        JsonEscape(appVersion) + "\"" +
                    "}";

                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                HttpWebRequest request =
                    (HttpWebRequest)WebRequest.Create(endpoint);

                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Accept = "application/json";
                request.Timeout = HttpTimeoutMilliseconds;
                request.ReadWriteTimeout = HttpTimeoutMilliseconds;
                request.KeepAlive = false;
                request.ContentLength = bodyBytes.Length;
                request.Headers["apikey"] = SupabasePublishableKey;
                request.Headers[HttpRequestHeader.Authorization] =
                    "Bearer " + SupabasePublishableKey;

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(
                        bodyBytes,
                        0,
                        bodyBytes.Length);
                }

                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(
                    responseStream,
                    Encoding.UTF8))
                {
                    return ParseLicenseResponse(reader.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                string serverBody = ReadWebExceptionBody(ex);
                if (!string.IsNullOrWhiteSpace(serverBody))
                {
                    LicenseApiResult rejected =
                        ParseLicenseResponse(serverBody);
                    if (rejected.State != LicenseRequestState.Success)
                        return rejected;
                }

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

        private static LicenseApiResult ParseLicenseResponse(string json)
        {
            bool ok = Regex.IsMatch(
                json ?? "",
                "\\\"ok\\\"\\s*:\\s*true",
                RegexOptions.IgnoreCase);

            string message = GetJsonString(json, "message");
            string plan = GetJsonString(json, "plan");
            string expires = GetJsonString(json, "expires_at");
            string serverTime = GetJsonString(json, "server_time");

            DateTimeOffset parsedServerTime;
            if (!DateTimeOffset.TryParse(
                    serverTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsedServerTime))
            {
                parsedServerTime = DateTimeOffset.UtcNow;
            }

            DateTimeOffset parsedExpiry;
            DateTimeOffset? expiry = null;
            if (DateTimeOffset.TryParse(
                    expires,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsedExpiry))
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
                Plan = plan ?? "",
                ExpiresAtUtc = expiry,
                ServerTimeUtc = parsedServerTime.ToUniversalTime()
            };
        }

        private static string ReadWebExceptionBody(WebException ex)
        {
            try
            {
                if (ex == null || ex.Response == null)
                    return "";

                using (WebResponse response = ex.Response)
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(
                    stream,
                    Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return "";
            }
        }

        private static string GetJsonString(
            string json,
            string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            Match match = Regex.Match(
                json,
                "\\\"" + Regex.Escape(propertyName) +
                "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return "";

            return JsonUnescape(match.Groups[1].Value);
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
                return "";

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string JsonUnescape(string value)
        {
            if (value == null)
                return "";

            return value
                .Replace("\\r", "\r")
                .Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private static string NormalizeDisplayKey(string value)
        {
            return (value ?? "")
                .Trim()
                .Replace(" ", "")
                .ToUpperInvariant();
        }

        private static bool IsOfflineGraceValid(LicenseCacheData cache)
        {
            if (cache == null)
                return false;

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

            return Path.Combine(folder, "license.cache");
        }

        private static void SaveCache(
            string licenseKey,
            string machineId,
            LicenseApiResult result)
        {
            try
            {
                string path = GetCacheFilePath();
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                DateTimeOffset serverTime = result.ServerTimeUtc == default(
                    DateTimeOffset)
                    ? DateTimeOffset.UtcNow
                    : result.ServerTimeUtc.ToUniversalTime();

                string payload = BuildCachePayload(
                    NormalizeDisplayKey(licenseKey),
                    machineId,
                    result.Plan ?? "",
                    result.ExpiresAtUtc,
                    DateTimeOffset.UtcNow,
                    serverTime);

                string signature = ComputeCacheSignature(
                    payload,
                    machineId);

                File.WriteAllText(
                    path,
                    payload + "signature=" + signature + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Không chặn phiên đang chạy nếu Windows không ghi được cache.
            }
        }

        private static LicenseCacheData LoadCache(string machineId)
        {
            try
            {
                string path = GetCacheFilePath();
                if (!File.Exists(path))
                    return null;

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
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

                DateTimeOffset lastOnline;
                DateTimeOffset lastServer;
                if (!TryParseUtc(checkedText, out lastOnline) ||
                    !TryParseUtc(serverText, out lastServer))
                {
                    return null;
                }

                DateTimeOffset parsedExpiry;
                DateTimeOffset? expiry = null;
                if (!string.IsNullOrWhiteSpace(expiresText))
                {
                    if (!TryParseUtc(expiresText, out parsedExpiry))
                        return null;
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
                    ? expires.Value.ToUniversalTime().ToString(
                        "O",
                        CultureInfo.InvariantCulture)
                    : ""));
            builder.AppendLine(
                "last_online=" +
                lastOnline.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            builder.AppendLine(
                "server_time=" +
                serverTime.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string GetValue(
            Dictionary<string, string> values,
            string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
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

            result = default(DateTimeOffset);
            return false;
        }

        private static string ComputeCacheSignature(
            string payload,
            string machineId)
        {
            byte[] key = SHA256.Create().ComputeHash(
                Encoding.UTF8.GetBytes(
                    CacheSignatureSalt + "|" + machineId));

            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return BytesToHex(hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(payload ?? "")));
            }
        }

        private static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BytesToHex(sha.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? "")));
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool ConstantTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];

            return difference == 0;
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
                // Bỏ qua lỗi dọn cache; máy chủ vẫn là nguồn xác thực chính.
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
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            {
                Height = new GridLength(72)
            });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

            System.Windows.Controls.Border header =
                new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 120, 215)),
                    Padding = new Thickness(20, 12, 20, 10)
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
                    Text = "Key được khóa theo máy và kiểm tra hạn dùng từ máy chủ.",
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
            System.Windows.Controls.Grid.SetRow(body, 1);

            body.Children.Add(CreateLabel("Mã máy:"));

            System.Windows.Controls.DockPanel machinePanel =
                new System.Windows.Controls.DockPanel
                {
                    Margin = new Thickness(0, 4, 0, 13)
                };

            WpfButton copyMachineButton = new WpfButton
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

            WpfTextBox machineTextBox = new WpfTextBox
            {
                Text = OnlineLicenseManager.GetShortMachineCode(machineId),
                IsReadOnly = true,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(242, 242, 242))
            };

            copyMachineButton.Click += delegate
            {
                try
                {
                    // Sao chép trực tiếp từ WPF TextBox, không tham chiếu
                    // System.Windows.Clipboard hoặc System.Windows.Forms.Clipboard.
                    // Cách này loại bỏ hoàn toàn lỗi CS0104 khi dự án dùng cả WPF và WinForms.
                    machineTextBox.SelectAll();
                    machineTextBox.Copy();
                    machineTextBox.Select(0, 0);
                }
                catch
                {
                    // Clipboard đang bận thì người dùng vẫn có thể bôi đen.
                }
            };

            machinePanel.Children.Add(copyMachineButton);
            machinePanel.Children.Add(machineTextBox);
            body.Children.Add(machinePanel);

            body.Children.Add(CreateLabel("Nhập key kích hoạt:"));

            _keyTextBox = new WpfTextBox
            {
                Text = initialKey ?? "",
                Height = 34,
                Margin = new Thickness(0, 4, 0, 10),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                CharacterCasing = System.Windows.Controls.CharacterCasing.Upper,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _keyTextBox.KeyDown += KeyTextBox_KeyDown;
            body.Children.Add(_keyTextBox);

            _statusText = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(initialMessage)
                    ? "Cần có Internet khi kích hoạt lần đầu."
                    : initialMessage,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 42,
                Foreground = string.IsNullOrWhiteSpace(initialMessage)
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
                    Width = new GridLength(1, GridUnitType.Star)
                });
            buttonGrid.ColumnDefinitions.Add(
                new System.Windows.Controls.ColumnDefinition
                {
                    Width = new GridLength(110)
                });

            _activateButton = new WpfButton
            {
                Content = "KÍCH HOẠT ONLINE",
                Height = 40,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 215)),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            _activateButton.Click += ActivateButton_Click;

            WpfButton closeButton = new WpfButton
            {
                Content = "ĐÓNG",
                Height = 40,
                FontWeight = FontWeights.Bold
            };
            closeButton.Click += delegate
            {
                DialogResult = false;
            };

            System.Windows.Controls.Grid.SetColumn(_activateButton, 0);
            System.Windows.Controls.Grid.SetColumn(closeButton, 1);
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
            if (e.Key == WpfKey.Enter)
            {
                e.Handled = true;
                TryActivate();
            }
        }

        private void ActivateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            TryActivate();
        }

        private void TryActivate()
        {
            string key = (_keyTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                SetStatus("Vui lòng nhập key kích hoạt.", false);
                return;
            }

            _activateButton.IsEnabled = false;
            _keyTextBox.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;
            SetStatus("Đang kết nối máy chủ và kiểm tra key...", true);

            LicenseApiResult result;
            try
            {
                result = OnlineLicenseManager.Activate(key, _machineId);
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
                    OnlineLicenseManager.FormatExpiry(result.ExpiresAtUtc),
                    "Kích hoạt thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                return;
            }

            SetStatus(result.Message, false);
            _keyTextBox.Focus();
            _keyTextBox.SelectAll();
        }

        private void SetStatus(string message, bool neutral)
        {
            _statusText.Text = message ?? "";
            _statusText.Foreground = neutral
                ? System.Windows.Media.Brushes.DimGray
                : System.Windows.Media.Brushes.Firebrick;
            _statusText.UpdateLayout();
        }
    }

}