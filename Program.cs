using System.CommandLine;
using System.CommandLine.Invocation;
using AiMemory.Commands;
using Avalonia;

var root = new RootCommand("AI Memory Tool - local engineering memory with Ollama + PostgreSQL/pgvector. Install/update/remove the NuGet tool with dotnet tool; manage tray autostart with ai-memory tray setup/install/update/remove.");

var dbOption = new Option<string?>("--db", "PostgreSQL database name or connection string override");
var ollamaOption = new Option<string?>("--ollama", "Ollama base URL override");
var modelOption = new Option<string?>("--model", "Ollama embedding model override");
var semanticOption = new Option<bool>("--semantic", "Use semantic extraction for rules/knowledge instead of only fast heuristics");
var semanticModelOption = new Option<string?>("--semantic-model", "Ollama model used by semantic extraction");
var refreshOption = new Option<bool>("--refresh", "Reprocess all matching rules/knowledge chunks regardless of extraction state");
var workspaceOption = new Option<string?>("--workspace", "Workspace name override");
var projectFilterOption = new Option<string?>("--project", "Project filter");
var portOption = new Option<int>("--port", () => 5050, "Dashboard HTTP port");
var candidateLimitOption = new Option<int?>("--candidate-limit", "Max chunks considered by rules/knowledge extraction stages");
var parallelismOption = new Option<int?>("--parallelism", "Max concurrent chunk embedding/upsert operations during chunk indexing");

var index = new Command("index", "Index memory stages for configured projects");
var indexStagesArg = new Argument<string[]>("stages", () => [], "Optional stages: chunks, rules, knowledge");
index.AddArgument(indexStagesArg);
index.AddOption(dbOption);
index.AddOption(ollamaOption);
index.AddOption(modelOption);
index.AddOption(semanticOption);
index.AddOption(semanticModelOption);
index.AddOption(refreshOption);
index.AddOption(workspaceOption);
index.AddOption(projectFilterOption);
index.AddOption(candidateLimitOption);
index.AddOption(parallelismOption);
index.SetHandler(async (InvocationContext context) =>
{
    var parseResult = context.ParseResult;
    await IndexCommand.RunAsync(
        parseResult.GetValueForArgument(indexStagesArg),
        parseResult.GetValueForOption(projectFilterOption),
        parseResult.GetValueForOption(workspaceOption),
        parseResult.GetValueForOption(dbOption),
        parseResult.GetValueForOption(ollamaOption),
        parseResult.GetValueForOption(modelOption),
        parseResult.GetValueForOption(semanticOption),
        parseResult.GetValueForOption(semanticModelOption),
        parseResult.GetValueForOption(refreshOption),
        parseResult.GetValueForOption(candidateLimitOption),
        parseResult.GetValueForOption(parallelismOption));
});

var search = new Command("search", "Search engineering memory");
var queryArg = new Argument<string>("query", "Search query");
var limitOption = new Option<int>("--limit", () => 10, "Max results");
search.AddArgument(queryArg);
search.AddOption(limitOption);
search.AddOption(dbOption);
search.AddOption(ollamaOption);
search.AddOption(modelOption);
search.SetHandler(async (query, limit, db, ollama, model) => await SearchCommand.RunAsync(query, limit, db, ollama, model), queryArg, limitOption, dbOption, ollamaOption, modelOption);

var watch = new Command("watch", "Watch configured projects and reindex changed files");
watch.AddOption(dbOption);
watch.AddOption(ollamaOption);
watch.AddOption(modelOption);
watch.SetHandler(async (db, ollama, model) => await WatchCommand.RunAsync(db, ollama, model), dbOption, ollamaOption, modelOption);

var doctor = new Command("doctor", "Validate local environment, schema, tray and configuration");
var doctorJsonOption = new Option<bool>("--json", "Write machine-readable JSON diagnostics");
var doctorStrictOption = new Option<bool>("--strict", "Treat warnings as failures");
var doctorNoNetworkOption = new Option<bool>("--no-network", "Skip network checks such as Ollama reachability");
doctor.AddOption(doctorJsonOption);
doctor.AddOption(doctorStrictOption);
doctor.AddOption(doctorNoNetworkOption);
doctor.SetHandler(async (InvocationContext context) =>
{
    var parseResult = context.ParseResult;
    context.ExitCode = await DoctorCommand.RunAsync(
        parseResult.GetValueForOption(doctorJsonOption),
        parseResult.GetValueForOption(doctorStrictOption),
        parseResult.GetValueForOption(doctorNoNetworkOption));
});

var setup = new Command("setup", "Interactive first-time configuration wizard");
setup.SetHandler(async () => await SetupCommand.RunAsync());

