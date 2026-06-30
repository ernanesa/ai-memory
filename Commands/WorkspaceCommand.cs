using AiMemory.Configuration;

namespace AiMemory.Commands
{
    public static class WorkspaceCommand
    {
        public static async Task AddAsync(string name)
        {
            var config = await ConfigService.LoadAsync();
            var workspace = ConfigService.GetOrCreateWorkspace(config, name);
            config.ActiveWorkspace = workspace.Name;
            await ConfigService.SaveAsync(config);
            Console.WriteLine($"Workspace saved and selected: {workspace.Name}");
        }

        public static async Task ListAsync()
        {
            var config = await ConfigService.LoadAsync();
            if (config.Workspaces.Count == 0)
            {
                Console.WriteLine("No workspaces configured.");
                return;
            }

            foreach (var workspace in config.Workspaces)
            {
                var marker = workspace.Name.Equals(config.ActiveWorkspace, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                Console.WriteLine($"{marker} {workspace.Name}\t{workspace.Projects.Count} project(s)");
            }
        }

        public static async Task UseAsync(string name)
        {
            var config = await ConfigService.LoadAsync();
            var workspace = ConfigService.GetWorkspace(config, name);
            if (workspace is null)
            {
                Console.WriteLine($"Workspace not found: {name}");
                return;
            }

            config.ActiveWorkspace = workspace.Name;
            await ConfigService.SaveAsync(config);
            Console.WriteLine($"Active workspace: {workspace.Name}");
        }

        public static async Task RemoveAsync(string name)
        {
            var config = await ConfigService.LoadAsync();
            var removed = config.Workspaces.RemoveAll(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                Console.WriteLine($"Workspace not found: {name}");
                return;
            }

            if (config.ActiveWorkspace.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                config.ActiveWorkspace = config.Workspaces.FirstOrDefault()?.Name ?? "Default";
            }

            await ConfigService.SaveAsync(config);
            Console.WriteLine($"Workspace removed: {name}");
        }
    }
}
