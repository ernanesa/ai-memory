using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Npgsql;
using AiMemory.Configuration;

namespace AiMemory.Tray
{
    public partial class App : Application
    {
        private DispatcherTimer? _timer;
        private WindowIcon? _activeIcon;
        private WindowIcon? _idleIcon;
        private TrayIcon? _trayIcon;
        private bool _isChecking;
        private readonly List<NativeMenuItemBase> _dynamicMenuItems = new();
        private FileSystemWatcher? _configWatcher;
        private readonly SemaphoreSlim _menuSemaphore = new(1, 1);

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Omitir janela principal ativa
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // Carregar bitmaps dos assets de ícones
                try
                {
                    using var activeStream = AssetLoader.Open(new Uri("avares://ai-memory/Tray/Assets/active.png"));
                    _activeIcon = new WindowIcon(activeStream);

                    using var idleStream = AssetLoader.Open(new Uri("avares://ai-memory/Tray/Assets/idle.png"));
                    _idleIcon = new WindowIcon(idleStream);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Falha ao carregar ícones: {ex.Message}");
                }

                // Capturar instância do TrayIcon
                _trayIcon = TrayIcon.GetIcons(this)?.FirstOrDefault();
                if (_trayIcon is not null)
                {
                    _trayIcon.IsVisible = true;
                    if (_idleIcon is not null)
                    {
                        _trayIcon.Icon = _idleIcon;
                    }
                }

                // Iniciar o timer de monitoramento a cada 4 segundos
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(4)
                };
                _timer.Tick += OnTimerTick;
                _timer.Start();

                // Executa a primeira validação imediatamente
                OnTimerTick(null, EventArgs.Empty);

                // Carregar projetos no menu
                _ = RefreshProjectsMenuAsync();

                // Configurar FileSystemWatcher para monitorar o config.json
                SetupConfigWatcher();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (_trayIcon is null || _isChecking) return;

            // Garantir visibilidade contínua
            _trayIcon.IsVisible = true;

