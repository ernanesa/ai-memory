using AiMemory.Configuration;
using Npgsql;

namespace AiMemory.Commands;

public static class DoctorCommand
{
    public static async Task RunAsync()
    {
        var config = await ConfigService.LoadAsync();

        Console.WriteLine("AI Memory Doctor");
        Console.WriteLine();

        Print(ConfigService.Exists(), $"Configuration file: {ConfigService.ConfigPath}", "Configuration file not found. Run ai-memory setup.");
        Print(config.Workspaces.Count > 0, $"Configured workspaces: {config.Workspaces.Count}", "No workspaces configured. Run ai-memory setup.");
        Print(!string.IsNullOrWhiteSpace(config.ActiveWorkspace), $"Active workspace: {config.ActiveWorkspace}", "Active workspace is empty.");

        var totalProjects = config.Workspaces.Sum(w => w.Projects.Count);
        Print(totalProjects > 0, $"Configured projects: {totalProjects}", "No projects configured. Run ai-memory project add.");

        foreach (var workspace in config.Workspaces)
        {
            foreach (var project in workspace.Projects)
            {
                Print(Directory.Exists(project.Path), $"Project {workspace.Name}/{project.Name}: {project.Path}", $"Project {workspace.Name}/{project.Name} path does not exist: {project.Path}");
            }
        }

        await CheckPostgresAsync(config);
        await CheckOllamaAsync(config);
    }

    private static async Task CheckPostgresAsync(AiMemoryConfig config)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConfigService.ResolveConnectionString(config));
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            Print(true, "PostgreSQL connection ok", "");
        }
        catch (Exception ex)
        {
            Print(false, "", $"PostgreSQL connection failed: {ex.Message}");
        }
    }

    private static async Task CheckOllamaAsync(AiMemoryConfig config)
    {
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false })
            {
                BaseAddress = new Uri(ConfigService.ResolveOllamaBaseUrl(config).TrimEnd('/') + "/")
            };
            using var response = await http.GetAsync("api/version");
            Print(response.IsSuccessStatusCode, "Ollama connection ok", $"Ollama returned HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Print(false, "", $"Ollama connection failed: {ex.Message}");
        }
    }

    private static void Print(bool ok, string success, string failure)
    {
        Console.WriteLine(ok ? $"[ok] {success}" : $"[fail] {failure}");
    }
}
