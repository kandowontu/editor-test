using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

// Launcher to run the native-windows WPF project when running `dotnet run` from the workspace root.
class Program
{
    static int Main(string[] args)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            // locate workspace root by searching upward from the current directory and the app base directory
            string FindWorkspaceRoot()
            {
                string[] starts = { Directory.GetCurrentDirectory(), baseDir };
                foreach (var start in starts)
                {
                    try
                    {
                        var dir = new DirectoryInfo(start);
                        while (dir != null)
                        {
                            if (Directory.Exists(Path.Combine(dir.FullName, "native-windows"))) return dir.FullName;
                            dir = dir.Parent;
                        }
                    }
                    catch { }
                }
                return Directory.GetCurrentDirectory();
            }
            var workspaceRoot = FindWorkspaceRoot();
            // locate the built WPF exe (after build it will be in native-windows/bin/Debug/net7.0-windows)
            var candidate = Path.Combine(workspaceRoot, "native-windows", "bin", "Debug");
            if (!Directory.Exists(candidate)) candidate = Path.Combine(workspaceRoot, "native-windows", "bin");

            string exe = null;
            foreach (var dir in Directory.EnumerateDirectories(candidate, "net*", SearchOption.TopDirectoryOnly))
            {
                var p = Path.Combine(dir, "FamidashEditor.exe");
                if (File.Exists(p)) { exe = p; break; }
            }
            if (exe == null)
            {
                Console.WriteLine("Built WPF executable not found. Attempting to run project build and then run the exe...");
                // Attempt to build the native-windows project using dotnet
                var psi = new ProcessStartInfo("dotnet", "build native-windows\\FamidashEditor.csproj") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                var p = Process.Start(psi);
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    Console.WriteLine("Failed to build native-windows project.");
                    Console.WriteLine(p.StandardOutput.ReadToEnd());
                    Console.WriteLine(p.StandardError.ReadToEnd());
                    return p.ExitCode;
                }
                foreach (var dir in Directory.EnumerateDirectories(Path.Combine(Directory.GetCurrentDirectory(), "native-windows", "bin", "Debug"), "net*", SearchOption.TopDirectoryOnly))
                {
                    var pexe = Path.Combine(dir, "FamidashEditor.exe");
                    if (File.Exists(pexe)) { exe = pexe; break; }
                }
            }
            if (exe == null)
            {
                Console.WriteLine("Could not find FamidashEditor.exe after build. Exiting.");
                return 2;
            }

            var start = new ProcessStartInfo(exe) { UseShellExecute = true };
            Process.Start(start);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Launcher error: " + ex.Message);
            return 1;
        }
    }
}
