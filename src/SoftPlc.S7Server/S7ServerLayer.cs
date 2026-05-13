using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
    // ── Direct P/Invoke to snap7.dll (bypasses wrapper's ref byte marshaling) ──
    [DllImport("snap7.dll")]
    private static extern int Srv_RegisterArea(IntPtr server, int areaCode, int index, IntPtr pUsrData, int size);

    // ── snap7 server ──────────────────────────────────────────────────────────
    private readonly Snap7.S7Server _server = new();
    private readonly IntPtr _serverHandle;

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

    // ── Port ──────────────────────────────────────────────────────────────────
    private int _port = 102;
    /// <summary>The TCP port the S7 server is listening on.</summary>
    public int Port => _port;

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

        // Extract native server handle via reflection (wrapper stores it as 'server' field)
        _serverHandle = ExtractServerHandle(_server);
        Log.Debug("[S7Server] Native handle: 0x{Handle:X}", _serverHandle);

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

        // Register fixed areas using direct P/Invoke with pinned IntPtr
        RegisterAreaDirect(Snap7.S7Server.srvAreaPE, 0, _pePin, _peBuffer.Length);
        RegisterAreaDirect(Snap7.S7Server.srvAreaPA, 0, _paPin, _paBuffer.Length);
        RegisterAreaDirect(Snap7.S7Server.srvAreaMK, 0, _mkPin, _mkBuffer.Length);

        // Keep callback delegate alive
        _eventsCallback = OnServerEvent;
        _server.SetEventsCallBack(_eventsCallback, IntPtr.Zero);

        // Sync timer: 5ms
        _syncTimer = new Timer(SyncBuffers, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(5));
    }

    /// <summary>Extract the native IntPtr handle from the Snap7.S7Server wrapper via reflection.</summary>
    private static IntPtr ExtractServerHandle(Snap7.S7Server server)
    {
        // Try common field names used by Snap7 .NET wrappers
        var type = server.GetType();
        foreach (var name in new[] { "server", "Server", "_server", "hServer", "Handle" })
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(IntPtr))
            {
                var handle = (IntPtr)field.GetValue(server)!;
                if (handle != IntPtr.Zero) return handle;
            }
        }
        // Fallback: search all IntPtr fields
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.FieldType == typeof(IntPtr))
            {
                var handle = (IntPtr)field.GetValue(server)!;
                if (handle != IntPtr.Zero)
                {
                    Log.Debug("[S7Server] Found native handle in field '{Name}'", field.Name);
                    return handle;
                }
            }
        }
        throw new InvalidOperationException("Cannot extract native server handle from Snap7.S7Server wrapper");
    }

    /// <summary>Register area using direct P/Invoke with a pinned GCHandle (guaranteed stable pointer).</summary>
    private void RegisterAreaDirect(int areaCode, int index, GCHandle pin, int size)
    {
        var ptr = pin.AddrOfPinnedObject();
        var rc = Srv_RegisterArea(_serverHandle, areaCode, index, ptr, size);
        if (rc != 0)
            Log.Error("[S7Server] Srv_RegisterArea failed: area={Area}, index={Index}, rc={Rc}", areaCode, index, rc);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── Public API ────────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Start listening on a local IP address and port.</summary>
    /// <param name="address">Bind address (default: all interfaces).</param>
    /// <param name="port">TCP port (default: 102). If 0, auto-selects 102 or 1102 if 102 is occupied.</param>
    public int Start(string address = "0.0.0.0", int port = 0)
    {
        if (port <= 0)
            port = IsPortAvailable(102) ? 102 : 1102;

        _port = port;

        // Set local port via Snap7 param p_u16_LocalPort = 1
        int portValue = port;
        _server.SetParam(1 /* p_u16_LocalPort */, ref portValue);

        var result = _server.StartTo(address);
        if (result == 0)
            Log.Information("[S7Server] Started on {Address}:{Port} (MaxClients={Max})",
                address, port, 3);
        else
            Log.Error("[S7Server] Start failed on port {Port}, error code={Code}", port, result);
        return result;
    }

    /// <summary>Check if a TCP port is available (not already in LISTEN state).</summary>
    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
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

        // Register using direct P/Invoke with pinned pointer
        RegisterAreaDirect(Snap7.S7Server.srvAreaDB, dbNumber, pin, size);
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
    /// Sync each registered DB: PlcMemory is the source of truth.
    ///
    /// 1. Detect S7 client writes (snap7 buffer changed vs shadow) and apply
    ///    only those bytes to PlcMemory (PlcMemory wins on conflict).
    /// 2. Push current PlcMemory → snap7 buffer so S7 clients can read.
    /// 3. Update shadow to current PlcMemory state.
    ///
    /// This avoids the old full-buffer write-back that could overwrite
    /// concurrent ViewModel writes with stale data.
    /// </summary>
    private void SyncDataBlocks()
    {
        foreach (var (dbNum, snap7Buf) in _dbBuffers)
        {
            var size   = snap7Buf.Length;
            var shadow = _dbShadow[dbNum];

            // ── 1. Snapshot snap7 buffer ──────────────────────────────────
            _server.LockArea(Snap7.S7Server.srvAreaDB, dbNum);
            var snap7Snapshot = new byte[size];
            Buffer.BlockCopy(snap7Buf, 0, snap7Snapshot, 0, size);
            _server.UnlockArea(Snap7.S7Server.srvAreaDB, dbNum);

            // ── 2. Detect & apply S7 client writes to PlcMemory ──────────
            //    Only runs when an S7 client actually wrote something.
            //    PlcMemory-side changes win on conflict (local priority).
            bool hasRemoteWrite = false;
            for (int i = 0; i < size; i++)
            {
                if (snap7Snapshot[i] != shadow[i])
                {
                    hasRemoteWrite = true;
                    break;
                }
            }

            if (hasRemoteWrite)
            {
                var mem = _memory.ReadDbArea(dbNum, 0, size);
                bool anyApplied = false;
                for (int i = 0; i < size; i++)
                {
                    if (snap7Snapshot[i] != shadow[i] && mem[i] == shadow[i])
                    {
                        // S7 client changed this byte AND PlcMemory didn't → apply
                        mem[i] = snap7Snapshot[i];
                        anyApplied = true;
                    }
                    // else: PlcMemory already changed (or both) → PlcMemory wins
                }
                if (anyApplied)
                    _memory.WriteDbArea(dbNum, 0, mem);
            }

            // ── 3. Push PlcMemory → snap7 buffer ─────────────────────────
            var current = _memory.ReadDbArea(dbNum, 0, size);

            _server.LockArea(Snap7.S7Server.srvAreaDB, dbNum);
            Buffer.BlockCopy(current, 0, snap7Buf, 0, size);
            _server.UnlockArea(Snap7.S7Server.srvAreaDB, dbNum);

            // ── 4. Update shadow ─────────────────────────────────────────
            Buffer.BlockCopy(current, 0, shadow, 0, size);
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