            _isChecking = true;
            try
            {
                var isActive = await Task.Run(IsMcpProcessActive);

                if (isActive)
                {
                    if (_activeIcon is not null) _trayIcon.Icon = _activeIcon;
                    _trayIcon.ToolTipText = "AI Memory: Ativo (Servidor MCP em execução)";
                }
                else
                {
                    if (_idleIcon is not null) _trayIcon.Icon = _idleIcon;
                    _trayIcon.ToolTipText = "AI Memory: Idle (Nenhuma IDE conectada)";
                }
            }
            finally
            {
                _isChecking = false;
            }
        }

        private static bool IsMcpProcessActive()
        {
            try
            {
                var currentPid = Environment.ProcessId;
                var processes = Process.GetProcesses();
                try
                {
                    foreach (var p in processes)
                    {
                        if (p.Id == currentPid) continue;

                        string procName;
                        try
                        {
                            procName = p.ProcessName.ToLowerInvariant();
                        }
                        catch
                        {
                            continue;
                        }

                        // Se for ai-memory ou dotnet
                        if (procName.Contains("ai-memory") || procName == "dotnet")
                        {
                            try
                            {
                                if (OperatingSystem.IsLinux())
                                {
                                    var cmdlinePath = $"/proc/{p.Id}/cmdline";
                                    if (File.Exists(cmdlinePath))
                                    {
                                        var cmdline = File.ReadAllText(cmdlinePath);
                                        // Se o comando contém "ai-memory" e "mcp", e não contém "tray"
                                        if (cmdline.Contains("ai-memory") && cmdline.Contains("mcp") && !cmdline.Contains("tray"))
                                        {
                                            return true;
                                        }
                                    }
                                }
                                else
                                {
                                    // No Windows/macOS, o executável seria ai-memory.exe ou ai-memory
                                    if (procName.Contains("ai-memory"))
                                    {
                                        return true;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignorar falhas de permissão ou saída rápida
                            }
                        }
                    }
                }
                finally
                {
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
                // Ignorar falhas
            }

            return false;
        }

        private async Task RefreshProjectsMenuAsync()
        {
            await _menuSemaphore.WaitAsync();
            try
            {
                if (_trayIcon?.Menu is not { } menu) return;

                // Remover itens dinâmicos anteriores
                foreach (var item in _dynamicMenuItems)
                {
                    menu.Items.Remove(item);
                }
                _dynamicMenuItems.Clear();

                try
                {
                    var config = await Task.Run(() => ConfigService.LoadAsync());
                    var workspace = ConfigService.GetWorkspace(config);
                    var projects = workspace?.Projects ?? [];

                    // Tentar obter contagem de chunks indexados por projeto
                    Dictionary<string, int> chunkCounts = new(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var connStr = ConfigService.ResolveConnectionString(config);
                        chunkCounts = await Task.Run(() => GetIndexedChunkCounts(connStr));
                    }
                    catch
                    {
                        // Banco indisponível — exibir projetos sem contagem
                    }

                    // Submenu de projetos indexados
                    var workspaceName = workspace?.Name ?? config.ActiveWorkspace;
                    var submenuParent = new NativeMenuItem($"Projetos — {workspaceName}");
                    var submenu = new NativeMenu();

                    if (projects.Count > 0)
                    {
                        foreach (var project in projects)
                        {
                            var p = project;
                            var label = chunkCounts.TryGetValue(p.Name, out var count)
                                ? $"{p.Name}  ({count} chunks)"
                                : p.Name;

                            var item = new NativeMenuItem(label);
                            item.Click += (_, _) => OpenDirectory(p.Path);
                            submenu.Items.Add(item);
                        }
                    }
                    else
                    {
                        submenu.Items.Add(new NativeMenuItem("Nenhum projeto configurado") { IsEnabled = false });
                    }

                    submenuParent.Menu = submenu;
                    InsertDynamic(menu, 0, submenuParent);

                    // Submenu de gerenciamento de workspaces
                    var allWorkspaces = config.Workspaces;
                    var activeWs = config.ActiveWorkspace;
                    var wsParent = new NativeMenuItem($"Workspace: {activeWs}");
                    var wsMenu = new NativeMenu();

                    foreach (var ws in allWorkspaces)
                    {
                        var wsName = ws.Name;
                        var isActive = wsName.Equals(activeWs, StringComparison.OrdinalIgnoreCase);
                        var wsItem = new NativeMenuItem(isActive ? $"✓ {wsName}" : $"  {wsName}");
                        if (!isActive)
                        {
                            wsItem.Click += (_, _) => SwitchWorkspace(wsName);
                        }
                        else
                        {
                            wsItem.IsEnabled = false;
                        }
                        wsMenu.Items.Add(wsItem);
                    }

                    wsParent.Menu = wsMenu;
                    InsertDynamic(menu, 1, wsParent);
                    InsertDynamic(menu, 2, new NativeMenuItemSeparator());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Falha ao carregar projetos no menu: {ex.Message}");
                }
            }
            finally
            {
                _menuSemaphore.Release();
            }
        }

        private void InsertDynamic(NativeMenu menu, int index, NativeMenuItemBase item)
        {
            menu.Items.Insert(index, item);
            _dynamicMenuItems.Add(item);
        }

        private void SwitchWorkspace(string workspaceName)
        {
            Task.Run(async () =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ai-memory",
                        Arguments = $"workspace use \"{workspaceName}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process is not null)
                    {
                        var stdoutTask = process.StandardOutput.ReadToEndAsync();
                        var stderrTask = process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        await stdoutTask;
                        await stderrTask;

                        if (process.ExitCode == 0)
                        {
                            ShowNotification("Workspace Alterado", $"Workspace ativo: {workspaceName}");
                            await Dispatcher.UIThread.InvokeAsync(RefreshProjectsMenuAsync);
                        }
                        else
                        {
                            ShowNotification("Falha ao Trocar", $"Não foi possível alternar para {workspaceName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowNotification("Erro", $"Falha ao trocar workspace: {ex.Message}");
                }
            });
        }

        private static Dictionary<string, int> GetIndexedChunkCounts(string connectionString)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                SELECT p.name, COUNT(c.id)
                FROM ai_projects p
                JOIN ai_chunks c ON c.project_id = p.id
                GROUP BY p.name
                ORDER BY p.name
                """;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    result[name] = count;
                }
            }
            catch
            {
                // Retorna dicionário vazio se o banco não estiver acessível
            }

            return result;
        }

        private static string ShortenPath(string path)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal))
            {
                return "~" + path[home.Length..];
            }
            return path;
        }

        private static void OpenDirectory(string path)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo("open", path) { UseShellExecute = false });
                }
                else if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer", path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Falha ao abrir diretório: {ex.Message}");
            }
        }

        public void OnIndexNowClick(object? sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "ai-memory",
                        Arguments = "index",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process is not null)
                    {
                        // Ler streams concorrentemente para evitar deadlock no pipe buffer
                        var stdoutTask = process.StandardOutput.ReadToEndAsync();
                        var stderrTask = process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        await stdoutTask;
                        var err = await stderrTask;

                        if (process.ExitCode == 0)
                        {
                            ShowNotification("Indexação Concluída", "O workspace foi reindexado com sucesso no pgvector.");
                            await Dispatcher.UIThread.InvokeAsync(RefreshProjectsMenuAsync);
                        }
                        else
                        {
                            ShowNotification("Falha na Indexação", $"O processo retornou erro: {err}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowNotification("Erro ao Indexar", $"Não foi possível iniciar o utilitário ai-memory: {ex.Message}");
                }
            });
        }

        public void OnCheckDatabaseClick(object? sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                try
                {
                    var config = await ConfigService.LoadAsync();
                    var connectionString = ConfigService.ResolveConnectionString(config);

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    ShowNotification("Banco de Dados OK", "Conexão com PostgreSQL ativa e acessível!");
                }
                catch (Exception ex)
                {
                    ShowNotification("Falha no Banco", $"Não foi possível conectar: {ex.Message}");
                }
            });
        }

        public void OnStartPostgresClick(object? sender, EventArgs e) => ManagePostgresService("start", "Serviço Iniciado", "Falha ao iniciar serviço");
        public void OnStopPostgresClick(object? sender, EventArgs e) => ManagePostgresService("stop", "Serviço Parado", "Falha ao parar serviço");
        public void OnRestartPostgresClick(object? sender, EventArgs e) => ManagePostgresService("restart", "Serviço Reiniciado", "Falha ao reiniciar serviço");

        private static void ManagePostgresService(string action, string successTitle, string failTitle)
        {
            Task.Run(async () =>
            {
                try
                {
                    ProcessStartInfo startInfo;
                    if (OperatingSystem.IsLinux())
                    {
                        // pkexec solicita privilégio gráfico no Linux
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "pkexec",
                            Arguments = $"systemctl {action} postgresql",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "brew",
                            Arguments = $"services {action} postgresql",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "net",
                            Arguments = $"{action} postgresql-x64-16", // Nome aproximado do serviço no windows
                            UseShellExecute = true, // Verbose UAC runas
                            Verb = "runas"
                        };
                    }
                    else
                    {
                        ShowNotification("Erro", "Plataforma não suportada para controle de serviço.");
                        return;
                    }

                    using var process = Process.Start(startInfo);
                    if (process is not null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode == 0)
                        {
                            ShowNotification(successTitle, "Ação executada com sucesso!");
                        }
                        else
                        {
                            ShowNotification(failTitle, $"O processo retornou código de erro: {process.ExitCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ShowNotification("Erro no Serviço", $"Falha ao executar ação: {ex.Message}");
                }
            });
        }

        public void OnCreateDatabaseClick(object? sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                try
                {
                    var config = await ConfigService.LoadAsync();
                    var targetConnectionString = ConfigService.ResolveConnectionString(config);
                    var databaseName = GetDatabaseName(config.Database) ?? "ai_memory";

                    ShowNotification("Processando...", "Criando banco de dados se necessário...");

                    // 1. Tentar criar o banco caso não exista
                    var created = await CreateDatabaseIfNeededAsync(targetConnectionString, databaseName);

                    // 2. Aplicar schema
                    if (created)
                    {
                        ShowNotification("Banco Criado", "Aplicando migrações e tabelas...");
                        var schemaApplied = await ApplySchemaAsync(targetConnectionString);
                        if (schemaApplied)
                        {
                            ShowNotification("Pronto!", "Banco de dados e tabelas criados com sucesso!");
                            await Dispatcher.UIThread.InvokeAsync(RefreshProjectsMenuAsync);
                        }
                        else
                        {
                            ShowNotification("Erro", "Banco criado, mas falhou ao aplicar tabelas.");
                        }
                    }
                    else
                    {
                        ShowNotification("Sem alterações", "O banco já está configurado ou não foi possível criá-lo.");
                    }
                }
                catch (Exception ex)
                {
                    ShowNotification("Falha ao Criar Banco", ex.Message);
                }
            });
        }

        private static async Task<bool> CreateDatabaseIfNeededAsync(string targetConnectionString, string databaseName)
        {
            try
            {
                // Testa se já consegue conectar direto
                using (var testConn = new NpgsqlConnection(targetConnectionString))
                {
                    try
                    {
                        await testConn.OpenAsync();
                        return true; // Já conecta, tudo certo!
                    }
                    catch
                    {
                        // Ignora erro de conexao e tenta criar
                    }
                }

                var adminConnectionString = BuildConnectionStringForDatabase(targetConnectionString, "postgres");
                await using var conn = new NpgsqlConnection(adminConnectionString);
                await conn.OpenAsync();

                await using var existsCmd = conn.CreateCommand();
                existsCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)";
                existsCmd.Parameters.AddWithValue("name", databaseName);
                var exists = (bool)(await existsCmd.ExecuteScalarAsync() ?? false);

                if (!exists)
                {
                    await using var createCmd = conn.CreateCommand();
                    var quotedDatabaseName = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
                    createCmd.CommandText = $"CREATE DATABASE {quotedDatabaseName}";
                    await createCmd.ExecuteNonQueryAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Falha no CreateDatabaseIfNeededAsync: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> ApplySchemaAsync(string connectionString)
        {
            var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "sql");
            if (!Directory.Exists(schemaDirectory))
            {
                return false;
            }

            try
            {
                var schemaFiles = Directory.GetFiles(schemaDirectory, "*.sql")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (schemaFiles.Length == 0)
                {
                    return false;
                }

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                foreach (var schemaFile in schemaFiles)
                {
                    var schema = await File.ReadAllTextAsync(schemaFile);
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = schema;
                    await cmd.ExecuteNonQueryAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Falha ao aplicar schema: {ex.Message}");
                return false;
            }
        }

        private static string? GetDatabaseName(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) return null;
            if (!database.Contains('=')) return database.Trim();

            try
            {
                return new NpgsqlConnectionStringBuilder(database).Database;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildConnectionStringForDatabase(string originalConnectionString, string newDatabaseName)
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(originalConnectionString)
                {
                    Database = newDatabaseName
                };
                return builder.ConnectionString;
            }
            catch
            {
                return originalConnectionString;
            }
        }

        private static void ShowNotification(string title, string text)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    // notify-send no Linux (GNOME Ubuntu / KDE BigLinux)
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "notify-send",
                        Arguments = $"\"{title}\" \"{text}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    // AppleScript no macOS
                    var appleScript = $"display notification \"{text}\" with title \"{title}\"";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e '{appleScript}'",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else if (OperatingSystem.IsWindows())
                {
                    // PowerShell BalloonTip no Windows 11
                    var psCommand = $"[void] [System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms'); " +
                                    $"$notification = New-Object System.Windows.Forms.NotifyIcon; " +
                                    $"$notification.Icon = [System.Drawing.SystemIcons]::Information; " +
                                    $"$notification.BalloonTipText = '{text.Replace("'", "''")}'; " +
                                    $"$notification.BalloonTipTitle = '{title.Replace("'", "''")}'; " +
                                    $"$notification.Visible = $true; " +
                                    $"$notification.ShowBalloonTip(5000)";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"{psCommand}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Falha ao disparar notificação do SO: {ex.Message}");
            }
        }

        private void SetupConfigWatcher()
        {
            try
            {
                var directory = ConfigService.ConfigDirectory;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _configWatcher = new FileSystemWatcher(directory)
                {
                    Filter = Path.GetFileName(ConfigService.ConfigPath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };

                _configWatcher.Changed += OnConfigChanged;
                _configWatcher.Created += OnConfigChanged;
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Falha ao configurar FileSystemWatcher: {ex.Message}");
            }
        }

        private void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            // Executar na thread de UI de forma assíncrona
            Dispatcher.UIThread.Post(async () =>
            {
                // Aguardar 250ms para garantir a liberação do arquivo pelo processo escritor
                await Task.Delay(250);
                await RefreshProjectsMenuAsync();
            });
        }

        public async void OnExitClick(object? sender, EventArgs e)
        {
            try
            {
                var isActive = await Task.Run(IsMcpProcessActive);
                if (isActive)
                {
                    ShowNotification("AI Memory Ativo", "A bandeja foi encerrada. O servidor ai-memory continuará rodando em segundo plano.");
                    await Task.Delay(500); // tempo para o notify-send ser disparado
                }

                _timer?.Stop();
                if (_configWatcher is not null)
                {
                    _configWatcher.EnableRaisingEvents = false;
                    _configWatcher.Dispose();
                }

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao sair: {ex.Message}");
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            }
        }
    }
}
