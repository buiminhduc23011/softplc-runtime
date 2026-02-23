using System.Diagnostics;

namespace SoftPlc.Core.Timers;

/// <summary>
/// TOF – Timer Off-Delay.
/// Q becomes FALSE when IN transitions to FALSE and PT milliseconds have elapsed.
/// </summary>
public sealed class TofTimer
{
    private readonly Stopwatch _sw = new();
    private bool _prevIn;

    public string Name { get; }

    /// <summary>Preset time in milliseconds.</summary>
    public int PresetMs { get; set; }

    /// <summary>Timer input.</summary>
    public bool IN { get; set; }

    /// <summary>Timer output.</summary>
    public bool Q { get; private set; }

    /// <summary>Elapsed time in milliseconds since IN went FALSE.</summary>
    public long ET => _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;

    public TofTimer(string name, int presetMs)
    {
        Name     = name;
        PresetMs = presetMs;
    }

    /// <summary>Call once per scan cycle.</summary>
    public void Update()
    {
        if (IN)
        {
            Q = true;
            _sw.Reset();
        }
        else
        {
            if (_prevIn)
                _sw.Restart();  // falling edge → start delay

            if (Q && _sw.ElapsedMilliseconds >= PresetMs)
            {
                Q = false;
                _sw.Stop();
            }
        }
        _prevIn = IN;
    }
}