var project = new Command("project", "Manage configured projects");

var projectAdd = new Command("add", "Add a project to configuration");
projectAdd.AddOption(workspaceOption);
projectAdd.SetHandler(async (workspace) => await ProjectCommand.AddAsync(workspace), workspaceOption);

var projectList = new Command("list", "List configured projects");
projectList.AddOption(workspaceOption);
projectList.SetHandler(async (workspace) => await ProjectCommand.ListAsync(workspace), workspaceOption);

var projectRemove = new Command("remove", "Remove a configured project");
var projectNameArg = new Argument<string>("name", "Project name");
projectRemove.AddArgument(projectNameArg);
projectRemove.AddOption(workspaceOption);
projectRemove.SetHandler(async (name, workspace) => await ProjectCommand.RemoveAsync(name, workspace), projectNameArg, workspaceOption);

project.AddCommand(projectAdd);
project.AddCommand(projectList);
project.AddCommand(projectRemove);

var workspace = new Command("workspace", "Manage workspaces");

var workspaceAdd = new Command("add", "Create a workspace and make it active");
var workspaceNameArg = new Argument<string>("name", "Workspace name");
workspaceAdd.AddArgument(workspaceNameArg);
workspaceAdd.SetHandler(async (name) => await WorkspaceCommand.AddAsync(name), workspaceNameArg);

var workspaceList = new Command("list", "List workspaces");
workspaceList.SetHandler(async () => await WorkspaceCommand.ListAsync());

var workspaceUse = new Command("use", "Select the active workspace");
var workspaceUseNameArg = new Argument<string>("name", "Workspace name");
workspaceUse.AddArgument(workspaceUseNameArg);
workspaceUse.SetHandler(async (name) => await WorkspaceCommand.UseAsync(name), workspaceUseNameArg);

var workspaceRemove = new Command("remove", "Remove a workspace");
var workspaceRemoveNameArg = new Argument<string>("name", "Workspace name");
workspaceRemove.AddArgument(workspaceRemoveNameArg);
workspaceRemove.SetHandler(async (name) => await WorkspaceCommand.RemoveAsync(name), workspaceRemoveNameArg);

workspace.AddCommand(workspaceAdd);
workspace.AddCommand(workspaceList);
workspace.AddCommand(workspaceUse);
workspace.AddCommand(workspaceRemove);

var mcp = new Command("mcp", "Start MCP server over stdio");
mcp.AddOption(dbOption);
mcp.AddOption(ollamaOption);
mcp.AddOption(modelOption);
mcp.SetHandler(async (db, ollama, model) => await McpCommand.RunAsync(db, ollama, model), dbOption, ollamaOption, modelOption);

var dashboard = new Command("dashboard", "Show memory dashboard summary");
dashboard.AddOption(workspaceOption);
dashboard.AddOption(projectFilterOption);
dashboard.AddOption(dbOption);
dashboard.SetHandler(async (workspace, project, db) => await DashboardCommand.RunAsync(workspace, project, db), workspaceOption, projectFilterOption, dbOption);

var dashboardServe = new Command("serve", "Start local web dashboard");
dashboardServe.AddOption(workspaceOption);
dashboardServe.AddOption(projectFilterOption);
dashboardServe.AddOption(dbOption);
dashboardServe.AddOption(portOption);
dashboardServe.SetHandler(async (workspace, project, db, port) => await DashboardCommand.ServeAsync(workspace, project, db, port), workspaceOption, projectFilterOption, dbOption, portOption);
dashboard.AddCommand(dashboardServe);

root.AddCommand(index);
root.AddCommand(search);
root.AddCommand(watch);
root.AddCommand(doctor);
root.AddCommand(setup);
var tray = new Command("tray", "Start or manage the system tray monitor application");

var trayInstall = new Command("install", "Install the tray autostart entry and start the tray when supported");
trayInstall.SetHandler(async (InvocationContext context) =>
{
    Console.WriteLine("Installing system tray application...");
    var success = await AiMemory.Services.TraySetupService.InstallAsync();
    if (success) Console.WriteLine("System tray application installed successfully.");
    else
    {
        Console.WriteLine("Failed to install system tray application.");
        context.ExitCode = 1;
    }
});

var trayUninstall = new Command("uninstall", "Remove the tray autostart entry and stop the tray process");
trayUninstall.AddAlias("remove");
trayUninstall.SetHandler(async (InvocationContext context) =>
{
    Console.WriteLine("Uninstalling system tray application...");
    var success = await AiMemory.Services.TraySetupService.UninstallAsync();
    if (success) Console.WriteLine("System tray application uninstalled successfully.");
    else
    {
        Console.WriteLine("Failed to uninstall system tray application.");
        context.ExitCode = 1;
    }
});

