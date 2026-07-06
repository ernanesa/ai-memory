using Avalonia;

AppBuilder.Configure<AiMemory.Tray.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);