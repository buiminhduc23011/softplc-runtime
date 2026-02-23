using SoftPlc.Core;

namespace SoftPlc.ControlApi;

/// <summary>
/// DTOs used by the Control API.
/// </summary>
public sealed record ForcePayload(string Area, int Byte, int Bit, bool Value);

public sealed record CpuStateDto(string State);

public sealed record MemoryBytesDto(int Offset, string Hex, int[] Bytes);

public sealed record ScanDiagDto(
    long  ScanCycleCount,
    long  LastScanTimeUs,
    long  OverrunCount,
    int   ScanTimeMs);

/// <summary>Body for PUT /memory/db/{id} – write raw bytes into a Data Block.</summary>
public sealed record WriteDbPayload(int Offset, int[] Bytes);
