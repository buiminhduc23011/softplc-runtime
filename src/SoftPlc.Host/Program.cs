using Serilog;
using Serilog.Events;
using SoftPlc.ControlApi;
using SoftPlc.Core;
using SoftPlc.S7Server;

// ── Serilog bootstrap ────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/softplc-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("╔══════════════════════════════════════╗");
    Log.Information("║        Soft PLC S7-Compatible        ║");
    Log.Information("╚══════════════════════════════════════╝");

    // ── Build WebApplication for ControlApi ──────────────────────────────────
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://0.0.0.0:15000");

    // Register PlcEngine as singleton (shared across ControlApi endpoints)
    var engine = new PlcEngine { ScanTimeMs = 10, OverrunWarningMs = 20 };
    builder.Services.AddSingleton(engine);

    // Wire scan-overrun event
    engine.ScanOverrun += (_, us) =>
        Log.Warning("[Host] Scan overrun detected: {us}µs", us);

    // ── Sample user logic: I0.0 mirrors to Q0.0, I0.1 increments M10 counter ─
    engine.SetProgram(memory =>
    {
        // Mirror input bit to output bit
        var i0 = memory.GetInputBit(0, 0);
        memory.SetOutputBit(0, 0, i0);

        var i1 = memory.GetInputBit(0, 1);
        if (i1)
        {
            var cnt = memory.GetMerkerByte(10);
            memory.SetMerkerByte(10, (byte)((cnt + 1) % 256));
        }
    });

    // ── Build app ─────────────────────────────────────────────────────────────
    var app = builder.Build();
    app.MapControlApiRoutes();

    // ── S7 Server layer ───────────────────────────────────────────────────────
    S7ServerLayer? s7Layer = null;
    try
    {
        s7Layer = new S7ServerLayer(engine.Memory, maxClients: 3);
        s7Layer.RegisterDataBlock(1, 512);
        s7Layer.RegisterDataBlock(2, 128);
        var s7Result = s7Layer.Start();
        if (s7Result != 0)
            Log.Warning("[Host] S7Server start returned error code {Code} – continuing without S7 network", s7Result);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "[Host] S7Server layer failed to initialise (snap7.dll missing?) – continuing without S7");
        s7Layer?.Dispose();
        s7Layer = null;
    }

    // ── Start PLC scan loop ───────────────────────────────────────────────────
    engine.Start();

    // ── Graceful shutdown ─────────────────────────────────────────────────────
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Log.Information("[Host] Shutdown signal received…");
        cts.Cancel();
    };

    Log.Information("[Host] PLC running. ControlApi: http://0.0.0.0:15000  |  S7 port: 102");
    Log.Information("[Host] Press Ctrl+C to stop.");

    // Run web app (blocks until cancellation)
    await app.RunAsync(cts.Token);

    // ── Teardown ──────────────────────────────────────────────────────────────
    await engine.StopAsync();
    s7Layer?.Dispose();
    engine.Dispose();
    Log.Information("[Host] Graceful shutdown complete.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Host] Unhandled startup exception");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