var trayStatus = new Command("status", "Show current system tray status");
trayStatus.SetHandler(async () =>
{
    var status = await AiMemory.Services.TraySetupService.GetStatusAsync();
    Console.WriteLine($"Tray Installed: {status.Installed}");
    Console.WriteLine($"Tray Running: {status.Running}");
    Console.WriteLine($"Autostart Path: {status.AutostartPath}");
    Console.WriteLine($"Executable Path: {status.ExecutablePath}");
});

var trayUpdate = new Command("update", "Recreate tray autostart after a dotnet tool update or path change");
trayUpdate.SetHandler(async (InvocationContext context) =>
{
    Console.WriteLine("Updating system tray application...");
    var removed = await AiMemory.Services.TraySetupService.UninstallAsync();
    if (!removed)
    {
        Console.WriteLine("Failed to remove the previous system tray startup registration.");
        context.ExitCode = 1;
        return;
    }

    var success = await AiMemory.Services.TraySetupService.InstallAsync();
    if (success) Console.WriteLine("System tray application updated successfully.");
    else
    {
        Console.WriteLine("Failed to update system tray application.");
        context.ExitCode = 1;
    }
});

var traySetup = new Command("setup", "Interactive wizard to install, update or remove the system tray application");
traySetup.SetHandler(async (InvocationContext context) =>
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔════════════════════════════════════════╗");
    Console.WriteLine("║   AI Memory — System Tray Setup        ║");
    Console.WriteLine("╚════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();

    Console.Write("Checking tray status... ");
    var status = await AiMemory.Services.TraySetupService.GetStatusAsync();
    Console.WriteLine();

    Console.WriteLine($"  Autostart  : {(status.Installed ? $"✓ Installed  →  {status.AutostartPath}" : "✗ Not installed")}");
    Console.WriteLine($"  Running    : {(status.Running ? "✓ Running" : "✗ Stopped")}");
    Console.WriteLine($"  Executable : {status.ExecutablePath}");
    Console.WriteLine();

    if (!status.Installed)
    {
        Console.WriteLine("  [1] Install — add to system startup and start now");
        Console.WriteLine("  [2] Cancel");
    }
    else
    {
        Console.WriteLine("  [1] Update  — re-link autostart to the current executable");
        Console.WriteLine("  [2] Remove  — uninstall from startup and stop the tray");
        Console.WriteLine("  [3] Cancel");
    }

    Console.Write("\nYour choice: ");
    var choice = (Console.ReadLine() ?? "").Trim();
    Console.WriteLine();

    if (!status.Installed && choice == "1")
    {
        Console.WriteLine("Installing system tray application...");
        var ok = await AiMemory.Services.TraySetupService.InstallAsync();
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(ok ? "✓ Tray installed and started successfully." : "✗ Installation failed. Check logs for details.");
        Console.ResetColor();
        if (!ok) context.ExitCode = 1;
    }
    else if (status.Installed && choice == "1")
    {
        Console.WriteLine("Updating system tray application...");
        var removed = await AiMemory.Services.TraySetupService.UninstallAsync();
        var ok = removed && await AiMemory.Services.TraySetupService.InstallAsync();
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(ok ? "✓ Tray updated successfully." : "✗ Update failed. Check logs for details.");
        Console.ResetColor();
        if (!ok) context.ExitCode = 1;
    }
    else if (status.Installed && choice == "2")
    {
        Console.WriteLine("Removing system tray application...");
        var ok = await AiMemory.Services.TraySetupService.UninstallAsync();
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(ok ? "✓ Tray removed from startup." : "✗ Removal failed. Check logs for details.");
        Console.ResetColor();
        if (!ok) context.ExitCode = 1;
    }
    else
    {
        Console.WriteLine("Cancelled.");
    }
});

tray.AddCommand(trayInstall);
tray.AddCommand(trayUninstall);
tray.AddCommand(trayUpdate);
tray.AddCommand(trayStatus);
tray.AddCommand(traySetup);

tray.SetHandler((InvocationContext context) =>
{
    try
    {
        AiMemory.Services.TraySetupService.RegisterCurrentTrayProcess();
        AppBuilder.Configure<AiMemory.Tray.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(
                context.ParseResult.Tokens.Select(t => t.Value).ToArray());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error starting tray: {ex.Message}");
        context.ExitCode = 1;
    }
});

root.AddCommand(workspace);
root.AddCommand(project);
root.AddCommand(mcp);
root.AddCommand(dashboard);
root.AddCommand(tray);

return await root.InvokeAsync(args);
