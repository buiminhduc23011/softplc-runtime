using System.Runtime.InteropServices;
using Serilog;
using Snap7;

namespace SoftPlc.S7Server;

/// <summary>
/// Snap7 S7 server layer.
/// Exposes PlcMemory over the S7 protocol on port 102.
/// Thread-safe sync between snap7 shared buffers and PlcMemory via a background timer.
/// </summary>
public sealed class S7ServerLayer : IDisposable
{
    // ── snap7 server ──────────────────────────────────────────────────────────
    private readonly Snap7.S7Server _server = new();

    // ── PlcMemory reference ───────────────────────────────────────────────────
    private readonly SoftPlc.Core.PlcMemory _memory;

    // ── Shared buffers (pinned so GC won't move them while snap7 holds pointers)
    private readonly byte[] _peBuffer;  // Inputs
    private readonly byte[] _paBuffer;  // Outputs
    private readonly byte[] _mkBuffer;  // Merkers

    private readonly GCHandle _pePin;
    private readonly GCHandle _paPin;
    private readonly GCHandle _mkPin;

    // ── DB buffers ────────────────────────────────────────────────────────────
    private readonly Dictionary<int, byte[]>    _dbBuffers = new();
    private readonly Dictionary<int, GCHandle>  _dbPins    = new();
    private readonly Dictionary<int, byte[]>    _dbShadow  = new();  // last-synced snapshot for merge

    // ── Sync timer ────────────────────────────────────────────────────────────
    private readonly Timer _syncTimer;

    // ── Events callback (keep alive to avoid GC collection) ──────────────────
    private readonly Snap7.S7Server.TSrvCallback _eventsCallback;

    // ── Connection tracking ───────────────────────────────────────────────────
    public int ClientsCount => _server.ClientsCount;
    public int ServerStatus => _server.ServerStatus;

    private bool _disposed;

