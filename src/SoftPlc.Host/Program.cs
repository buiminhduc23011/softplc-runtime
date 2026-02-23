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

    // ── 1. Create PLC engine ──────────────────────────────────────────────────
    var engine = new PlcEngine { ScanTimeMs = 10, OverrunWarningMs = 20 };
    engine.ScanOverrun += (_, us) => Log.Warning("[Host] Scan overrun: {us}µs", us);

    // ── Sample user logic: I0.0 → Q0.0,  rising edge I0.1 → increment M10 ────
    engine.SetProgram(memory =>
    {
        memory.SetOutputBit(0, 0, memory.GetInputBit(0, 0));

        if (memory.GetInputBit(0, 1))
            memory.SetMerkerByte(10, (byte)((memory.GetMerkerByte(10) + 1) % 256));
    });

    // ── 2. Create S7 Server layer BEFORE building the web app ─────────────────
    //       so we can register it in DI.
    S7ServerLayer? s7Layer = null;
    try
    {
        s7Layer = new S7ServerLayer(engine.Memory, maxClients: 3);

        // ── Pre-registered Data Blocks ────────────────────────────────────────
        // Add every DB your S7 clients need to access.
        // Syntax: RegisterDataBlock(<DB number>, <size in bytes>)
        s7Layer.RegisterDataBlock(1,   512);   // DB1   – general purpose
        s7Layer.RegisterDataBlock(2,   128);   // DB2   – general purpose
        s7Layer.RegisterDataBlock(172, 256);   // DB172 – user-defined
        // ─────────────────────────────────────────────────────────────────────

        var s7Result = s7Layer.Start();
        if (s7Result != 0)
            Log.Warning("[Host] S7Server start error code={Code} – no S7 network", s7Result);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "[Host] S7Server init failed (snap7.dll missing?) – no S7 network");
        s7Layer?.Dispose();
        s7Layer = null;
    }

    // ── 3. Build WebApplication ───────────────────────────────────────────────
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://0.0.0.0:15000");

    builder.Services.AddSingleton(engine);
    // Register S7ServerLayer (may be null if snap7 unavailable)
    if (s7Layer is not null)
        builder.Services.AddSingleton(s7Layer);

    var app = builder.Build();
    app.MapControlApiRoutes();

    // ── 4. Start PLC scan loop ────────────────────────────────────────────────
    engine.Start();

    // ── 5. Graceful shutdown ──────────────────────────────────────────────────
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Log.Information("[Host] Shutdown signal received…");
        cts.Cancel();
    };

    Log.Information("[Host] PLC running  │  ControlApi: http://0.0.0.0:15000  │  S7: port 102");
    Log.Information("[Host] Pre-registered DBs: DB1(512B), DB2(128B), DB172(256B)");
    Log.Information("[Host] Press Ctrl+C to stop.");

    await app.RunAsync(cts.Token);

    // ── 6. Teardown ───────────────────────────────────────────────────────────
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
