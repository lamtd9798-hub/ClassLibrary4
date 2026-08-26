#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(ClassLibrary4.MepPluginExtensionApp))]

namespace ClassLibrary4
{
    /// <summary>
    /// Đăng ký resolver ngay khi module ClassLibrary4 được nạp, trước khi
    /// AutoCAD khởi tạo Palette hoặc JIT các lớp OpenCV/ONNX.
    /// </summary>
    internal static class MepPluginModuleInitializer
    {
#pragma warning disable CA2255 // Chủ ý: plugin AutoCAD cần resolver trước khi JIT lớp AI.
        [ModuleInitializer]
        internal static void Initialize()
        {
            MepPluginExtensionApp.RegisterResolvers();
        }
#pragma warning restore CA2255
    }

    /// <summary>
    /// Resolver dependency dành riêng cho plugin AutoCAD .NET 8.
    ///
    /// Nguyên tắc:
    /// - Chỉ tìm dependency trong thư mục plugin và runtimes/win-x64.
    /// - Không nạp đè assembly của Windows Desktop Runtime như
    ///   System.Drawing.Common hoặc Microsoft.Win32.SystemEvents.
    /// - Managed DLL được nạp vào đúng AssemblyLoadContext đã yêu cầu nó.
    /// - Native OpenCV/ONNX được tìm cạnh DLL hoặc trong thư mục runtimes.
    /// </summary>
    public sealed class MepPluginExtensionApp : IExtensionApplication
    {
        private static readonly object InitGate = new object();
        private static bool _initialized;
        private static string _pluginDirectory = "";
        private static AssemblyLoadContext _pluginLoadContext;

        private static readonly HashSet<string> HostFrameworkAssemblies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Drawing.Common",
                "System.Drawing",
                "Microsoft.Win32.SystemEvents",
                "System.Windows.Forms",
                "PresentationCore",
                "PresentationFramework",
                "WindowsBase"
            };

        internal static string LastResolverError { get; private set; } = "";

        public void Initialize()
        {
            RegisterResolvers();
        }

        public void Terminate()
        {
        }

        public static void RegisterResolvers()
        {
            if (_initialized)
                return;

            lock (InitGate)
            {
                if (_initialized)
                    return;

                try
                {
                    Assembly pluginAssembly = typeof(MepPluginExtensionApp).Assembly;
                    _pluginDirectory = GetPluginDirectory(pluginAssembly);
                    _pluginLoadContext =
                        AssemblyLoadContext.GetLoadContext(pluginAssembly) ??
                        AssemblyLoadContext.Default;

                    _pluginLoadContext.Resolving += ResolveManagedAssembly;
                    _pluginLoadContext.ResolvingUnmanagedDll += ResolveNativeAssembly;
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveFromCurrentDomain;

                    LastResolverError = "";
                    _initialized = true;
                }
                catch (System.Exception ex)
                {
                    LastResolverError = ex.GetType().Name + ": " + ex.Message;
                }
            }
        }

        private static Assembly ResolveManagedAssembly(
            AssemblyLoadContext context,
            AssemblyName requestedName)
        {
            return ResolveAssembly(
                context ?? _pluginLoadContext ?? AssemblyLoadContext.Default,
                requestedName,
                _pluginDirectory);
        }

        private static Assembly ResolveFromCurrentDomain(
            object sender,
            ResolveEventArgs args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(args?.Name))
                    return null;

