using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace AiMemory.Services
{
    public static class TraySetupService
    {
        private const string ToolCommandName = "ai-memory";
        private const string TrayLaunchAgentLabel = "com.aimemory.tray";

        public record TrayStatus(bool Installed, bool Running, string? AutostartPath, string? ExecutablePath);
        public record TrayCommand(string FileName, string[] Arguments, string WorkingDirectory)
        {
            public string Display => string.Join(" ", new[] { FileName }.Concat(Arguments).Select(QuoteForDisplay));
        }

        public static string GetAutostartPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\Startup", "ai-memory-tray.lnk");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(home, "Library", "LaunchAgents", "com.aimemory.tray.plist");
            }
            else // Linux
            {
                return Path.Combine(home, ".config", "autostart", "ai-memory-tray.desktop");
            }
        }

        public static async Task<TrayStatus> GetStatusAsync()
        {
            var autostartPath = GetAutostartPath();
            var installed = File.Exists(autostartPath);
            var trayCommand = ResolveToolCommand(["tray"]);

            var running = false;
            try
            {
                var currentPid = Environment.ProcessId;
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == currentPid) continue;

                        if (await IsTrayProcessAsync(p))
                        {
                            running = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Ignora processos sem permissão de leitura
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return new TrayStatus(installed, running, autostartPath, trayCommand.Display);
        }

        public static ProcessStartInfo CreateAiMemoryProcessStartInfo(params string[] arguments)
        {
            var command = ResolveToolCommand(arguments);
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = command.WorkingDirectory
            };

            foreach (var argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            AddStableEnvironment(startInfo);
            return startInfo;
        }

        private static TrayCommand ResolveToolCommand(params string[] extraArguments)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var globalToolPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(home, ".dotnet", "tools", $"{ToolCommandName}.exe")
                : Path.Combine(home, ".dotnet", "tools", ToolCommandName);

            if (File.Exists(globalToolPath))
            {
                return new TrayCommand(globalToolPath, extraArguments, Path.GetDirectoryName(globalToolPath) ?? home);
            }

            var appHostPath = Path.Combine(AppContext.BaseDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{ToolCommandName}.exe" : ToolCommandName);
            if (File.Exists(appHostPath))
            {
                return new TrayCommand(appHostPath, extraArguments, AppContext.BaseDirectory);
            }

            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) &&
                File.Exists(processPath) &&
                Path.GetFileNameWithoutExtension(processPath).Equals(ToolCommandName, StringComparison.OrdinalIgnoreCase))
            {
                return new TrayCommand(processPath, extraArguments, Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory);
            }

            var dllPath = Path.Combine(AppContext.BaseDirectory, $"{ToolCommandName}.dll");
            if (File.Exists(dllPath))
            {
                var dotnetPath = !string.IsNullOrWhiteSpace(processPath) &&
                                 Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? processPath
                    : "dotnet";
                return new TrayCommand(dotnetPath, [dllPath, .. extraArguments], AppContext.BaseDirectory);
            }

            return new TrayCommand(ToolCommandName, extraArguments, AppContext.BaseDirectory);
        }

        /// <summary>
        /// Obtém o UID do usuário atual no macOS via `id -u`.
        /// Necessário para usar a API moderna do launchctl (bootstrap/bootout).
        /// </summary>
        private static async Task<string> GetMacOsUidAsync()
        {
            try
            {
                var info = new ProcessStartInfo("id", "-u")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(info);
                if (proc == null) return "";
                var uid = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return uid.Trim();
            }
            catch
            {
                return "";
            }
        }

        public static async Task<bool> InstallAsync()
        {
            var trayCommand = ResolveToolCommand(["tray"]);

            var autostartPath = GetAutostartPath();
            var autostartDir = Path.GetDirectoryName(autostartPath);
            if (!string.IsNullOrEmpty(autostartDir))
            {
                Directory.CreateDirectory(autostartDir);
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var shortcutPath = autostartPath;
                    var psCommand = $"$WshShell = New-Object -ComObject WScript.Shell; " +
                                    $"$Shortcut = $WshShell.CreateShortcut('{PowerShellSingleQuote(shortcutPath)}'); " +
                                    $"$Shortcut.TargetPath = '{PowerShellSingleQuote(trayCommand.FileName)}'; " +
                                    $"$Shortcut.Arguments = '{PowerShellSingleQuote(string.Join(" ", trayCommand.Arguments.Select(QuoteForWindowsArgument)))}'; " +
                                    $"$Shortcut.WorkingDirectory = '{PowerShellSingleQuote(trayCommand.WorkingDirectory)}'; " +
                                    $"$Shortcut.Save()";

                    var psInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"{psCommand}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var process = Process.Start(psInfo);
                    if (process != null) await process.WaitForExitAsync();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var logDir = Path.Combine(home, "Library", "Logs", "AiMemory");
                    Directory.CreateDirectory(logDir);

                    WriteMacOsPlist(autostartPath, trayCommand, logDir);

                    try
                    {
                        var uid = await GetMacOsUidAsync();

                        if (!string.IsNullOrEmpty(uid))
                        {
                            await RunProcessAsync("launchctl", ["bootout", $"gui/{uid}/{TrayLaunchAgentLabel}"], ignoreErrors: true);
                            var bootstrap = await RunProcessAsync("launchctl", ["bootstrap", $"gui/{uid}", autostartPath]);
                            if (bootstrap != 0)
                            {
                                return false;
                            }

                            await RunProcessAsync("launchctl", ["kickstart", "-k", $"gui/{uid}/{TrayLaunchAgentLabel}"], ignoreErrors: true);
                        }
                        else
                        {
                            await RunProcessAsync("launchctl", ["unload", autostartPath], ignoreErrors: true);
                            var load = await RunProcessAsync("launchctl", ["load", "-w", autostartPath]);
                            if (load != 0)
                            {
                                return false;
                            }

                            await RunProcessAsync("launchctl", ["start", TrayLaunchAgentLabel], ignoreErrors: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Aviso ao registrar LaunchAgent no macOS: {ex.Message}");
                    }
                }
                else // Linux
                {
                    var iconPath = Path.Combine(AppContext.BaseDirectory, "Tray", "Assets", "active.png");
                    if (!File.Exists(iconPath))
                    {
                        iconPath = "utilities-system-monitor"; // fallback
                    }

                    var desktopContent = $"""
[Desktop Entry]
Type=Application
Exec={FormatDesktopExec(trayCommand)}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name=AI Memory Tray
Comment=Monitor da bandeja para AI Memory
Icon={iconPath}
""";
                    await File.WriteAllTextAsync(autostartPath, desktopContent);

                    try
                    {
                        await RunProcessAsync("chmod", ["+x", autostartPath], ignoreErrors: true);
                        if (File.Exists(trayCommand.FileName))
                        {
                            await RunProcessAsync("chmod", ["+x", trayCommand.FileName], ignoreErrors: true);
                        }
                    }
                    catch { }
                }

                // Se não for macOS, inicia em segundo plano
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var status = await GetStatusAsync();
                    if (!status.Running)
                    {
                        var startInfo = CreateAiMemoryProcessStartInfo("tray");
                        startInfo.UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                        startInfo.CreateNoWindow = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                        Process.Start(startInfo);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao instalar bandeja de sistema: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> UninstallAsync()
        {
            var autostartPath = GetAutostartPath();

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    try
                    {
                        var uid = await GetMacOsUidAsync();
                        if (!string.IsNullOrEmpty(uid))
                        {
                            await RunProcessAsync("launchctl", ["bootout", $"gui/{uid}/{TrayLaunchAgentLabel}"], ignoreErrors: true);
                        }
                        else
                        {
                            await RunProcessAsync("launchctl", ["unload", autostartPath], ignoreErrors: true);
                        }
                    }
                    catch { }
                }

                if (File.Exists(autostartPath))
                {
                    File.Delete(autostartPath);
                }

                var currentPid = Environment.ProcessId;
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == currentPid) continue;

                        if (await IsTrayProcessAsync(p))
                        {
                            p.Kill();
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao desinstalar bandeja de sistema: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> IsTrayProcessAsync(Process process)
        {
            var processName = process.ProcessName;
            if (!processName.Contains(ToolCommandName, StringComparison.OrdinalIgnoreCase) &&
                !processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var commandLine = await TryGetCommandLineAsync(process);
            if (!string.IsNullOrWhiteSpace(commandLine))
            {
                return commandLine.Contains(ToolCommandName, StringComparison.OrdinalIgnoreCase) &&
                       commandLine.Contains("tray", StringComparison.OrdinalIgnoreCase) &&
                       !commandLine.Contains(" mcp", StringComparison.OrdinalIgnoreCase);
            }

            return processName.Equals(ToolCommandName, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string?> TryGetCommandLineAsync(Process process)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var cmdLinePath = $"/proc/{process.Id}/cmdline";
                    if (File.Exists(cmdLinePath))
                    {
                        var cmdLine = await File.ReadAllTextAsync(cmdLinePath);
                        return cmdLine.Replace('\0', ' ');
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var startInfo = new ProcessStartInfo("ps")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.Add("-p");
                    startInfo.ArgumentList.Add(process.Id.ToString());
                    startInfo.ArgumentList.Add("-o");
                    startInfo.ArgumentList.Add("command=");

                    using var ps = Process.Start(startInfo);
                    if (ps is null) return null;

                    var output = await ps.StandardOutput.ReadToEndAsync();
                    await ps.WaitForExitAsync();
                    return output.Trim();
                }
            }
            catch
            {
                // Best effort only.
            }

            return null;
        }

        private static void WriteMacOsPlist(string path, TrayCommand command, string logDir)
        {
            var programArguments = new XElement("array",
                new XElement("string", command.FileName),
                command.Arguments.Select(argument => new XElement("string", argument)));

            var environment = new XElement("dict",
                new XElement("key", "PATH"),
                new XElement("string", BuildLaunchdPath()));

            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
            {
                environment.Add(new XElement("key", "DOTNET_ROOT"));
                environment.Add(new XElement("string", dotnetRoot));
            }

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
                new XElement("plist",
                    new XAttribute("version", "1.0"),
                    new XElement("dict",
                        new XElement("key", "Label"),
                        new XElement("string", TrayLaunchAgentLabel),
                        new XElement("key", "ProgramArguments"),
                        programArguments,
                        new XElement("key", "WorkingDirectory"),
                        new XElement("string", command.WorkingDirectory),
                        new XElement("key", "EnvironmentVariables"),
                        environment,
                        new XElement("key", "RunAtLoad"),
                        new XElement("true"),
                        new XElement("key", "KeepAlive"),
                        new XElement("false"),
                        new XElement("key", "StandardOutPath"),
                        new XElement("string", Path.Combine(logDir, "tray.log")),
                        new XElement("key", "StandardErrorPath"),
                        new XElement("string", Path.Combine(logDir, "tray-error.log")))));

            document.Save(path);
        }

        private static string BuildLaunchdPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var currentPath = Environment.GetEnvironmentVariable("PATH");
            var entries = new[]
                {
                    Path.Combine(home, ".dotnet", "tools"),
                    "/usr/local/bin",
                    "/opt/homebrew/bin",
                    "/usr/bin",
                    "/bin",
                    "/usr/sbin",
                    "/sbin"
                }
                .Concat((currentPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return string.Join(Path.PathSeparator, entries);
        }

        private static void AddStableEnvironment(ProcessStartInfo startInfo)
        {
            if (!startInfo.Environment.ContainsKey("PATH"))
            {
                startInfo.Environment["PATH"] = BuildLaunchdPath();
            }
        }

        private static string FormatDesktopExec(TrayCommand command)
            => string.Join(" ", new[] { command.FileName }.Concat(command.Arguments).Select(QuoteForDesktopExec));

        private static string QuoteForDesktopExec(string value)
        {
            if (value.Length == 0) return "\"\"";
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return value.Any(char.IsWhiteSpace) ? $"\"{escaped}\"" : escaped;
        }

        private static string QuoteForWindowsArgument(string value)
            => value.Any(char.IsWhiteSpace) || value.Contains('"')
                ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                : value;

        private static string QuoteForDisplay(string value)
            => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

        private static string PowerShellSingleQuote(string value)
            => value.Replace("'", "''");

        private static async Task<int> RunProcessAsync(string fileName, string[] arguments, bool ignoreErrors = false)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ignoreErrors ? 0 : 1;
            }

            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 && !ignoreErrors && !string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine(stderr.Trim());
            }

            return process.ExitCode;
        }
    }
}
