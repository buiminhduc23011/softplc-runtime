using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SoftPlc.Core;

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
        app.MapPost("/memory/db/{id:int}/create", (PlcEngine engine, int id, int size = 256) =>
        {
            if (size < 1 || size > 65535)
                return Results.BadRequest("size must be 1..65535");
            engine.Memory.CreateDataBlock(id, size);
            return Results.Ok(new { DB = id, Size = size, Status = "created" });
        });

        return app;
    }
}
