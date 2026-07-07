using System.CommandLine;
using System.CommandLine.Invocation;
using AiMemory.Commands;

var root = new RootCommand("AI Memory Tool - local engineering memory with Ollama + PostgreSQL/pgvector.");

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
setup.SetHandler(async () =>
{
    await SetupCommand.RunAsync();
    Console.WriteLine();
    Console.Write("Install AI Memory .NET 10 skills/workflows now? [Y/n]: ");
    var answer = (Console.ReadLine() ?? "").Trim();
    var accepted = string.IsNullOrWhiteSpace(answer)
                   || answer.StartsWith("y", StringComparison.OrdinalIgnoreCase);
    if (accepted)
    {
        await SkillsCommand.InstallInteractiveAsync();
    }
    else
    {
        Console.WriteLine("Skills installation skipped. Run 'ai-memory skills install' later.");
    }
});

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

var skills = new Command("skills", "List, detect and install AI Memory skills/workflows");
var skillsList = new Command("list", "List bundled skills and workflows");
skillsList.SetHandler(async () => await SkillsCommand.ListAsync());
var skillsDetect = new Command("detect", "Detect IDE/CLI install targets");
skillsDetect.SetHandler(async () => await SkillsCommand.DetectAsync());
var skillsInstall = new Command("install", "Install bundled skills and workflows");
var skillsAllOption = new Option<bool>("--all", "Install in all detected targets without prompting");
skillsInstall.AddOption(skillsAllOption);
skillsInstall.SetHandler(async (all) => await SkillsCommand.InstallAsync(all), skillsAllOption);
skills.AddCommand(skillsList);
skills.AddCommand(skillsDetect);
skills.AddCommand(skillsInstall);

var tray = new Command("tray", "Start or manage the system tray monitor application");
var trayInstall = new Command("install", "Install the tray autostart entry and start the tray when supported");
trayInstall.SetHandler(async (InvocationContext context) =>
{
    var success = await AiMemory.Services.TraySetupService.InstallAsync();
    Console.WriteLine(success ? "System tray application installed successfully." : "Failed to install system tray application.");
    if (!success) context.ExitCode = 1;
});
var trayUninstall = new Command("uninstall", "Remove the tray autostart entry and stop the tray process");
trayUninstall.AddAlias("remove");
trayUninstall.SetHandler(async (InvocationContext context) =>
{
    var success = await AiMemory.Services.TraySetupService.UninstallAsync();
    Console.WriteLine(success ? "System tray application uninstalled successfully." : "Failed to uninstall system tray application.");
    if (!success) context.ExitCode = 1;
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
    var removed = await AiMemory.Services.TraySetupService.UninstallAsync();
    var success = removed && await AiMemory.Services.TraySetupService.InstallAsync();
    Console.WriteLine(success ? "System tray application updated successfully." : "Failed to update system tray application.");
    if (!success) context.ExitCode = 1;
});
var traySetup = new Command("setup", "Interactive wizard to install, update or remove the system tray application");
traySetup.SetHandler(async () =>
{
    var status = await AiMemory.Services.TraySetupService.GetStatusAsync();
    Console.WriteLine($"Tray Installed: {status.Installed}");
    Console.WriteLine($"Tray Running: {status.Running}");
    Console.WriteLine($"Autostart Path: {status.AutostartPath}");
    Console.WriteLine($"Executable Path: {status.ExecutablePath}");
    Console.WriteLine(status.Installed ? "Run 'ai-memory tray update' or 'ai-memory tray remove'." : "Run 'ai-memory tray install'.");
});
tray.AddCommand(trayInstall);
tray.AddCommand(trayUninstall);
tray.AddCommand(trayUpdate);
tray.AddCommand(trayStatus);
tray.AddCommand(traySetup);
tray.SetHandler((InvocationContext context) =>
{
    if (AiMemory.Services.TrayLaunchService.TryStart(out var errorMessage))
    {
        Console.WriteLine("Tray application started.");
        return;
    }

    Console.Error.WriteLine($"Error starting tray: {errorMessage}. Install with: dotnet tool install -g AiMemory.Tray");
    context.ExitCode = 1;
});

root.AddCommand(index);
root.AddCommand(search);
root.AddCommand(watch);
root.AddCommand(doctor);
root.AddCommand(setup);
root.AddCommand(workspace);
root.AddCommand(project);
root.AddCommand(mcp);
root.AddCommand(dashboard);
root.AddCommand(skills);
root.AddCommand(tray);

return await root.InvokeAsync(args);
