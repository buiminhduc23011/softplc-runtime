using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SoftPlc.AgvSimulator.Models;
using SoftPlc.AgvSimulator.Views;
using SoftPlc.ControlApi;
using SoftPlc.Core;
using SoftPlc.S7Server;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SoftPlc.AgvSimulator;

public class App : Application
{
    // Shared services – accessible by MainWindow
    public static PlcEngine  Engine   { get; private set; } = null!;
    public static S7ServerLayer? S7   { get; private set; }

    private CancellationTokenSource _cts = new();
    private WebApplication? _webApp;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        StartPlcInfrastructure();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.Exit += OnAppExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ── Boot PLC + S7 + REST API ────────────────────────────────────────────
    private void StartPlcInfrastructure()
    {
        // 1. PLC Engine
        Engine = new PlcEngine { ScanTimeMs = 10, OverrunWarningMs = 20 };
        Engine.ScanOverrun += (_, us) => Log.Warning("[AGV] Scan overrun {Us}µs", us);
        Engine.SetProgram(_ => { /* user logic placeholder */ });

        // 2. S7 Server
        try
        {
            S7 = new S7ServerLayer(Engine.Memory, maxClients: 3);
            S7.RegisterDataBlock(Db172Map.DbNumber, Db172Map.DbSize);
            var rc = S7.Start();
            if (rc != 0) Log.Warning("[AGV] S7Server error code={Code}", rc);
            else         Log.Information("[AGV] S7Server listening on port 102");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AGV] S7Server unavailable (snap7.dll?)");
            S7?.Dispose();
            S7 = null;
        }

        // 3. ASP.NET Core REST API in background thread
        Task.Run(async () =>
        {
            try
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://0.0.0.0:15000");
                builder.Services.AddSingleton(Engine);
                if (S7 is not null) builder.Services.AddSingleton(S7);

                _webApp = builder.Build();
                _webApp.MapControlApiRoutes();

                Log.Information("[AGV] REST API on http://0.0.0.0:15000");
                await _webApp.RunAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error(ex, "[AGV] Web host error"); }
        });

        // 4. Start scan loop
        Engine.Start();
        Log.Information("[AGV] PLC Engine started (scan={Ms}ms)", Engine.ScanTimeMs);
    }

    private void OnAppExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Log.Information("[AGV] Shutting down…");
        _cts.Cancel();
        _webApp?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        Engine.StopAsync().GetAwaiter().GetResult();
        S7?.Dispose();
        Engine.Dispose();
        Log.CloseAndFlush();
    }
}
