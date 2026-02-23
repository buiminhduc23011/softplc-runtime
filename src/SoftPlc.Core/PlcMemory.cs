using System.Collections.Concurrent;

namespace SoftPlc.Core;

/// <summary>
/// Thread-safe PLC memory model representing I, Q, M, and DB areas.
/// Uses ReaderWriterLockSlim for concurrent read / exclusive write access.
/// </summary>
public sealed class PlcMemory : IDisposable
{
    // ── Area sizes ──────────────────────────────────────────────────────────
    public const int InputSize  = 1024;
    public const int OutputSize = 1024;
    public const int MerkerSize = 4096;

    // ── Raw arrays ──────────────────────────────────────────────────────────
    private readonly byte[] _inputs  = new byte[InputSize];
    private readonly byte[] _outputs = new byte[OutputSize];
    private readonly byte[] _merkers = new byte[MerkerSize];
    private readonly ConcurrentDictionary<int, byte[]> _dataBlocks = new();

    // ── Force tracking ──────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, bool> _forcedBits = new();

    // ── Locks (one per area) ─────────────────────────────────────────────────
    private readonly ReaderWriterLockSlim _iLock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly ReaderWriterLockSlim _qLock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly ReaderWriterLockSlim _mLock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly ReaderWriterLockSlim _dbLock = new(LockRecursionPolicy.SupportsRecursion);

    private bool _disposed;

    // ────────────────────────────────────────────────────────────────────────
    // ── Generic helpers ──────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    private static void ValidateRange(int offset, int length, int areaSize, string area)
    {
        if (offset < 0 || offset + length > areaSize)
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"Access out of range for area '{area}': offset={offset}, length={length}, size={areaSize}");
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── Input (I) ────────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public byte   GetInputByte(int byteOffset)  => ReadByte(_inputs, _iLock, byteOffset, InputSize, "I");
    public void   SetInputByte(int byteOffset, byte value) => WriteByte(_inputs, _iLock, byteOffset, value, InputSize, "I");
    public bool   GetInputBit(int byteOffset, int bit)     => ReadBit(_inputs, _iLock, byteOffset, bit, InputSize, "I");
    public void   SetInputBit(int byteOffset, int bit, bool value) => WriteBit(_inputs, _iLock, byteOffset, bit, value, InputSize, "I");
    public byte[] ReadInputArea(int offset, int count) => ReadArea(_inputs, _iLock, offset, count, InputSize, "I");
    public void   WriteInputArea(int offset, byte[] data) => WriteArea(_inputs, _iLock, offset, data, InputSize, "I");

    // ────────────────────────────────────────────────────────────────────────
    // ── Output (Q) ───────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public byte   GetOutputByte(int byteOffset)  => ReadByte(_outputs, _qLock, byteOffset, OutputSize, "Q");
    public void   SetOutputByte(int byteOffset, byte value) => WriteByte(_outputs, _qLock, byteOffset, value, OutputSize, "Q");
    public bool   GetOutputBit(int byteOffset, int bit)     => ReadBit(_outputs, _qLock, byteOffset, bit, OutputSize, "Q");
    public void   SetOutputBit(int byteOffset, int bit, bool value) => WriteBit(_outputs, _qLock, byteOffset, bit, value, OutputSize, "Q");
    public byte[] ReadOutputArea(int offset, int count) => ReadArea(_outputs, _qLock, offset, count, OutputSize, "Q");
    public void   WriteOutputArea(int offset, byte[] data) => WriteArea(_outputs, _qLock, offset, data, OutputSize, "Q");

    // ────────────────────────────────────────────────────────────────────────
    // ── Merker (M) ───────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public byte   GetMerkerByte(int byteOffset)  => ReadByte(_merkers, _mLock, byteOffset, MerkerSize, "M");
    public void   SetMerkerByte(int byteOffset, byte value) => WriteByte(_merkers, _mLock, byteOffset, value, MerkerSize, "M");
    public bool   GetMerkerBit(int byteOffset, int bit)     => ReadBit(_merkers, _mLock, byteOffset, bit, MerkerSize, "M");
    public void   SetMerkerBit(int byteOffset, int bit, bool value) => WriteBit(_merkers, _mLock, byteOffset, bit, value, MerkerSize, "M");
    public ushort GetMerkerWord(int byteOffset)  => ReadWord(_merkers, _mLock, byteOffset, MerkerSize, "M");
    public void   SetMerkerWord(int byteOffset, ushort value) => WriteWord(_merkers, _mLock, byteOffset, value, MerkerSize, "M");
    public uint   GetMerkerDWord(int byteOffset) => ReadDWord(_merkers, _mLock, byteOffset, MerkerSize, "M");
    public void   SetMerkerDWord(int byteOffset, uint value) => WriteDWord(_merkers, _mLock, byteOffset, value, MerkerSize, "M");
    public float  GetMerkerReal(int byteOffset)  => ReadReal(_merkers, _mLock, byteOffset, MerkerSize, "M");
    public void   SetMerkerReal(int byteOffset, float value) => WriteReal(_merkers, _mLock, byteOffset, value, MerkerSize, "M");
    public byte[] ReadMerkerArea(int offset, int count) => ReadArea(_merkers, _mLock, offset, count, MerkerSize, "M");
    public void   WriteMerkerArea(int offset, byte[] data) => WriteArea(_merkers, _mLock, offset, data, MerkerSize, "M");

