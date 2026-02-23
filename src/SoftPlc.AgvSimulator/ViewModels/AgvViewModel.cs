using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SoftPlc.Core;
using SoftPlc.AgvSimulator.Models;

namespace SoftPlc.AgvSimulator.ViewModels;

/// <summary>
/// Bindable ViewModel for DB172.
/// - INPUT properties: user edits → writes to PlcMemory (simulating AGV → PLC)
/// - OUTPUT properties: read-only, refreshed from PlcMemory (PLC/S7 client → AGV)
/// </summary>
public sealed class AgvViewModel : INotifyPropertyChanged
{
    private readonly PlcMemory _mem;
    private byte[] _buf = new byte[Db172Map.DbSize];

    // Suppress write-back during Refresh to avoid feedback loops
    private bool _refreshing;

    public AgvViewModel(PlcMemory memory)
    {
        _mem = memory;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ── INPUT – bool flags (byte 0) ─────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    private bool _onOff;
    public bool OnOff
    {
        get => _onOff;
        set { if (SetInput(ref _onOff, value)) WriteBit(Db172Map.FLAGS_IN, 0, value); }
    }

    private bool _start;
    public bool Start
    {
        get => _start;
        set { if (SetInput(ref _start, value)) WriteBit(Db172Map.FLAGS_IN, 1, value); }
    }

    private bool _clearError;
    public bool ClearError
    {
        get => _clearError;
        set { if (SetInput(ref _clearError, value)) WriteBit(Db172Map.FLAGS_IN, 2, value); }
    }

    private bool _safetyRelay;
    public bool SafetyRelay
    {
        get => _safetyRelay;
        set { if (SetInput(ref _safetyRelay, value)) WriteBit(Db172Map.FLAGS_IN, 3, value); }
    }

    // ── INPUT – floats ──────────────────────────────────────────────────────

    private float _valueVoltage;
    public float ValueVoltage
    {
        get => _valueVoltage;
        set { if (SetInput(ref _valueVoltage, value)) WriteFloat(Db172Map.VALUE_VOLTAGE, value); }
    }

    private float _valueCurrent;
    public float ValueCurrent
    {
        get => _valueCurrent;
        set { if (SetInput(ref _valueCurrent, value)) WriteFloat(Db172Map.VALUE_CURRENT, value); }
    }

    // ── INPUT – int16 bit arrays ────────────────────────────────────────────

    private short _signalEmgs;
    public short SignalEmgs
    {
        get => _signalEmgs;
        set { if (SetInput(ref _signalEmgs, value)) WriteInt16(Db172Map.SIGNAL_EMGS, value); }
    }

    private short _signalBumper;
    public short SignalBumper
    {
        get => _signalBumper;
        set { if (SetInput(ref _signalBumper, value)) WriteInt16(Db172Map.SIGNAL_BUMPER, value); }
    }

    private short _statusSensor;
    public short StatusSensor
    {
        get => _statusSensor;
        set { if (SetInput(ref _statusSensor, value)) WriteInt16(Db172Map.STATUS_SENSOR, value); }
    }

    private short _safetyZone1;
    public short SafetyZone1
    {
        get => _safetyZone1;
        set { if (SetInput(ref _safetyZone1, value)) WriteInt16(Db172Map.SAFETY_ZONE_1, value); }
    }

    private short _safetyZone2;
    public short SafetyZone2
    {
        get => _safetyZone2;
        set { if (SetInput(ref _safetyZone2, value)) WriteInt16(Db172Map.SAFETY_ZONE_2, value); }
    }

    // ── INPUT – RFID ────────────────────────────────────────────────────────

    private short _statusRfid;
    public short StatusRfid
    {
        get => _statusRfid;
        set { if (SetInput(ref _statusRfid, value)) WriteInt16(Db172Map.STATUS_RFID, value); }
    }

    private short _codeRfid;
    public short CodeRfid
    {
        get => _codeRfid;
        set { if (SetInput(ref _codeRfid, value)) WriteInt16(Db172Map.CODE_RFID, value); }
    }

    // ── INPUT – MAGLINE ────────────────────────────────────────────────────

    private short _statusMagline;
    public short StatusMagline
    {
        get => _statusMagline;
        set { if (SetInput(ref _statusMagline, value)) WriteInt16(Db172Map.STATUS_MAGLINE, value); }
    }

    private short _valueMagline;
    public short ValueMagline
    {
        get => _valueMagline;
        set { if (SetInput(ref _valueMagline, value)) WriteInt16(Db172Map.VALUE_MAGLINE, value); }
    }

    // ── INPUT – MOTOR 1 ────────────────────────────────────────────────────

    private short _statusMotor1;
    public short StatusMotor1
    {
        get => _statusMotor1;
        set { if (SetInput(ref _statusMotor1, value)) WriteInt16(Db172Map.STATUS_MOTOR_1, value); }
    }

    private short _warnMotor1;
    public short WarnMotor1
    {
        get => _warnMotor1;
        set { if (SetInput(ref _warnMotor1, value)) WriteInt16(Db172Map.WARN_MOTOR_1, value); }
    }

    private short _errorMotor1;
    public short ErrorMotor1
    {
        get => _errorMotor1;
        set { if (SetInput(ref _errorMotor1, value)) WriteInt16(Db172Map.ERROR_MOTOR_1, value); }
    }

    private short _speed1;
    public short Speed1
    {
        get => _speed1;
        set { if (SetInput(ref _speed1, value)) WriteInt16(Db172Map.SPEED_1, value); }
    }

    private int _valueEncoder1;
    public int ValueEncoder1
    {
        get => _valueEncoder1;
        set { if (SetInput(ref _valueEncoder1, value)) WriteInt32(Db172Map.VALUE_ENCODER_1, value); }
    }

    // ── INPUT – MOTOR 2 ────────────────────────────────────────────────────

    private short _statusMotor2;
    public short StatusMotor2
    {
        get => _statusMotor2;
        set { if (SetInput(ref _statusMotor2, value)) WriteInt16(Db172Map.STATUS_MOTOR_2, value); }
    }

    private short _warnMotor2;
    public short WarnMotor2
    {
        get => _warnMotor2;
        set { if (SetInput(ref _warnMotor2, value)) WriteInt16(Db172Map.WARN_MOTOR_2, value); }
    }

    private short _errorMotor2;
    public short ErrorMotor2
    {
        get => _errorMotor2;
        set { if (SetInput(ref _errorMotor2, value)) WriteInt16(Db172Map.ERROR_MOTOR_2, value); }
    }

    private short _speed2;
    public short Speed2
    {
        get => _speed2;
        set { if (SetInput(ref _speed2, value)) WriteInt16(Db172Map.SPEED_2, value); }
    }

    private int _valueEncoder2;
    public int ValueEncoder2
    {
        get => _valueEncoder2;
        set { if (SetInput(ref _valueEncoder2, value)) WriteInt32(Db172Map.VALUE_ENCODER_2, value); }
    }

    // ── INPUT – MOTION ──────────────────────────────────────────────────────

    private short _moveMoveIn;
    public short MoveMoveIn
    {
        get => _moveMoveIn;
        set { if (SetInput(ref _moveMoveIn, value)) WriteInt16(Db172Map.MOVE_MOVE_IN, value); }
    }

    private short _statusMove;
    public short StatusMove
    {
        get => _statusMove;
        set { if (SetInput(ref _statusMove, value)) WriteInt16(Db172Map.STATUS_MOVE, value); }
    }

    private short _modeLiftIn;
    public short ModeLiftIn
    {
        get => _modeLiftIn;
        set { if (SetInput(ref _modeLiftIn, value)) WriteInt16(Db172Map.MODE_LIFT_IN, value); }
    }

    private short _statusLift;
    public short StatusLift
    {
        get => _statusLift;
        set { if (SetInput(ref _statusLift, value)) WriteInt16(Db172Map.STATUS_LIFT, value); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ── OUTPUT – bool flags (byte 60) ──────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    private bool _writeEmg;
    public bool WriteEmg
    {
        get => _writeEmg;
        set { if (SetOutput(ref _writeEmg, value)) WriteBit(Db172Map.FLAGS_OUT, 0, value); }
    }

    private bool _reset;
    public bool Reset
    {
        get => _reset;
        set { if (SetOutput(ref _reset, value)) WriteBit(Db172Map.FLAGS_OUT, 1, value); }
    }

    private bool _charge;
    public bool Charge
    {
        get => _charge;
        set { if (SetOutput(ref _charge, value)) WriteBit(Db172Map.FLAGS_OUT, 2, value); }
    }

    private bool _brake;
    public bool Brake
    {
        get => _brake;
        set { if (SetOutput(ref _brake, value)) WriteBit(Db172Map.FLAGS_OUT, 3, value); }
    }

    // ── OUTPUT – int16 ──────────────────────────────────────────────────────

    private short _sound;
    public short Sound
    {
        get => _sound;
        set { if (SetOutput(ref _sound, value)) WriteInt16(Db172Map.SOUND, value); }
    }

    private short _light;
    public short Light
    {
        get => _light;
        set { if (SetOutput(ref _light, value)) WriteInt16(Db172Map.LIGHT, value); }
    }

    private short _fieldSet1;
    public short FieldSet1
    {
        get => _fieldSet1;
        set { if (SetOutput(ref _fieldSet1, value)) WriteInt16(Db172Map.FIELD_SET_1, value); }
    }

    private short _fieldSet2;
    public short FieldSet2
    {
        get => _fieldSet2;
        set { if (SetOutput(ref _fieldSet2, value)) WriteInt16(Db172Map.FIELD_SET_2, value); }
    }

    private short _controlMotion;
    public short ControlMotion
    {
        get => _controlMotion;
        set { if (SetOutput(ref _controlMotion, value)) WriteInt16(Db172Map.CONTROL_MOTION, value); }
    }

    private short _speedMotor1;
    public short SpeedMotor1
    {
        get => _speedMotor1;
        set { if (SetOutput(ref _speedMotor1, value)) WriteInt16(Db172Map.SPEED_MOTOR_1, value); }
    }

    private short _speedMotor2;
    public short SpeedMotor2
    {
        get => _speedMotor2;
        set { if (SetOutput(ref _speedMotor2, value)) WriteInt16(Db172Map.SPEED_MOTOR_2, value); }
    }

    private short _moveMoveOut;
    public short MoveMoveOut
    {
        get => _moveMoveOut;
        set { if (SetOutput(ref _moveMoveOut, value)) WriteInt16(Db172Map.MOVE_MOVE_OUT, value); }
    }

    private short _speedMove;
    public short SpeedMove
    {
        get => _speedMove;
        set { if (SetOutput(ref _speedMove, value)) WriteInt16(Db172Map.SPEED_MOVE, value); }
    }

    private short _modeLiftOut;
    public short ModeLiftOut
    {
        get => _modeLiftOut;
        set { if (SetOutput(ref _modeLiftOut, value)) WriteInt16(Db172Map.MODE_LIFT_OUT, value); }
    }

    private short _speedLift;
    public short SpeedLift
    {
        get => _speedLift;
        set { if (SetOutput(ref _speedLift, value)) WriteInt16(Db172Map.SPEED_LIFT, value); }
    }

    // ── PLC Diagnostics (display-only) ─────────────────────────────────────

    private string _cpuState = "Stop";
    public string CpuState { get => _cpuState; set => Set(ref _cpuState, value); }

    private long _scanCycle;
    public long ScanCycle { get => _scanCycle; set => Set(ref _scanCycle, value); }

    private long _lastScanUs;
    public long LastScanUs { get => _lastScanUs; set => Set(ref _lastScanUs, value); }

    private long _overrunCount;
    public long OverrunCount { get => _overrunCount; set => Set(ref _overrunCount, value); }

    private string _s7Status = "Initialising…";
    public string S7Status { get => _s7Status; set => Set(ref _s7Status, value); }

    // ═══════════════════════════════════════════════════════════════════════
    // ── Refresh – reads entire DB172 and updates UI ─────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    public void Refresh()
    {
        try
        {
            _buf = _mem.ReadDbArea(Db172Map.DbNumber, 0, Db172Map.DbSize);
        }
        catch { return; }

        _refreshing = true;
        try
        {
            // INPUT
            OnOff        = Db172Map.ReadBit(_buf, Db172Map.FLAGS_IN, 0);
            Start        = Db172Map.ReadBit(_buf, Db172Map.FLAGS_IN, 1);
            ClearError   = Db172Map.ReadBit(_buf, Db172Map.FLAGS_IN, 2);
            SafetyRelay  = Db172Map.ReadBit(_buf, Db172Map.FLAGS_IN, 3);
            ValueVoltage = Db172Map.ReadFloatBE(_buf, Db172Map.VALUE_VOLTAGE);
            ValueCurrent = Db172Map.ReadFloatBE(_buf, Db172Map.VALUE_CURRENT);
            SignalEmgs   = Db172Map.ReadInt16BE(_buf, Db172Map.SIGNAL_EMGS);
            SignalBumper = Db172Map.ReadInt16BE(_buf, Db172Map.SIGNAL_BUMPER);
            StatusSensor = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_SENSOR);
            SafetyZone1  = Db172Map.ReadInt16BE(_buf, Db172Map.SAFETY_ZONE_1);
            SafetyZone2  = Db172Map.ReadInt16BE(_buf, Db172Map.SAFETY_ZONE_2);
            StatusRfid   = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_RFID);
            CodeRfid     = Db172Map.ReadInt16BE(_buf, Db172Map.CODE_RFID);
            StatusMagline = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_MAGLINE);
            ValueMagline  = Db172Map.ReadInt16BE(_buf, Db172Map.VALUE_MAGLINE);
            StatusMotor1  = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_MOTOR_1);
            WarnMotor1    = Db172Map.ReadInt16BE(_buf, Db172Map.WARN_MOTOR_1);
            ErrorMotor1   = Db172Map.ReadInt16BE(_buf, Db172Map.ERROR_MOTOR_1);
            Speed1        = Db172Map.ReadInt16BE(_buf, Db172Map.SPEED_1);
            ValueEncoder1 = Db172Map.ReadInt32BE(_buf, Db172Map.VALUE_ENCODER_1);
            StatusMotor2  = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_MOTOR_2);
            WarnMotor2    = Db172Map.ReadInt16BE(_buf, Db172Map.WARN_MOTOR_2);
            ErrorMotor2   = Db172Map.ReadInt16BE(_buf, Db172Map.ERROR_MOTOR_2);
            Speed2        = Db172Map.ReadInt16BE(_buf, Db172Map.SPEED_2);
            ValueEncoder2 = Db172Map.ReadInt32BE(_buf, Db172Map.VALUE_ENCODER_2);
            MoveMoveIn    = Db172Map.ReadInt16BE(_buf, Db172Map.MOVE_MOVE_IN);
            StatusMove    = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_MOVE);
            ModeLiftIn    = Db172Map.ReadInt16BE(_buf, Db172Map.MODE_LIFT_IN);
            StatusLift    = Db172Map.ReadInt16BE(_buf, Db172Map.STATUS_LIFT);

            // OUTPUT
            WriteEmg      = Db172Map.ReadBit(_buf, Db172Map.FLAGS_OUT, 0);
            Reset         = Db172Map.ReadBit(_buf, Db172Map.FLAGS_OUT, 1);
            Charge        = Db172Map.ReadBit(_buf, Db172Map.FLAGS_OUT, 2);
            Brake         = Db172Map.ReadBit(_buf, Db172Map.FLAGS_OUT, 3);
            Sound         = Db172Map.ReadInt16BE(_buf, Db172Map.SOUND);
            Light         = Db172Map.ReadInt16BE(_buf, Db172Map.LIGHT);
            FieldSet1     = Db172Map.ReadInt16BE(_buf, Db172Map.FIELD_SET_1);
            FieldSet2     = Db172Map.ReadInt16BE(_buf, Db172Map.FIELD_SET_2);
            ControlMotion = Db172Map.ReadInt16BE(_buf, Db172Map.CONTROL_MOTION);
            SpeedMotor1   = Db172Map.ReadInt16BE(_buf, Db172Map.SPEED_MOTOR_1);
            SpeedMotor2   = Db172Map.ReadInt16BE(_buf, Db172Map.SPEED_MOTOR_2);
            MoveMoveOut   = Db172Map.ReadInt16BE(_buf, Db172Map.MOVE_MOVE_OUT);
            SpeedMove     = Db172Map.ReadInt16BE(_buf, Db172Map.SPEED_MOVE);
            ModeLiftOut   = Db172Map.ReadInt16BE(_buf, Db172Map.MODE_LIFT_OUT);
            SpeedLift     = Db172Map.ReadInt16BE(_buf, Db172Map.SPEED_LIFT);
        }
        finally { _refreshing = false; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ── Private helpers ─────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    private void WriteBit(int byteOff, int bit, bool val)
    {
        if (_refreshing) return;
        try
        {
            var b = _mem.ReadDbArea(Db172Map.DbNumber, byteOff, 1);
            Db172Map.WriteBit(b, 0, bit, val);
            _mem.WriteDbArea(Db172Map.DbNumber, byteOff, b);
        }
        catch { }
    }

    private void WriteInt16(int byteOff, short val)
    {
        if (_refreshing) return;
        try
        {
            var b = new byte[2];
            Db172Map.WriteInt16BE(b, 0, val);
            _mem.WriteDbArea(Db172Map.DbNumber, byteOff, b);
        }
        catch { }
    }

    private void WriteInt32(int byteOff, int val)
    {
        if (_refreshing) return;
        try
        {
            var b = new byte[4];
            Db172Map.WriteInt32BE(b, 0, val);
            _mem.WriteDbArea(Db172Map.DbNumber, byteOff, b);
        }
        catch { }
    }

    private void WriteFloat(int byteOff, float val)
    {
        if (_refreshing) return;
        try
        {
            var b = new byte[4];
            Db172Map.WriteFloatBE(b, 0, val);
            _mem.WriteDbArea(Db172Map.DbNumber, byteOff, b);
        }
        catch { }
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T val, [CallerMemberName] string? prop = null)
    {
        if (Equals(field, val)) return;
        field = val;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <returns>true if value actually changed (to trigger write-back)</returns>
    private bool SetInput<T>(ref T field, T val, [CallerMemberName] string? prop = null)
    {
        if (Equals(field, val)) return false;
        field = val;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        return true;
    }

    private bool SetOutput<T>(ref T field, T val, [CallerMemberName] string? prop = null)
        => SetInput(ref field, val, prop);
}
