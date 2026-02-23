using System.Diagnostics;

namespace SoftPlc.Core.Timers;

/// <summary>
/// TON – Timer On-Delay.
/// Q becomes TRUE when IN has been continuously TRUE for at least PT milliseconds.
/// </summary>
public sealed class TonTimer
{
    private readonly Stopwatch _sw = new();
    private bool _prevIn;

    public string Name { get; }

    /// <summary>Preset time in milliseconds.</summary>
    public int PresetMs { get; set; }

    /// <summary>Timer input.</summary>
    public bool IN { get; set; }

    /// <summary>Timer output (elapsed ≥ preset).</summary>
    public bool Q { get; private set; }

    /// <summary>Elapsed time in milliseconds.</summary>
    public long ET => _sw.IsRunning ? _sw.ElapsedMilliseconds : (_sw.Elapsed == TimeSpan.Zero ? 0 : _sw.ElapsedMilliseconds);

    public TonTimer(string name, int presetMs)
    {
        Name     = name;
        PresetMs = presetMs;
    }

    /// <summary>Call once per scan cycle.</summary>
    public void Update()
    {
        if (IN)
        {
            if (!_prevIn) _sw.Restart();  // rising edge → start
            else if (!Q && _sw.ElapsedMilliseconds >= PresetMs)
            {
                Q = true;
                _sw.Stop();
            }
        }
        else
        {
            // falling edge → reset
            Q = false;
            _sw.Reset();
        }
        _prevIn = IN;
    }
}
