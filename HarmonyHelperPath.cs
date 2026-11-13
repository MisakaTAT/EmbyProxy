using HarmonyLib;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EmbyProxy
{
    /// <summary>
    /// Harmony (MonoMod) drops a native helper into /tmp on Unix. Some systems,
    /// notably Synology DSM, mount /tmp with noexec, so the helper cannot be loaded.
    /// Point MonoMod at a writable directory on an executable mount instead.
    /// </summary>
    internal static class HarmonyHelperPath
    {
        private const string SwitchName = "HelperDropPath";
        private const string EnvironmentVariable = "MONOMOD_HELPERDROPPATH";

        private static bool _configured;

        public static void Configure(ILogger logger, IApplicationPaths applicationPaths)
        {
            if (_configured)
            {
                return;
            }

            _configured = true;

            try
            {
                var candidates = BuildCandidates(applicationPaths);
                string selected = null;
                string selectedRealPath = null;
                string lastError = null;
                var selectedIsNoExec = false;

                foreach (var candidate in candidates)
                {
                    if (!TryPrepare(candidate, out var realPath, out var isNoExec, out var error))
                    {
                        lastError = error;
                        continue;
                    }

                    if (!isNoExec)
                    {
                        selected = candidate;
                        selectedRealPath = realPath;
                        break;
                    }

                    if (selected == null)
                    {
                        selected = candidate;
                        selectedRealPath = realPath;
                        selectedIsNoExec = true;
                    }
                }

                if (selected == null)
                {
                    logger.Warn("EmbyProxy: no usable directory for the Harmony helper. {0}", lastError ?? string.Empty);
                    RequestLog.NoteError("Harmony 辅助库目录不可用: " + (lastError ?? "没有可写目录"));
                    return;
                }

                if (selectedIsNoExec)
                {
                    logger.Warn("EmbyProxy: all Harmony helper directories are on noexec mounts; using {0}.", selected);
                }

                // Cover MonoMod versions that read the environment before the switch API is used.
                Environment.SetEnvironmentVariable(EnvironmentVariable, selected);

                var switchesType = typeof(Harmony).Assembly.GetType("MonoMod.Switches");
                var setter = switchesType?.GetMethod(
                    "SetSwitchValue",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(object) },
                    null);

                if (setter == null)
                {
                    logger.Warn("EmbyProxy: MonoMod.Switches was not found; Harmony will keep using the default temp directory.");
                    RequestLog.NoteError("Harmony 版本不支持 Monomod 辅助库目录设置");
                    return;
                }

                setter.Invoke(null, new object[] { SwitchName, selected });

                logger.Info("EmbyProxy: Harmony helper directory set to {0} (real path {1}).", selected, selectedRealPath);
                RequestLog.Note(string.Format("Harmony 辅助库: {0}{1}", selected, selectedIsNoExec ? " (noexec)" : string.Empty));
            }
            catch (Exception exception)
            {
                logger.Warn("EmbyProxy: failed to configure the Harmony helper directory: {0}", exception.Message);
                RequestLog.NoteError("Harmony 辅助库目录设置失败: " + exception.Message);
            }
        }

        private static List<string> BuildCandidates(IApplicationPaths applicationPaths)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(string path)
            {
                if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                {
                    candidates.Add(path);
                }
            }

            if (applicationPaths != null)
            {
                Add(Combine(applicationPaths.TempDirectory, "EmbyProxy"));
                Add(Combine(applicationPaths.PluginsPath, ".harmony"));
                Add(Combine(applicationPaths.ProgramDataPath, ".harmony"));
            }

            var assemblyDirectory = Path.GetDirectoryName(typeof(Harmony).Assembly.Location);
            Add(Combine(assemblyDirectory, ".harmony"));
            Add(Path.Combine(Path.GetTempPath(), "embyproxy-harmony"));

            return candidates;
        }

        private static string Combine(string directory, string child)
        {
            return string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, child);
        }

        private static bool TryPrepare(string directory, out string realPath, out bool isNoExec, out string error)
        {
            realPath = directory;
            isNoExec = false;
            error = null;

            try
            {
                Directory.CreateDirectory(directory);

                var probe = Path.Combine(directory, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);

                realPath = GetRealPath(Path.GetFullPath(directory));
                isNoExec = IsNoExecMount(realPath);
                return true;
            }
            catch (Exception exception)
            {
                error = string.Format("{0}: {1}", directory, exception.Message);
                return false;
            }
        }

        private static bool IsNoExecMount(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(path).TrimEnd('/');
                var bestLength = -1;
                var noExec = false;

                foreach (var line in File.ReadLines("/proc/self/mountinfo"))
                {
                    var fields = line.Split(' ');
                    if (fields.Length < 6)
                    {
                        continue;
                    }

                    var mountPoint = UnescapeMountPath(fields[4]).TrimEnd('/');
                    if (mountPoint.Length == 0)
                    {
                        mountPoint = "/";
                    }

                    var matches = fullPath == mountPoint ||
                        fullPath.StartsWith(mountPoint == "/" ? "/" : mountPoint + "/", StringComparison.Ordinal);

                    if (!matches || mountPoint.Length <= bestLength)
                    {
                        continue;
                    }

                    bestLength = mountPoint.Length;
                    noExec = Array.IndexOf(fields[5].Split(','), "noexec") >= 0;
                }

                return noExec;
            }
            catch
            {
                return false;
            }
        }

        private static string UnescapeMountPath(string value)
        {
            return value
                .Replace("\\040", " ")
                .Replace("\\011", "\t")
                .Replace("\\012", "\n")
                .Replace("\\134", "\\");
        }

        private static string GetRealPath(string path)
        {
            try
            {
                var pointer = RealPathNative(path, IntPtr.Zero);
                if (pointer == IntPtr.Zero)
                {
                    return path;
                }

                try
                {
                    return Marshal.PtrToStringAnsi(pointer) ?? path;
                }
                finally
                {
                    Free(pointer);
                }
            }
            catch
            {
                return path;
            }
        }

        [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPathNative(string path, IntPtr resolved);

        [DllImport("libc", EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);
    }
}
