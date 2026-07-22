using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamidashEditor
{
    internal static class LocalRuntimeFolders
    {
        private static readonly object SyncRoot = new();
        private static string? cachedSimFamidashFolder;
        internal const string SimFamidashOverlayFileName = "famidash_path_overlay.lua";
        internal const string FamidashHitboxOverlayFileName = "famidash_hitbox_overlay.lua";

        internal static string BaseDirectory =>
            Path.GetFullPath(AppContext.BaseDirectory);

        internal static string SimFamidashFolder =>
            Path.Combine(BaseDirectory, "sim-famidash");

        internal static string SimFamidashOverlayPath =>
            Path.Combine(SimFamidashFolder, SimFamidashOverlayFileName);

        internal static string FamidashHitboxOverlayPath =>
            Path.Combine(SimFamidashFolder, FamidashHitboxOverlayFileName);

        internal static string MesenFolder =>
            Path.Combine(BaseDirectory, "mesen");

        internal static string MesenExePath =>
            Path.Combine(MesenFolder, "Mesen.exe");

        internal static string EnsureSimFamidashFolder()
        {
            lock (SyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(cachedSimFamidashFolder) &&
                    IsValidSimFamidashFolder(cachedSimFamidashFolder))
                {
                    return cachedSimFamidashFolder;
                }

                string target = SimFamidashFolder;
                if (!IsValidSimFamidashFolder(target))
                {
                    string? source = FindSourceSimFamidashFolder(target);
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        throw new DirectoryNotFoundException(
                            "Could not find a sim-famidash source folder to copy into the local release folder. " +
                            $"Expected local folder:\n{target}");
                    }

                    CopyDirectoryIfNewer(source, target);
                }

                if (!IsValidSimFamidashFolder(target))
                {
                    throw new DirectoryNotFoundException(
                        "The local sim-famidash folder is missing required build files after copy. " +
                        $"Expected 1BigSimExport.bat/build.bat under:\n{target}");
                }

                cachedSimFamidashFolder = target;
                return target;
            }
        }

        internal static string EnsureSimFamidashOverlayScript(string? simFamidashFolder = null)
        {
            lock (SyncRoot)
            {
                string targetRoot = string.IsNullOrWhiteSpace(simFamidashFolder)
                    ? SimFamidashFolder
                    : simFamidashFolder;
                string target = Path.Combine(targetRoot, SimFamidashOverlayFileName);
                string? source = FindSourceOverlayScript(target);

                if (File.Exists(target))
                {
                    if (!string.IsNullOrWhiteSpace(source) &&
                        File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(target))
                    {
                        File.Copy(source, target, overwrite: true);
                    }

                    return target;
                }

                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new FileNotFoundException(
                        "Could not find the standalone Mesen path overlay Lua script. " +
                        $"Expected local script:\n{target}",
                        target);
                }

                Directory.CreateDirectory(targetRoot);
                File.Copy(source, target, overwrite: true);
                return target;
            }
        }

        internal static string EnsureFamidashHitboxOverlayScript()
        {
            lock (SyncRoot)
            {
                string target = FamidashHitboxOverlayPath;
                string? source = FindSourceHitboxOverlayScript(target);

                if (File.Exists(target))
                {
                    if (!string.IsNullOrWhiteSpace(source) &&
                        File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(target))
                    {
                        File.Copy(source, target, overwrite: true);
                    }

                    return target;
                }

                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new FileNotFoundException(
                        "Could not find the Famidash hitbox overlay Lua script. " +
                        $"Expected local script:\n{target}",
                        target);
                }

                Directory.CreateDirectory(SimFamidashFolder);
                File.Copy(source, target, overwrite: true);
                return target;
            }
        }

        internal static bool IsLocalMesenAvailable() =>
            File.Exists(MesenExePath);

        private static bool IsValidSimFamidashFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return false;
            return File.Exists(Path.Combine(folder, "1BigSimExport.bat")) &&
                   File.Exists(Path.Combine(folder, "build.bat")) &&
                   Directory.Exists(Path.Combine(folder, "LEVELS")) &&
                   Directory.Exists(Path.Combine(folder, "MUSIC")) &&
                   Directory.Exists(Path.Combine(folder, "SAUCE"));
        }

        private static string? FindSourceSimFamidashFolder(string target)
        {
            var candidates = new List<string>();
            AddWalkCandidates(candidates, BaseDirectory);
            AddWalkCandidates(candidates, Environment.CurrentDirectory);

            string targetFull = Path.GetFullPath(target);
            foreach (string candidate in candidates
                         .Where(c => !string.IsNullOrWhiteSpace(c))
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(candidate, targetFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsValidSimFamidashFolder(candidate))
                    return candidate;
            }

            return null;
        }

        private static void AddWalkCandidates(List<string> candidates, string? start)
        {
            if (string.IsNullOrWhiteSpace(start)) return;

            DirectoryInfo? dir;
            try { dir = new DirectoryInfo(Path.GetFullPath(start)); }
            catch { return; }

            for (int i = 0; dir != null && i < 10; i++, dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "sim-famidash"));
                candidates.Add(Path.Combine(dir.FullName, "ReleaseBuild", "sim-famidash"));
                candidates.Add(Path.Combine(dir.FullName, "native-windows", "ReleaseBuild", "sim-famidash"));
            }
        }

        private static string? FindSourceOverlayScript(string target)
        {
            var candidates = new List<string>();
            AddOverlayCandidates(candidates, BaseDirectory);
            AddOverlayCandidates(candidates, Environment.CurrentDirectory);

            string targetFull = Path.GetFullPath(target);
            foreach (string candidate in candidates
                         .Where(c => !string.IsNullOrWhiteSpace(c))
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(candidate, targetFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static void AddOverlayCandidates(List<string> candidates, string? start)
        {
            if (string.IsNullOrWhiteSpace(start)) return;

            DirectoryInfo? dir;
            try { dir = new DirectoryInfo(Path.GetFullPath(start)); }
            catch { return; }

            for (int i = 0; dir != null && i < 10; i++, dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "sim-famidash", SimFamidashOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, SimFamidashOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "famidash", SimFamidashOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "ReleaseBuild", "sim-famidash", SimFamidashOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "native-windows", "ReleaseBuild", "sim-famidash", SimFamidashOverlayFileName));
            }
        }

        private static string? FindSourceHitboxOverlayScript(string target)
        {
            var candidates = new List<string>();
            AddHitboxOverlayCandidates(candidates, BaseDirectory);
            AddHitboxOverlayCandidates(candidates, Environment.CurrentDirectory);

            string targetFull = Path.GetFullPath(target);
            foreach (string candidate in candidates
                         .Where(c => !string.IsNullOrWhiteSpace(c))
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(candidate, targetFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static void AddHitboxOverlayCandidates(List<string> candidates, string? start)
        {
            if (string.IsNullOrWhiteSpace(start)) return;

            DirectoryInfo? dir;
            try { dir = new DirectoryInfo(Path.GetFullPath(start)); }
            catch { return; }

            for (int i = 0; dir != null && i < 10; i++, dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "sim-famidash", FamidashHitboxOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "famidash", "LUA SCRIPTS", FamidashHitboxOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "LUA SCRIPTS", FamidashHitboxOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, FamidashHitboxOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "ReleaseBuild", "sim-famidash", FamidashHitboxOverlayFileName));
                candidates.Add(Path.Combine(dir.FullName, "native-windows", "ReleaseBuild", "sim-famidash", FamidashHitboxOverlayFileName));
            }
        }

        private static void CopyDirectoryIfNewer(string sourceRoot, string targetRoot)
        {
            Directory.CreateDirectory(targetRoot);

            foreach (string sourceDir in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceDir);
                if (ShouldSkipRelativePath(relative, isDirectory: true))
                    continue;
                Directory.CreateDirectory(Path.Combine(targetRoot, relative));
            }

            foreach (string sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                if (ShouldSkipRelativePath(relative, isDirectory: false))
                    continue;

                string targetFile = Path.Combine(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? targetRoot);

                var src = new FileInfo(sourceFile);
                var dst = new FileInfo(targetFile);
                if (dst.Exists && dst.LastWriteTimeUtc >= src.LastWriteTimeUtc)
                    continue;

                File.Copy(sourceFile, targetFile, overwrite: true);
            }
        }

        private static bool ShouldSkipRelativePath(string relative, bool isDirectory)
        {
            string normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => string.Equals(p, ".git", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!isDirectory)
            {
                string fileName = Path.GetFileName(normalized);
                if (string.Equals(fileName, "BUILD.zip", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
