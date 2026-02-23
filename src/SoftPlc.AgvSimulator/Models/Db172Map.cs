using System;

namespace SoftPlc.AgvSimulator.Models;

/// <summary>
/// DB172 byte-offset map for AGV signals (S7 big-endian layout).
///
/// INPUT area  (byte  0–59): AGV → SoftPLC  — simulator WRITES, S7 client reads
/// OUTPUT area (byte 60–95): SoftPLC → AGV  — S7 client WRITES, simulator reads/displays
///
/// Total used: 84 bytes  |  DB size: 256 bytes
/// </summary>
public static class Db172Map
{
    public const int DbNumber = 172;
    public const int DbSize   = 256;

    // ── INPUT area ─────────────────────────────────────────────────────────
    /// <summary>Byte 0 – bool flags: bit0=ON_OFF, bit1=START, bit2=CLEAR_ERROR, bit3=SAFETY_RELAY</summary>
    public const int FLAGS_IN        = 0;
    // byte 1 = reserved
    /// <summary>Bytes 2–5  – float (big-endian): Điện áp pin (V)</summary>
    public const int VALUE_VOLTAGE   = 2;
    /// <summary>Bytes 6–9  – float (big-endian): Dòng sạc (A)</summary>
    public const int VALUE_CURRENT   = 6;
    /// <summary>Bytes 10–11 – int16 (big-endian): SIGNAL_EMGS bit array</summary>
    public const int SIGNAL_EMGS     = 10;
    /// <summary>Bytes 12–13 – int16: SIGNAL_BUMPER bit array</summary>
    public const int SIGNAL_BUMPER   = 12;
    /// <summary>Bytes 14–15 – int16: STATUS_SENSOR bit array</summary>
    public const int STATUS_SENSOR   = 14;
    /// <summary>Bytes 16–17 – int16: SAFETY_ZONE_1 bit array</summary>
    public const int SAFETY_ZONE_1   = 16;
    /// <summary>Bytes 18–19 – int16: SAFETY_ZONE_2 bit array</summary>
    public const int SAFETY_ZONE_2   = 18;
    /// <summary>Bytes 20–21 – int16: STATUS_RFID  (-1=lỗi đọc, 0=ngoài vùng, 1=trong vùng)</summary>
    public const int STATUS_RFID     = 20;
    /// <summary>Bytes 22–23 – int16: CODE_RFID hiện tại</summary>
    public const int CODE_RFID       = 22;
    /// <summary>Bytes 24–25 – int16: STATUS_MAGLINE (-1=lỗi, 1=bình thường)</summary>
    public const int STATUS_MAGLINE  = 24;
    /// <summary>Bytes 26–27 – int16: VALUE_MAGLINE bit array</summary>
    public const int VALUE_MAGLINE   = 26;
    /// <summary>Bytes 28–29 – int16: STATUS_MOTOR_1 (-1=chưa kết nối, 1=bình thường)</summary>
    public const int STATUS_MOTOR_1  = 28;
    /// <summary>Bytes 30–31 – int16: WARN_MOTOR_1</summary>
    public const int WARN_MOTOR_1    = 30;
    /// <summary>Bytes 32–33 – int16: ERROR_MOTOR_1</summary>
    public const int ERROR_MOTOR_1   = 32;
    /// <summary>Bytes 34–35 – int16: SPEED_1 (rpm)</summary>
    public const int SPEED_1         = 34;
    /// <summary>Bytes 36–39 – int32 (big-endian): VALUE_ENCODER_1</summary>
    public const int VALUE_ENCODER_1 = 36;
    /// <summary>Bytes 40–41 – int16: STATUS_MOTOR_2</summary>
    public const int STATUS_MOTOR_2  = 40;
    /// <summary>Bytes 42–43 – int16: WARN_MOTOR_2</summary>
    public const int WARN_MOTOR_2    = 42;
    /// <summary>Bytes 44–45 – int16: ERROR_MOTOR_2</summary>
    public const int ERROR_MOTOR_2   = 44;
    /// <summary>Bytes 46–47 – int16: SPEED_2 (rpm)</summary>
    public const int SPEED_2         = 46;
    /// <summary>Bytes 48–51 – int32: VALUE_ENCODER_2</summary>
    public const int VALUE_ENCODER_2 = 48;
    /// <summary>Bytes 52–53 – int16: MOVE_MOVE (mode di chuyển AGV đang chấp hành, 0-13)</summary>
    public const int MOVE_MOVE_IN    = 52;
    /// <summary>Bytes 54–55 – int16: STATUS_MOVE</summary>
    public const int STATUS_MOVE     = 54;
    /// <summary>Bytes 56–57 – int16: MODE_LIFT_IN (0=dừng, 1=nâng, 2=hạ – đang chấp hành)</summary>
    public const int MODE_LIFT_IN    = 56;
    /// <summary>Bytes 58–59 – int16: STATUS_LIFT (0=dừng,1=đang,2=timeout,3-4=lỗi,5=hoàn thành nâng,6=hoàn thành hạ)</summary>
    public const int STATUS_LIFT     = 58;

