namespace SoftPlc.Core;

/// <summary>
/// Represents a user-supplied PLC program executed during each scan cycle.
/// </summary>
public interface IPlcProgram
{
    /// <summary>Called once per scan cycle while the CPU is in RUN state.</summary>
    void Execute(PlcMemory memory);
}
