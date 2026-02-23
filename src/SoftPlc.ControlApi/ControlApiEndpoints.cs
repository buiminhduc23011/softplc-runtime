using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SoftPlc.Core;
using SoftPlc.S7Server;

namespace SoftPlc.ControlApi;

/// <summary>
/// Registers all Control API routes onto a <see cref="WebApplication"/>.
/// The host must register <see cref="PlcEngine"/> as a singleton in DI before calling this.
/// </summary>
public static class ControlApiEndpoints
{
    public static IEndpointRouteBuilder MapControlApiRoutes(this IEndpointRouteBuilder app)
    {
        // ── CPU state ─────────────────────────────────────────────────────────
        app.MapGet("/cpu/state", (PlcEngine engine) =>
            Results.Ok(new CpuStateDto(engine.CpuState.ToString())));

        app.MapGet("/cpu/diag", (PlcEngine engine) =>
            Results.Ok(new ScanDiagDto(
                engine.ScanCycleCount,
                engine.LastScanTimeUs,
                engine.OverrunCount,
                engine.ScanTimeMs)));

        app.MapPost("/cpu/run", (PlcEngine engine) =>
        {
            engine.Start();
            return Results.Ok(new CpuStateDto(engine.CpuState.ToString()));
        });

        app.MapPost("/cpu/stop", async (PlcEngine engine) =>
        {
            await engine.StopAsync();
            return Results.Ok(new CpuStateDto(engine.CpuState.ToString()));
        });

        // ── Memory – Merkers ────────────────────────────────────────────────────
        app.MapGet("/memory/m/{byteOffset:int}", (PlcEngine engine, int byteOffset, int count = 1) =>
        {
            count = Math.Clamp(count, 1, 256);
            try
            {
                var data = engine.Memory.ReadMerkerArea(byteOffset, count);
                return Results.Ok(new MemoryBytesDto(
                    byteOffset,
                    Convert.ToHexString(data),
                    data.Select(b => (int)b).ToArray()));
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.BadRequest("Offset/count out of range");
            }
        });

        // ── Memory – DataBlocks ──────────────────────────────────────────────────
        app.MapGet("/memory/db/{id:int}", (PlcEngine engine, int id, int offset = 0, int count = 16) =>
        {
            count = Math.Clamp(count, 1, 512);
            try
            {
                var data = engine.Memory.ReadDbArea(id, offset, count);
                return Results.Ok(new MemoryBytesDto(
                    offset,
                    Convert.ToHexString(data),
                    data.Select(b => (int)b).ToArray()));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"DB{id} does not exist");
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.BadRequest("Offset/count out of range");
            }
        });

        // ── Write bytes to DataBlock ────────────────────────────────────────────
        app.MapPut("/memory/db/{id:int}", (PlcEngine engine, int id, WriteDbPayload payload) =>
        {
            if (payload.Bytes is null || payload.Bytes.Length == 0)
                return Results.BadRequest("bytes array must not be empty");

            try
            {
                var raw = payload.Bytes.Select(b => (byte)(b & 0xFF)).ToArray();
                engine.Memory.WriteDbArea(id, payload.Offset, raw);
                return Results.Ok(new { DB = id, Offset = payload.Offset, BytesWritten = raw.Length });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"DB{id} does not exist");
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.BadRequest("Offset out of range");
            }
        });

        // ── Memory – Inputs ────────────────────────────────────────────────────
        app.MapGet("/memory/i/{byteOffset:int}", (PlcEngine engine, int byteOffset, int count = 1) =>
        {
            count = Math.Clamp(count, 1, 256);
            try
            {
                var data = engine.Memory.ReadInputArea(byteOffset, count);
                return Results.Ok(new MemoryBytesDto(byteOffset, Convert.ToHexString(data), data.Select(b => (int)b).ToArray()));
            }
            catch (ArgumentOutOfRangeException) { return Results.BadRequest("Out of range"); }
        });

        // ── Memory – Outputs ───────────────────────────────────────────────────
        app.MapGet("/memory/q/{byteOffset:int}", (PlcEngine engine, int byteOffset, int count = 1) =>
        {
            count = Math.Clamp(count, 1, 256);
            try
            {
                var data = engine.Memory.ReadOutputArea(byteOffset, count);
                return Results.Ok(new MemoryBytesDto(byteOffset, Convert.ToHexString(data), data.Select(b => (int)b).ToArray()));
            }
            catch (ArgumentOutOfRangeException) { return Results.BadRequest("Out of range"); }
        });

        // ── Force bits/bytes ────────────────────────────────────────────────────
        app.MapPost("/memory/force", (PlcEngine engine, ForcePayload payload) =>
        {
            if (payload.Bit < 0 || payload.Bit > 7)
                return Results.BadRequest("bit must be 0..7");
            try
            {
                engine.Memory.ForceMemory(payload.Area, payload.Byte, payload.Bit, payload.Value);
                return Results.Ok(new { payload.Area, payload.Byte, payload.Bit, payload.Value, Status = "forced" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // ── DB management ────────────────────────────────────────────────────────
        // Creates DB in PlcMemory AND registers it in the S7 server layer (if available).
        // After this call S7 clients can immediately read/write the new DB.
        app.MapPost("/memory/db/{id:int}/create",
            (PlcEngine engine, IServiceProvider sp, int id, int size = 256) =>
        {
            if (size < 1 || size > 65535)
                return Results.BadRequest("size must be 1..65535");

            if (engine.Memory.DataBlockExists(id))
                return Results.Conflict($"DB{id} already exists");

            // Register in PlcMemory
            engine.Memory.CreateDataBlock(id, size);

            // Register in S7 server so remote clients can access it
            var s7 = sp.GetService<S7ServerLayer>();
            s7?.RegisterDataBlock(id, size);

            return Results.Ok(new
            {
                DB     = id,
                Size   = size,
                Status = s7 is not null ? "created+s7registered" : "created (no S7 layer)"
            });
        });

        // ── List all registered DBs ──────────────────────────────────────────────
        app.MapGet("/memory/db", (PlcEngine engine) =>
        {
            var dbs = engine.Memory.GetDataBlockNumbers()
                .OrderBy(n => n)
                .Select(n => new { DB = n, Size = engine.Memory.GetDataBlockSize(n) });
            return Results.Ok(dbs);
        });

        return app;
    }
}