    // ────────────────────────────────────────────────────────────────────────
    // ── DataBlocks (DB) ──────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Creates or replaces a Data Block with the given size (filled with zeros).</summary>
    public void CreateDataBlock(int dbNumber, int size)
    {
        if (dbNumber < 1) throw new ArgumentOutOfRangeException(nameof(dbNumber));
        if (size      < 1) throw new ArgumentOutOfRangeException(nameof(size));
        _dataBlocks[dbNumber] = new byte[size];
    }

    public void RemoveDataBlock(int dbNumber) => _dataBlocks.TryRemove(dbNumber, out _);

    public byte[] ReadDbArea(int dbNumber, int offset, int count)
    {
        var db = GetDb(dbNumber);
        _dbLock.EnterReadLock();
        try
        {
            ValidateRange(offset, count, db.Length, $"DB{dbNumber}");
            var result = new byte[count];
            Buffer.BlockCopy(db, offset, result, 0, count);
            return result;
        }
        finally { _dbLock.ExitReadLock(); }
    }

    public void WriteDbArea(int dbNumber, int offset, byte[] data)
    {
        var db = GetDb(dbNumber);
        _dbLock.EnterWriteLock();
        try
        {
            ValidateRange(offset, data.Length, db.Length, $"DB{dbNumber}");
            Buffer.BlockCopy(data, 0, db, offset, data.Length);
        }
        finally { _dbLock.ExitWriteLock(); }
    }

    public byte GetDbByte(int dbNumber, int offset)   => ReadDbArea(dbNumber, offset, 1)[0];
    public void SetDbByte(int dbNumber, int offset, byte value) => WriteDbArea(dbNumber, offset, [value]);

    public ushort GetDbWord(int dbNumber, int offset)
    {
        var raw = ReadDbArea(dbNumber, offset, 2);
        return (ushort)((raw[0] << 8) | raw[1]);
    }

    public void SetDbWord(int dbNumber, int offset, ushort value)
        => WriteDbArea(dbNumber, offset, [(byte)(value >> 8), (byte)(value & 0xFF)]);

    public bool DataBlockExists(int dbNumber) => _dataBlocks.ContainsKey(dbNumber);

    public int[] GetDataBlockNumbers() => [.. _dataBlocks.Keys];

    public int GetDataBlockSize(int dbNumber) => GetDb(dbNumber).Length;

    private byte[] GetDb(int dbNumber)
        => _dataBlocks.TryGetValue(dbNumber, out var db) ? db
            : throw new KeyNotFoundException($"DB{dbNumber} does not exist");

    // ────────────────────────────────────────────────────────────────────────
    // ── Force helpers ────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public void ForceMemory(string area, int byteOffset, int bit, bool value)
    {
        area = area.ToUpperInvariant();
        string key = $"{area}.{byteOffset}.{bit}";
        _forcedBits[key] = value;

        switch (area)
        {
            case "I": SetInputBit(byteOffset, bit, value);  break;
            case "Q": SetOutputBit(byteOffset, bit, value); break;
            case "M": SetMerkerBit(byteOffset, bit, value); break;
            default: throw new ArgumentException($"Unknown area '{area}' for force");
        }
    }

    public IReadOnlyDictionary<string, bool> GetForcedBits() => _forcedBits;

    // ────────────────────────────────────────────────────────────────────────
    // ── Private primitives ───────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    private static byte ReadByte(byte[] buf, ReaderWriterLockSlim lk, int offset, int size, string area)
    {
        ValidateRange(offset, 1, size, area);
        lk.EnterReadLock();
        try   { return buf[offset]; }
        finally { lk.ExitReadLock(); }
    }

