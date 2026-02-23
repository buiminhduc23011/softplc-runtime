using System.Diagnostics;
using Serilog;

namespace SoftPlc.Core;

/// <summary>
/// Core PLC runtime engine.
/// Manages the scan cycle, CPU state, and user-logic injection.
/// </summary>
public sealed class PlcEngine : IDisposable
{
    // ── Configuration ────────────────────────────────────────────────────────
    public int ScanTimeMs { get; set; } = 10;
    public int OverrunWarningMs { get; set; } = 20;

    // ── Public state ─────────────────────────────────────────────────────────
    public PlcMemory Memory { get; } = new PlcMemory();

    private volatile CpuState _cpuState = CpuState.Stop;
    public CpuState CpuState
    {
        get => _cpuState;
        private set
        {
            if (_cpuState == value) return;
            _cpuState = value;
            Log.Information("[PlcEngine] CPU state changed to {State}", value);
            CpuStateChanged?.Invoke(this, value);
        }
    }

    // ── Scan diagnostics ─────────────────────────────────────────────────────
    public long LastScanTimeUs        { get; private set; }
    public long ScanCycleCount        { get; private set; }
    public long OverrunCount          { get; private set; }

    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler<CpuState>? CpuStateChanged;
    public event EventHandler<long>?     ScanOverrun;   // arg = actual scan µs

    // ── Logic injection ──────────────────────────────────────────────────────
    private IPlcProgram? _program;
    private Action<PlcMemory>? _programAction;
    private readonly object _programLock = new();

    // ── Scan thread ──────────────────────────────────────────────────────────
    private CancellationTokenSource? _cts;
    private Task? _scanTask;
    private bool _disposed;

    // ────────────────────────────────────────────────────────────────────────
    // ── Logic injection API ──────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public void SetProgram(IPlcProgram program)
    {
        lock (_programLock)
        {
            _program       = program;
            _programAction = null;
        }
    }

    public void SetProgram(Action<PlcMemory> action)
    {
        lock (_programLock)
        {
            _programAction = action;
            _program       = null;
        }
    }

    public void ClearProgram()
    {
        lock (_programLock) { _program = null; _programAction = null; }
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── CPU control ──────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Starts the scan loop and sets CPU state to RUN.</summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PlcEngine));

        if (_scanTask is { IsCompleted: false })
        {
            Log.Warning("[PlcEngine] Start called but scan task is already running");
            return;
        }

        _cts = new CancellationTokenSource();
        _scanTask = Task.Factory.StartNew(
            () => ScanLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        CpuState = CpuState.Run;
        Log.Information("[PlcEngine] Started. ScanTime={ScanTimeMs}ms", ScanTimeMs);
    }

    /// <summary>Requests a graceful stop of the scan loop.</summary>
    public async Task StopAsync()
    {
        if (_cts == null) return;
        CpuState = CpuState.Stop;
        _cts.Cancel();
        if (_scanTask != null)
            await _scanTask.ConfigureAwait(false);
        Log.Information("[PlcEngine] Stopped.");
    }

    /// <summary>Same as <see cref="StopAsync"/> but fire-and-forget for synchronous callers.</summary>
    public void Stop() => _ = StopAsync();

    // ────────────────────────────────────────────────────────────────────────
    // ── Scan loop ────────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    private void ScanLoop(CancellationToken ct)
    {
        var sw = new Stopwatch();
        Log.Debug("[PlcEngine] Scan loop thread started (TID={TID})", Environment.CurrentManagedThreadId);

        while (!ct.IsCancellationRequested)
        {
            sw.Restart();
            try
            {
                if (_cpuState == CpuState.Run)
                {
                    ExecuteUserLogic();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PlcEngine] Exception in scan cycle #{Cycle}", ScanCycleCount);
            }

            sw.Stop();
            long elapsedUs = sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
            LastScanTimeUs = elapsedUs;
            ScanCycleCount++;

            if (elapsedUs > OverrunWarningMs * 1000L)
            {
                OverrunCount++;
                Log.Warning("[PlcEngine] Scan overrun: {ElapsedUs}µs (limit {LimitUs}µs) on cycle #{Cycle}",
                    elapsedUs, OverrunWarningMs * 1000L, ScanCycleCount);
                ScanOverrun?.Invoke(this, elapsedUs);
            }

            // Sleep for remaining scan time
            long remainUs = ScanTimeMs * 1000L - elapsedUs;
            if (remainUs > 0)
                Thread.Sleep(TimeSpan.FromMicroseconds(remainUs));
        }

        Log.Debug("[PlcEngine] Scan loop thread exited.");
    }

    private void ExecuteUserLogic()
    {
        IPlcProgram? prog;
        Action<PlcMemory>? act;
        lock (_programLock) { prog = _program; act = _programAction; }

        if (prog != null)
            prog.Execute(Memory);
        else
            act?.Invoke(Memory);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── IDisposable ──────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        Memory.Dispose();
    }
}