    // ────────────────────────────────────────────────────────────────────────
    // ── Construction ──────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public S7ServerLayer(SoftPlc.Core.PlcMemory memory, int maxClients = 3)
    {
        _memory = memory;

        // Allocate & pin buffers
        _peBuffer = new byte[SoftPlc.Core.PlcMemory.InputSize];
        _paBuffer = new byte[SoftPlc.Core.PlcMemory.OutputSize];
        _mkBuffer = new byte[SoftPlc.Core.PlcMemory.MerkerSize];

        _pePin = GCHandle.Alloc(_peBuffer, GCHandleType.Pinned);
        _paPin = GCHandle.Alloc(_paBuffer, GCHandleType.Pinned);
        _mkPin = GCHandle.Alloc(_mkBuffer, GCHandleType.Pinned);

        // Set max clients
        int mc = maxClients;
        _server.SetParam(S7Consts.p_i32_MaxClients, ref mc);

        // Register fixed areas
        _server.RegisterArea(Snap7.S7Server.srvAreaPE, 0, ref _peBuffer[0], _peBuffer.Length);
        _server.RegisterArea(Snap7.S7Server.srvAreaPA, 0, ref _paBuffer[0], _paBuffer.Length);
        _server.RegisterArea(Snap7.S7Server.srvAreaMK, 0, ref _mkBuffer[0], _mkBuffer.Length);

        // Keep callback delegate alive
        _eventsCallback = OnServerEvent;
        _server.SetEventsCallBack(_eventsCallback, IntPtr.Zero);

        // Sync timer: 5ms
        _syncTimer = new Timer(SyncBuffers, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(5));
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── Public API ────────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Start listening on a local IP address (default: all interfaces).</summary>
    public int Start(string address = "0.0.0.0")
    {
        var result = _server.StartTo(address);
        if (result == 0)
            Log.Information("[S7Server] Started on {Address}:102 (MaxClients={Max})", address, 3);
        else
            Log.Error("[S7Server] Start failed, error code={Code}", result);
        return result;
    }

    /// <summary>Stop the server.</summary>
    public void Stop()
    {
        _server.Stop();
        Log.Information("[S7Server] Stopped.");
    }

    /// <summary>Register a Data Block and map it through snap7.</summary>
    public void RegisterDataBlock(int dbNumber, int size)
    {
        if (_dbBuffers.ContainsKey(dbNumber))
        {
            Log.Warning("[S7Server] DB{Db} already registered – skipping.", dbNumber);
            return;
        }

        // Create in PlcMemory
        _memory.CreateDataBlock(dbNumber, size);

        // Allocate + pin buffer
        var buf    = new byte[size];
        var pin    = GCHandle.Alloc(buf, GCHandleType.Pinned);
        _dbBuffers[dbNumber] = buf;
        _dbPins[dbNumber]    = pin;
        _dbShadow[dbNumber]  = new byte[size];

        _server.RegisterArea(Snap7.S7Server.srvAreaDB, dbNumber, ref buf[0], size);
        Log.Information("[S7Server] Registered DB{Db} ({Size} bytes)", dbNumber, size);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── Buffer sync ───────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    private void SyncBuffers(object? _)
    {
        if (_disposed) return;
        try
        {
            SyncInputs();
            SyncOutputs();
            SyncMerkers();
            SyncDataBlocks();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[S7Server] SyncBuffers exception");
        }
    }

    /// <summary>
    /// Inputs: snap7 PE is the source of truth (S7 clients can simulate inputs).
    /// Copy snap7 PE buffer → PlcMemory.I
    /// </summary>
    private void SyncInputs()
    {
        _server.LockArea(Snap7.S7Server.srvAreaPE, 0);
        var snapshot = new byte[_peBuffer.Length];
        Buffer.BlockCopy(_peBuffer, 0, snapshot, 0, snapshot.Length);
        _server.UnlockArea(Snap7.S7Server.srvAreaPE, 0);
        _memory.WriteInputArea(0, snapshot);
    }

    /// <summary>
    /// Outputs: PlcMemory.Q is the source of truth (scan logic writes outputs).
    /// Copy PlcMemory.Q → snap7 PA buffer so S7 clients can read.
    /// </summary>
    private void SyncOutputs()
    {
        var data = _memory.ReadOutputArea(0, SoftPlc.Core.PlcMemory.OutputSize);
        _server.LockArea(Snap7.S7Server.srvAreaPA, 0);
        Buffer.BlockCopy(data, 0, _paBuffer, 0, data.Length);
        _server.UnlockArea(Snap7.S7Server.srvAreaPA, 0);
    }

    /// <summary>
    /// Merkers: bidirectional. Last writer wins. Sync both ways each tick.
    /// snap7 buffer → PlcMemory (captures external writes) then PlcMemory → snap7 buffer.
    /// </summary>
    private void SyncMerkers()
    {
        // Pull from snap7 → PlcMemory (external writes win for this tick)
        _server.LockArea(Snap7.S7Server.srvAreaMK, 0);
        var fromSnap7 = new byte[_mkBuffer.Length];
        Buffer.BlockCopy(_mkBuffer, 0, fromSnap7, 0, fromSnap7.Length);
        _server.UnlockArea(Snap7.S7Server.srvAreaMK, 0);
        _memory.WriteMerkerArea(0, fromSnap7);

        // Push PlcMemory → snap7 so scan-logic writes are visible to S7 clients
        var toSnap7 = _memory.ReadMerkerArea(0, SoftPlc.Core.PlcMemory.MerkerSize);
        _server.LockArea(Snap7.S7Server.srvAreaMK, 0);
        Buffer.BlockCopy(toSnap7, 0, _mkBuffer, 0, toSnap7.Length);
        _server.UnlockArea(Snap7.S7Server.srvAreaMK, 0);
    }

    /// <summary>
    /// Sync each registered DB: snap7 buffer ↔ PlcMemory via shadow-merge.
    /// A shadow buffer stores the last-synced state. Changes on either side
    /// (PlcMemory or snap7) are detected byte-by-byte vs the shadow;
    /// local (PlcMemory / ViewModel) writes take priority on conflict.
    /// </summary>
    private void SyncDataBlocks()
    {
        foreach (var (dbNum, snap7Buf) in _dbBuffers)
        {
            var size = snap7Buf.Length;

            // 1. Read current snap7 state
            _server.LockArea(Snap7.S7Server.srvAreaDB, dbNum);
            var snap7Data = new byte[size];
            Buffer.BlockCopy(snap7Buf, 0, snap7Data, 0, size);
            _server.UnlockArea(Snap7.S7Server.srvAreaDB, dbNum);

            // 2. Read current PlcMemory state
            var memData = _memory.ReadDbArea(dbNum, 0, size);

            // 3. Get shadow (last-synced snapshot)
            var shadow = _dbShadow[dbNum];

            // 4. Merge byte-by-byte
            var merged = new byte[size];
            for (int i = 0; i < size; i++)
            {
                bool memChanged  = memData[i]   != shadow[i];
                bool snap7Changed = snap7Data[i] != shadow[i];

                if (memChanged)
                    merged[i] = memData[i];     // local (ViewModel) wins
                else if (snap7Changed)
                    merged[i] = snap7Data[i];   // remote (S7 client) wins
                else
                    merged[i] = shadow[i];      // no change
            }

            // 5. Push merged result to snap7
            _server.LockArea(Snap7.S7Server.srvAreaDB, dbNum);
            Buffer.BlockCopy(merged, 0, snap7Buf, 0, size);
            _server.UnlockArea(Snap7.S7Server.srvAreaDB, dbNum);

            // 6. Push merged result to PlcMemory
            _memory.WriteDbArea(dbNum, 0, merged);

            // 7. Update shadow
            Buffer.BlockCopy(merged, 0, shadow, 0, size);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── Events ────────────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    private void OnServerEvent(IntPtr usrPtr, ref Snap7.S7Server.USrvEvent ev, int size)
    {
        var code = ev.EvtCode;

        if (code == Snap7.S7Server.evcClientAdded)
            Log.Information("[S7Server] Client connected. Sender={Sender} TotalClients={Total}", ev.EvtSender, _server.ClientsCount);
        else if (code == Snap7.S7Server.evcClientDisconnected || code == Snap7.S7Server.evcClientTerminated)
            Log.Information("[S7Server] Client disconnected. Sender={Sender}", ev.EvtSender);
        else if (code == Snap7.S7Server.evcClientRejected || code == Snap7.S7Server.evcClientNoRoom)
            Log.Warning("[S7Server] Client rejected (max clients reached). Sender={Sender}", ev.EvtSender);
        else if (code == Snap7.S7Server.evcDataWrite)
            Log.Debug("[S7Server] Client wrote area. Sender={Sender}", ev.EvtSender);
        else if (code == Snap7.S7Server.evcDataRead)
            Log.Debug("[S7Server] Client read area. Sender={Sender}", ev.EvtSender);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── IDisposable ──────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _syncTimer.Dispose();
        _server.Stop();

        _pePin.Free();
        _paPin.Free();
        _mkPin.Free();

        foreach (var h in _dbPins.Values) h.Free();
        _dbPins.Clear();
        _dbBuffers.Clear();
        _dbShadow.Clear();
    }
}