    // ── OUTPUT area ────────────────────────────────────────────────────────
    /// <summary>Byte 60 – bool flags: bit0=WRITE_EMG, bit1=RESET, bit2=CHARGE, bit3=BRAKE</summary>
    public const int FLAGS_OUT       = 60;
    // byte 61 = reserved
    /// <summary>Bytes 62–63 – int16: SOUND (0=tắt, 1-10=mode âm thanh)</summary>
    public const int SOUND           = 62;
    /// <summary>Bytes 64–65 – int16: LIGHT (0=tắt, 1-10=mode led)</summary>
    public const int LIGHT           = 64;
    /// <summary>Bytes 66–67 – int16: FIELD_SET_1 (0=tắt, 1-10=vùng yêu cầu)</summary>
    public const int FIELD_SET_1     = 66;
    /// <summary>Bytes 68–69 – int16: FIELD_SET_2</summary>
    public const int FIELD_SET_2     = 68;
    /// <summary>Bytes 70–71 – int16: CONTROL_MOTION (1=mode cụ thể, 2=vận tốc từ PC)</summary>
    public const int CONTROL_MOTION  = 70;
    /// <summary>Bytes 72–73 – int16: SPEED_MOTOR_1 – vận tốc bánh trái</summary>
    public const int SPEED_MOTOR_1   = 72;
    /// <summary>Bytes 74–75 – int16: SPEED_MOTOR_2 – vận tốc bánh phải</summary>
    public const int SPEED_MOTOR_2   = 74;
    /// <summary>Bytes 76–77 – int16: MOVE_MOVE_OUT (0-13, mode di chuyển yêu cầu)</summary>
    public const int MOVE_MOVE_OUT   = 76;
    /// <summary>Bytes 78–79 – int16: SPEED_MOVE – vận tốc di chuyển AGV</summary>
    public const int SPEED_MOVE      = 78;
    /// <summary>Bytes 80–81 – int16: MODE_LIFT_OUT (0=dừng/reset, 1=nâng, 2=hạ)</summary>
    public const int MODE_LIFT_OUT   = 80;
    /// <summary>Bytes 82–83 – int16: SPEED_LIFT – vận tốc bàn nâng</summary>
    public const int SPEED_LIFT      = 82;

    // ── Encode/decode helpers (S7 big-endian) ─────────────────────────────

    public static short  ReadInt16BE(byte[] buf, int offset)
        => (short)((buf[offset] << 8) | buf[offset + 1]);

    public static int    ReadInt32BE(byte[] buf, int offset)
        => (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];

    public static float  ReadFloatBE(byte[] buf, int offset)
    {
        var b = new byte[] { buf[offset + 3], buf[offset + 2], buf[offset + 1], buf[offset] };
        return BitConverter.ToSingle(b, 0);
    }

    public static bool   ReadBit(byte[] buf, int offset, int bit)
        => (buf[offset] & (1 << bit)) != 0;

    public static void   WriteInt16BE(byte[] buf, int offset, short val)
    {
        buf[offset]     = (byte)(val >> 8);
        buf[offset + 1] = (byte)(val & 0xFF);
    }

    public static void   WriteInt32BE(byte[] buf, int offset, int val)
    {
        buf[offset]     = (byte)(val >> 24);
        buf[offset + 1] = (byte)(val >> 16);
        buf[offset + 2] = (byte)(val >>  8);
        buf[offset + 3] = (byte)(val & 0xFF);
    }

    public static void   WriteFloatBE(byte[] buf, int offset, float val)
    {
        var b = BitConverter.GetBytes(val);
        buf[offset]     = b[3];
        buf[offset + 1] = b[2];
        buf[offset + 2] = b[1];
        buf[offset + 3] = b[0];
    }

    public static void   WriteBit(byte[] buf, int offset, int bit, bool val)
    {
        if (val) buf[offset] = (byte)(buf[offset] |  (1 << bit));
        else     buf[offset] = (byte)(buf[offset] & ~(1 << bit));
    }
}
