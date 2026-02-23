namespace SoftPlc.Core;

/// <summary>
/// Represents the operational state of the PLC CPU.
/// </summary>
public enum CpuState
{
    /// <summary>PLC is stopped – scan loop does not execute user logic.</summary>
    Stop,

    /// <summary>PLC is running – scan loop executes user logic each cycle.</summary>
    Run
}
