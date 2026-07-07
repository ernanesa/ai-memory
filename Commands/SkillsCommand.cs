using AiMemory.Configuration;

namespace AiMemory.Commands
{
    public static class SkillsCommand
    {
        private const string PackName = "dotnet10";
        private const string PackDisplayName = ".NET 10 Migration Skills Pack";

        public static Task ListAsync()
        {
            var sourceRoot = ResolvePackSourceRoot();
            if (sourceRoot is null)
            {
                Console.WriteLine("Skills pack not found. Expected ai-config-files near the tool executable or current directory.");
                return Task.CompletedTask;
            }

            Console.WriteLine(PackDisplayName);
            Console.WriteLine($"Source: {sourceRoot}");
            Console.WriteLine();

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).OrderBy(x => x))
            {
                Console.WriteLine(Path.GetRelativePath(sourceRoot, file));
            }

            return Task.CompletedTask;
        }

        public static async Task DetectAsync()
        {
            var targets = await DetectTargetsAsync();
            PrintTargets(targets);
        }

        public static async Task InstallAsync(bool all)
        {
            if (all)
            {
                await InstallTargetsAsync(await DetectTargetsAsync());
                return;
            }

            await InstallInteractiveAsync();
        }

        public static async Task InstallInteractiveAsync()
        {
            var sourceRoot = ResolvePackSourceRoot();
            if (sourceRoot is null)
            {
                Console.WriteLine("Skills pack not found. Expected ai-config-files near the tool executable or current directory.");
                return;
            }

            var targets = await DetectTargetsAsync();
            if (targets.Count == 0)
            {
                Console.WriteLine("No install targets were detected.");
                Console.WriteLine("Tip: run 'ai-memory skills install --all' after configuring workspaces/projects.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine(PackDisplayName);
            Console.WriteLine("Detected install targets:");
            for (var i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                Console.WriteLine($"  [{i + 1}] {t.DisplayName}");
                Console.WriteLine($"      {t.Path}");
            }
            Console.WriteLine($"  [A] Install in all detected targets");
            Console.WriteLine($"  [S] Skip skills installation");
            Console.WriteLine();

            Console.Write("Choose targets (comma-separated numbers, A for all) [A]: ");
            var answer = (Console.ReadLine() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(answer) ||
                answer.Equals("a", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("todos", StringComparison.OrdinalIgnoreCase))
            {
                await InstallTargetsAsync(targets);
                return;
            }

            if (answer.Equals("s", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("skip", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Skills installation skipped.");
                return;
            }

            var selected = new List<InstallTarget>();
            foreach (var part in answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var index) && index >= 1 && index <= targets.Count)
                {
                    selected.Add(targets[index - 1]);
                }
            }

            if (selected.Count == 0)
            {
                Console.WriteLine("No valid target selected. Skills installation skipped.");
                return;
            }

            await InstallTargetsAsync(selected.DistinctBy(t => t.Path).ToList());
        }

        private static async Task InstallTargetsAsync(IReadOnlyList<InstallTarget> targets)
        {
            var sourceRoot = ResolvePackSourceRoot();
            if (sourceRoot is null)
            {
                Console.WriteLine("Skills pack not found. Expected ai-config-files near the tool executable or current directory.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Installing {PackDisplayName} from:");
            Console.WriteLine($"  {sourceRoot}");
            Console.WriteLine();

            foreach (var target in targets.DistinctBy(t => t.Path))
            {
                try
                {
                    await CopyDirectoryAsync(sourceRoot, target.Path);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[ok] {target.DisplayName}");
                    Console.ResetColor();
                    Console.WriteLine($"     {target.Path}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[warn] {target.DisplayName}: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.WriteLine("Next: configure your IDE/CLI to use the installed prompt/skill files when it does not auto-discover them.");
        }

        private static async Task<List<InstallTarget>> DetectTargetsAsync()
        {
            var targets = new List<InstallTarget>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrWhiteSpace(home))
            {
                targets.Add(new InstallTarget("local", "AI Memory local profile", Path.Combine(home, ".ai-memory", "agent-packs", PackName)));

                AddIfClientExists(targets, "codex", "Codex CLI profile", Path.Combine(home, ".codex"), Path.Combine(home, ".codex", "skills", "ai-memory-dotnet10"));
                AddIfClientExists(targets, "claude", "Claude profile", Path.Combine(home, ".claude"), Path.Combine(home, ".claude", "skills", "ai-memory-dotnet10"));
                AddIfClientExists(targets, "opencode", "opencode profile", Path.Combine(home, ".config", "opencode"), Path.Combine(home, ".config", "opencode", "skills", "ai-memory-dotnet10"));
                AddIfClientExists(targets, "cursor", "Cursor profile", Path.Combine(home, ".cursor"), Path.Combine(home, ".cursor", "ai-memory", PackName));

                var vsCodeUser = GetVsCodeUserDirectory(home);
                if (vsCodeUser is not null)
                {
                    targets.Add(new InstallTarget("vscode", "VS Code user profile", Path.Combine(vsCodeUser, "ai-memory", PackName)));
                }
                else if (CommandExists("code"))
                {
                    var defaultPath = GetDefaultVsCodeUserDirectory(home);
                    if (!string.IsNullOrWhiteSpace(defaultPath))
                    {
                        targets.Add(new InstallTarget("vscode", "VS Code user profile", Path.Combine(defaultPath, "ai-memory", PackName)));
                    }
                }
            }

            try
            {
                var config = await ConfigService.LoadAsync();
                foreach (var workspace in config.Workspaces)
                {
                    foreach (var project in workspace.Projects)
                    {
                        var root = ConfigService.ExpandPath(project.Path);
                        if (!Directory.Exists(root))
                        {
                            continue;
                        }

                        targets.Add(new InstallTarget(
                            $"project:{workspace.Name}/{project.Name}",
                            $"Project-local .ai skills for {workspace.Name}/{project.Name}",
                            Path.Combine(root, ".ai", "agent-packs", PackName)));

                        if (Directory.Exists(Path.Combine(root, ".roo")) || Directory.Exists(Path.Combine(root, ".roomodes")))
                        {
                            targets.Add(new InstallTarget(
                                $"roo:{workspace.Name}/{project.Name}",
                                $"Roo Code project profile for {workspace.Name}/{project.Name}",
                                Path.Combine(root, ".roo", "ai-memory", PackName)));
                        }

                        if (Directory.Exists(Path.Combine(root, ".opencode")))
                        {
                            targets.Add(new InstallTarget(
                                $"opencode-project:{workspace.Name}/{project.Name}",
                                $"opencode project profile for {workspace.Name}/{project.Name}",
                                Path.Combine(root, ".opencode", "skills", "ai-memory-dotnet10")));
                        }
                    }
                }
            }
            catch
            {
                // Config loading should not block detection of global targets.
            }

            return targets
                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? ResolvePackSourceRoot()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "ai-config-files"),
                Path.Combine(Directory.GetCurrentDirectory(), "ai-config-files")
            };

            return candidates.FirstOrDefault(Directory.Exists);
        }

        private static void AddIfClientExists(List<InstallTarget> targets, string command, string displayName, string detectDirectory, string installDirectory)
        {
            if (Directory.Exists(detectDirectory) || CommandExists(command))
            {
                targets.Add(new InstallTarget(command, displayName, installDirectory));
            }
        }

        private static string? GetVsCodeUserDirectory(string home)
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code - Insiders", "User")
                }
                : OperatingSystem.IsMacOS()
                    ? new[]
                    {
                        Path.Combine(home, "Library", "Application Support", "Code", "User"),
                        Path.Combine(home, "Library", "Application Support", "Code - Insiders", "User")
                    }
                    : new[]
                    {
                        Path.Combine(home, ".config", "Code", "User"),
                        Path.Combine(home, ".config", "Code - Insiders", "User")
                    };

            return candidates.FirstOrDefault(Directory.Exists);
        }

        private static string GetDefaultVsCodeUserDirectory(string home)
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User");
            }

            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(home, "Library", "Application Support", "Code", "User");
            }

            return Path.Combine(home, ".config", "Code", "User");
        }

        private static bool CommandExists(string command)
        {
            try
            {
                var locator = OperatingSystem.IsWindows() ? "where" : "which";
                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = locator,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                process.StartInfo.ArgumentList.Add(command);
                process.Start();

                if (!process.WaitForExit(1500))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static Task CopyDirectoryAsync(string sourceDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDirectory, file);
                var destination = Path.Combine(targetDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

            return Task.CompletedTask;
        }

        private static void PrintTargets(IReadOnlyList<InstallTarget> targets)
        {
            if (targets.Count == 0)
            {
                Console.WriteLine("No install targets detected.");
                return;
            }

            Console.WriteLine("Detected install targets:");
            foreach (var target in targets)
            {
                Console.WriteLine($"- {target.DisplayName}");
                Console.WriteLine($"  {target.Path}");
            }
        }

        private sealed record InstallTarget(string Id, string DisplayName, string Path);
    }
}