    private static void WriteByte(byte[] buf, ReaderWriterLockSlim lk, int offset, byte value, int size, string area)
    {
        ValidateRange(offset, 1, size, area);
        lk.EnterWriteLock();
        try   { buf[offset] = value; }
        finally { lk.ExitWriteLock(); }
    }

    private static bool ReadBit(byte[] buf, ReaderWriterLockSlim lk, int byteOffset, int bit, int size, string area)
    {
        if (bit < 0 || bit > 7) throw new ArgumentOutOfRangeException(nameof(bit));
        ValidateRange(byteOffset, 1, size, area);
        lk.EnterReadLock();
        try   { return (buf[byteOffset] & (1 << bit)) != 0; }
        finally { lk.ExitReadLock(); }
    }

    private static void WriteBit(byte[] buf, ReaderWriterLockSlim lk, int byteOffset, int bit, bool value, int size, string area)
    {
        if (bit < 0 || bit > 7) throw new ArgumentOutOfRangeException(nameof(bit));
        ValidateRange(byteOffset, 1, size, area);
        lk.EnterWriteLock();
        try
        {
            if (value) buf[byteOffset] |=  (byte)(1 << bit);
            else       buf[byteOffset] &= (byte)~(1 << bit);
        }
        finally { lk.ExitWriteLock(); }
    }

    private static byte[] ReadArea(byte[] buf, ReaderWriterLockSlim lk, int offset, int count, int size, string area)
    {
        ValidateRange(offset, count, size, area);
        lk.EnterReadLock();
        try
        {
            var result = new byte[count];
            Buffer.BlockCopy(buf, offset, result, 0, count);
            return result;
        }
        finally { lk.ExitReadLock(); }
    }

    private static void WriteArea(byte[] buf, ReaderWriterLockSlim lk, int offset, byte[] data, int size, string area)
    {
        ValidateRange(offset, data.Length, size, area);
        lk.EnterWriteLock();
        try   { Buffer.BlockCopy(data, 0, buf, offset, data.Length); }
        finally { lk.ExitWriteLock(); }
    }

    private static ushort ReadWord(byte[] buf, ReaderWriterLockSlim lk, int offset, int size, string area)
    {
        ValidateRange(offset, 2, size, area);
        lk.EnterReadLock();
        try   { return (ushort)((buf[offset] << 8) | buf[offset + 1]); }
        finally { lk.ExitReadLock(); }
    }

    private static void WriteWord(byte[] buf, ReaderWriterLockSlim lk, int offset, ushort value, int size, string area)
    {
        ValidateRange(offset, 2, size, area);
        lk.EnterWriteLock();
        try   { buf[offset] = (byte)(value >> 8); buf[offset + 1] = (byte)(value & 0xFF); }
        finally { lk.ExitWriteLock(); }
    }

    private static uint ReadDWord(byte[] buf, ReaderWriterLockSlim lk, int offset, int size, string area)
    {
        ValidateRange(offset, 4, size, area);
        lk.EnterReadLock();
        try   { return (uint)((buf[offset] << 24) | (buf[offset+1] << 16) | (buf[offset+2] << 8) | buf[offset+3]); }
        finally { lk.ExitReadLock(); }
    }

    private static void WriteDWord(byte[] buf, ReaderWriterLockSlim lk, int offset, uint value, int size, string area)
    {
        ValidateRange(offset, 4, size, area);
        lk.EnterWriteLock();
        try
        {
            buf[offset]   = (byte)(value >> 24);
            buf[offset+1] = (byte)(value >> 16);
            buf[offset+2] = (byte)(value >> 8);
            buf[offset+3] = (byte)(value & 0xFF);
        }
        finally { lk.ExitWriteLock(); }
    }

    private static float ReadReal(byte[] buf, ReaderWriterLockSlim lk, int offset, int size, string area)
    {
        ValidateRange(offset, 4, size, area);
        lk.EnterReadLock();
        try   { return BitConverter.ToSingle(buf, offset); }
        finally { lk.ExitReadLock(); }
    }

    private static void WriteReal(byte[] buf, ReaderWriterLockSlim lk, int offset, float value, int size, string area)
    {
        ValidateRange(offset, 4, size, area);
        var bytes = BitConverter.GetBytes(value);
        lk.EnterWriteLock();
        try   { Buffer.BlockCopy(bytes, 0, buf, offset, 4); }
        finally { lk.ExitWriteLock(); }
    }

    // ────────────────────────────────────────────────────────────────────────
    // ── IDisposable ──────────────────────────────────────────────────────────
    // ────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _iLock.Dispose();
        _qLock.Dispose();
        _mLock.Dispose();
        _dbLock.Dispose();
    }
}