                return ResolveAssembly(
                    _pluginLoadContext ?? AssemblyLoadContext.Default,
                    new AssemblyName(args.Name),
                    _pluginDirectory);
            }
            catch
            {
                return null;
            }
        }

        private static IntPtr ResolveNativeAssembly(
            Assembly requestingAssembly,
            string unmanagedDllName)
        {
            return ResolveUnmanagedDll(unmanagedDllName, _pluginDirectory);
        }

        private static Assembly ResolveAssembly(
            AssemblyLoadContext context,
            AssemblyName requestedName,
            string baseDirectory)
        {
            if (requestedName == null ||
                string.IsNullOrWhiteSpace(requestedName.Name) ||
                string.IsNullOrWhiteSpace(baseDirectory))
            {
                return null;
            }

            string simpleName = requestedName.Name;

            if (simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
                HostFrameworkAssemblies.Contains(simpleName))
            {
                // Bắt buộc để host .NET 8 tự resolve framework assembly.
                return null;
            }

            try
            {
                Assembly alreadyLoaded = context.Assemblies.FirstOrDefault(
                    assembly =>
                    {
                        try
                        {
                            return AssemblyName.ReferenceMatchesDefinition(
                                assembly.GetName(),
                                requestedName);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                if (alreadyLoaded != null)
                    return alreadyLoaded;
            }
            catch
            {
            }

            foreach (string directory in GetManagedSearchDirectories(baseDirectory))
            {
                string candidate = Path.Combine(directory, simpleName + ".dll");

                if (!File.Exists(candidate) ||
                    !IsCompatibleManagedCandidate(candidate, requestedName))
                {
                    continue;
                }

                try
                {
                    return context.LoadFromAssemblyPath(Path.GetFullPath(candidate));
                }
                catch (System.Exception ex)
                {
                    LastResolverError =
                        simpleName + " | " + ex.GetType().Name + ": " + ex.Message;
                }
            }

            return null;
        }

        private static bool IsCompatibleManagedCandidate(
            string path,
            AssemblyName requestedName)
        {
            try
            {
                AssemblyName candidateName = AssemblyName.GetAssemblyName(path);

                if (!string.Equals(
                        candidateName.Name,
                        requestedName.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Không nạp nhầm major version chỉ vì file có cùng tên.
                if (requestedName.Version != null &&
                    candidateName.Version != null &&
                    requestedName.Version.Major != candidateName.Version.Major)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetManagedSearchDirectories(
            string baseDirectory)
        {
            string[] values =
            {
                baseDirectory,
                Path.Combine(baseDirectory, "runtimes", "win-x64", "lib", "net8.0"),
                Path.Combine(baseDirectory, "runtimes", "win", "lib", "net8.0"),
                Path.Combine(baseDirectory, "lib", "net8.0")
            };

            return values
                .Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IntPtr ResolveUnmanagedDll(
            string unmanagedDllName,
            string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(unmanagedDllName) ||
                string.IsNullOrWhiteSpace(baseDirectory))
            {
                return IntPtr.Zero;
            }

            string simpleName = Path.GetFileNameWithoutExtension(unmanagedDllName);

            if (!string.Equals(simpleName, "OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(simpleName, "onnxruntime", StringComparison.OrdinalIgnoreCase) &&
                !simpleName.StartsWith("onnxruntime_providers_", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            string fileName = simpleName + ".dll";
            string[] searchDirectories =
            {
                baseDirectory,
                Path.Combine(baseDirectory, "runtimes", "win-x64", "native"),
                Path.Combine(baseDirectory, "runtimes", "win", "native"),
                Path.Combine(baseDirectory, "dll", "x64")
            };

            foreach (string directory in searchDirectories)
            {
                string candidate = Path.Combine(directory, fileName);

                try
                {
                    if (File.Exists(candidate) &&
                        NativeLibrary.TryLoad(candidate, out IntPtr handle))
                    {
                        return handle;
                    }
                }
                catch (System.Exception ex)
                {
                    LastResolverError =
                        fileName + " | " + ex.GetType().Name + ": " + ex.Message;
                }
            }

            return IntPtr.Zero;
        }

        private static string GetPluginDirectory(Assembly pluginAssembly)
        {
            try
            {
                string location = pluginAssembly?.Location ?? "";

                if (!string.IsNullOrWhiteSpace(location))
                {
                    string directory = Path.GetDirectoryName(location) ?? "";
                    return string.IsNullOrWhiteSpace(directory)
                        ? ""
                        : Path.GetFullPath(directory);
                }
            }
            catch
            {
            }

            return "";
        }
    }
}
