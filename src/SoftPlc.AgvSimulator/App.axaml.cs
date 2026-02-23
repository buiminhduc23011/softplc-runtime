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
using System.Diagnostics;
using System.Net.Sockets;
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

            // Ensure firewall rule exists before starting
            EnsureFirewallRule();

            var rc = S7.Start();
            if (rc != 0)
            {
                Log.Warning("[AGV] S7Server error code={Code}", rc);
            }
            else
            {
                Log.Information("[AGV] S7Server listening on port 102");
                // Loopback self-test: verify TCP port 102 is reachable
                VerifyS7Port();
            }
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

    // ── Firewall helper ─────────────────────────────────────────────────────
    /// <summary>
    /// Creates a Windows Firewall inbound rule for TCP port 102 (S7comm).
    /// Runs silently – if not elevated, it logs a warning with manual instructions.
    /// </summary>
    private static void EnsureFirewallRule()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // Check if rule already exists
            var check = RunNetsh("advfirewall firewall show rule name=\"SoftPLC S7 Port 102\"");
            if (check.Contains("SoftPLC S7 Port 102", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("[AGV] Firewall rule 'SoftPLC S7 Port 102' already exists");
                return;
            }

            // Try to create the rule (requires elevation)
            var result = RunNetsh(
                "advfirewall firewall add rule " +
                "name=\"SoftPLC S7 Port 102\" " +
                "dir=in action=allow protocol=TCP localport=102 " +
                "profile=any enable=yes");

            if (result.Contains("Ok", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[AGV] Firewall rule created: allow TCP inbound port 102");
            }
            else
            {
                Log.Warning("[AGV] Could not create firewall rule (need admin). " +
                    "Run manually: netsh advfirewall firewall add rule " +
                    "name=\"SoftPLC S7 Port 102\" dir=in action=allow protocol=TCP localport=102");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AGV] Firewall check failed. " +
                "Ensure TCP port 102 inbound is allowed in Windows Firewall.");
        }
    }

    private static string RunNetsh(string args)
    {
        var psi = new ProcessStartInfo("netsh", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return string.Empty;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }

    // ── Loopback self-test ──────────────────────────────────────────────────
    /// <summary>
    /// Verify TCP port 102 is actually listening by doing a quick loopback connect.
    /// </summary>
    private static void VerifyS7Port()
    {
        Task.Run(async () =>
        {
            // Small delay to let the server's TCP listener fully bind
            await Task.Delay(500);
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", 102).WaitAsync(TimeSpan.FromSeconds(3));
                Log.Information("[AGV] S7 loopback test OK – TCP port 102 is reachable");
            }
            catch (Exception ex)
            {
                Log.Error("[AGV] S7 loopback test FAILED – TCP port 102 not reachable: {Msg}. " +
                    "The S7 server may not be binding correctly.", ex.Message);
            }
        });
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
