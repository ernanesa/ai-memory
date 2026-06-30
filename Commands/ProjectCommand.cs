using AiMemory.Configuration;

namespace AiMemory.Commands
{
    public static class ProjectCommand
    {
        public static async Task AddAsync(string? workspaceName)
        {
            var config = await ConfigService.LoadAsync();
            var workspace = ResolveWorkspace(config, workspaceName);

            var path = Prompt("Project directory");
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("Project directory is required.");
                return;
            }

            path = ConfigService.ExpandPath(path);
            var name = new DirectoryInfo(path).Name;

            var existing = workspace.Projects.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                workspace.Projects.Add(new AiMemoryProjectConfig { Name = name, Path = path });
            }
            else
            {
                existing.Path = path;
            }

            await ConfigService.SaveAsync(config);
            Console.WriteLine($"Project saved in workspace '{workspace.Name}': {name} -> {path}");
        }

        public static async Task ListAsync(string? workspaceName)
        {
            var config = await ConfigService.LoadAsync();
            var workspace = ResolveWorkspace(config, workspaceName);

            if (workspace.Projects.Count == 0)
            {
                Console.WriteLine($"No projects configured in workspace '{workspace.Name}'.");
                return;
            }

            Console.WriteLine($"Workspace: {workspace.Name}");
            foreach (var project in workspace.Projects)
            {
                Console.WriteLine($"{project.Name}\t{project.Path}");
            }
        }

        public static async Task RemoveAsync(string name, string? workspaceName)
        {
            var config = await ConfigService.LoadAsync();
            var workspace = ResolveWorkspace(config, workspaceName);

            var removed = workspace.Projects.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                Console.WriteLine($"Project not found in workspace '{workspace.Name}': {name}");
                return;
            }

            await ConfigService.SaveAsync(config);
            Console.WriteLine($"Project removed from workspace '{workspace.Name}': {name}");
        }

        private static AiMemoryWorkspaceConfig ResolveWorkspace(AiMemoryConfig config, string? workspaceName)
        {
            if (!string.IsNullOrWhiteSpace(workspaceName))
            {
                return ConfigService.GetOrCreateWorkspace(config, workspaceName);
            }

            return ConfigService.GetWorkspace(config)
                ?? ConfigService.GetOrCreateWorkspace(config, config.ActiveWorkspace);
        }

        private static string Prompt(string label, string? defaultValue = null)
        {
            Console.Write(defaultValue is null ? $"{label}: " : $"{label} [{defaultValue}]: ");
            var value = Console.ReadLine();
            return string.IsNullOrWhiteSpace(value) ? defaultValue ?? "" : value.Trim();
        }
    }
}
