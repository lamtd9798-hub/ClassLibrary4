#nullable disable
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(ClassLibrary4.MepPluginExtensionApp))]

namespace ClassLibrary4
{
    /// <summary>
    /// AutoCAD .NET 8 Assembly & Native Resolver.
    /// 
    /// Đăng ký tự động khi AutoCAD chạy NETLOAD qua [assembly: ExtensionApplication].
    /// Tự động định vị và nạp các dependency của plugin nằm cạnh ClassLibrary4.dll:
    /// - System.Drawing.Common (và các dependency phụ thuộc)
    /// - OpenCvSharp / OpenCvSharp.Extensions / OpenCvSharpExtern.dll
    /// - Microsoft.ML.OnnxRuntime / onnxruntime.dll
    /// - Microsoft.Win32.SystemEvents
    /// - System.Numerics.Tensors
    /// </summary>
    public class MepPluginExtensionApp : IExtensionApplication
    {
        private static bool _initialized;
        private static readonly object _initLock = new object();

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

            lock (_initLock)
            {
                if (_initialized)
                    return;

                try
                {
                    string pluginDir = GetPluginDirectory();
                    Assembly pluginAssembly = typeof(MepPluginExtensionApp).Assembly;
                    AssemblyLoadContext pluginAlc = AssemblyLoadContext.GetLoadContext(pluginAssembly);

                    // 1. Hook AssemblyLoadContext của chính plugin DLL (AutoCAD custom ALC)
                    if (pluginAlc != null && pluginAlc != AssemblyLoadContext.Default)
                    {
                        pluginAlc.Resolving += (context, assemblyName) =>
                        {
                            return ResolveAssembly(context, assemblyName, pluginDir);
                        };

                        pluginAlc.ResolvingUnmanagedDll += (unmanagedDllAssembly, unmanagedDllName) =>
                        {
                            return ResolveUnmanagedDll(unmanagedDllName, pluginDir);
                        };
                    }

                    // 2. Hook AssemblyLoadContext.Default (.NET 8 CoreCLR)
                    AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
                    {
                        return ResolveAssembly(context, assemblyName, pluginDir);
                    };

                    AssemblyLoadContext.Default.ResolvingUnmanagedDll += (unmanagedDllAssembly, unmanagedDllName) =>
                    {
                        return ResolveUnmanagedDll(unmanagedDllName, pluginDir);
                    };

                    // 3. Hook AppDomain.CurrentDomain.AssemblyResolve (Fallback)
                    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                    {
                        AssemblyName asmName = new AssemblyName(args.Name);
                        return ResolveAssembly(pluginAlc ?? AssemblyLoadContext.Default, asmName, pluginDir);
                    };

                    // 4. Eagerly Pre-load các dependency quan trọng nằm cạnh ClassLibrary4.dll
                    EagerLoadPluginDependencies(pluginAlc, pluginDir);

                    _initialized = true;
                }
                catch
                {
                    // Tránh ném lỗi chặn khởi động AutoCAD
                }
            }
        }

        private static void EagerLoadPluginDependencies(AssemblyLoadContext alc, string pluginDir)
        {
            if (string.IsNullOrWhiteSpace(pluginDir) || !Directory.Exists(pluginDir))
                return;

            string[] priorityDlls = new string[]
            {
                "Microsoft.Win32.SystemEvents.dll",
                "System.Drawing.Common.dll",
                "OpenCvSharp.dll",
                "OpenCvSharp.Extensions.dll",
                "Microsoft.ML.OnnxRuntime.dll",
                "System.Numerics.Tensors.dll"
            };

            foreach (string dllName in priorityDlls)
            {
                string path = Path.Combine(pluginDir, dllName);
                if (File.Exists(path))
                {
                    try
                    {
                        if (alc != null)
                            alc.LoadFromAssemblyPath(path);
                        else
                            AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    }
                    catch
                    {
                        try
                        {
                            Assembly.LoadFrom(path);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static string GetPluginDirectory()
        {
            try
            {
                string loc = typeof(MepPluginExtensionApp).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    return Path.GetDirectoryName(loc);
                }
            }
            catch
            {
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return baseDir ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static Assembly ResolveAssembly(AssemblyLoadContext context, AssemblyName assemblyName, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory) || assemblyName == null || string.IsNullOrWhiteSpace(assemblyName.Name))
                return null;

            string targetName = assemblyName.Name;

            // 1. Thư mục chính của plugin
            string candidatePath = Path.Combine(baseDirectory, targetName + ".dll");
            if (File.Exists(candidatePath))
            {
                return TryLoadAssemblyPath(context, candidatePath);
            }

            // 2. Thư mục con runtimes/win-x64 hoặc runtimes/win
            string[] subDirs = new string[]
            {
                Path.Combine(baseDirectory, "runtimes", "win-x64", "lib", "net8.0"),
                Path.Combine(baseDirectory, "runtimes", "win", "lib", "net8.0"),
                Path.Combine(baseDirectory, "runtimes", "win-x64", "native"),
                Path.Combine(baseDirectory, "dll", "x64")
            };

            foreach (string sub in subDirs)
            {
                string subPath = Path.Combine(sub, targetName + ".dll");
                if (File.Exists(subPath))
                {
                    return TryLoadAssemblyPath(context, subPath);
                }
            }

            return null;
        }

        private static Assembly TryLoadAssemblyPath(AssemblyLoadContext context, string path)
        {
            if (context != null)
            {
                try
                {
                    return context.LoadFromAssemblyPath(path);
                }
                catch
                {
                }
            }

            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            catch
            {
                try
                {
                    return Assembly.LoadFrom(path);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static IntPtr ResolveUnmanagedDll(string unmanagedDllName, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(unmanagedDllName))
                return IntPtr.Zero;

            string fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? unmanagedDllName
                : unmanagedDllName + ".dll";

            string[] searchDirs = new string[]
            {
                baseDirectory,
                Path.Combine(baseDirectory, "runtimes", "win-x64", "native"),
                Path.Combine(baseDirectory, "dll", "x64")
            };

            foreach (string dir in searchDirs)
            {
                string fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                {
                    if (NativeLibrary.TryLoad(fullPath, out IntPtr handle))
                    {
                        return handle;
                    }
                }
            }

            return IntPtr.Zero;
        }
    }
}
