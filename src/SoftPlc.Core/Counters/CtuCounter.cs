namespace SoftPlc.Core.Counters;

/// <summary>
/// CTU – Count Up counter.
/// CV increments on rising edge of CU. Q becomes TRUE when CV ≥ PV.
/// R resets CV to 0.
/// </summary>
public sealed class CtuCounter
{
    private bool _prevCu;

    public string Name { get; }

    /// <summary>Preset value.</summary>
    public int PV { get; set; }

    /// <summary>Count Up input (increments on rising edge).</summary>
    public bool CU { get; set; }

    /// <summary>Reset input (active high, synchronous reset).</summary>
    public bool R { get; set; }

    /// <summary>Counter output – TRUE when CV ≥ PV.</summary>
    public bool Q { get; private set; }

    /// <summary>Current count value.</summary>
    public int CV { get; private set; }

    public CtuCounter(string name, int presetValue)
    {
        Name = name;
        PV   = presetValue;
    }

    /// <summary>Call once per scan cycle.</summary>
    public void Update()
    {
        if (R)
        {
            CV = 0;
        }
        else if (CU && !_prevCu)
        {
            // rising edge
            if (CV < int.MaxValue) CV++;
        }

        Q = CV >= PV;
        _prevCu = CU;
    }

    /// <summary>Directly preset the counter value (e.g. from Control API).</summary>
    public void Preset(int value) => CV = value;
}
