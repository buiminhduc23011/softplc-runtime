"""
test_agv_db172.py
-----------------
Đọc toàn bộ DB172 từ SoftPlc.AgvSimulator qua giao thức S7 (snap7).

Cài đặt:
    pip install python-snap7

Khởi động SoftPlc.AgvSimulator trước, sau đó chạy:
    python test_agv_db172.py [--ip 127.0.0.1] [--port 102] [--watch]
"""

import argparse
import struct
import time
import sys

try:
    import snap7
    from snap7.util import get_bool
except ImportError:
    print("Lỗi: chưa cài snap7.  Chạy:  pip install python-snap7")
    sys.exit(1)

# ─── Hằng số DB172 ────────────────────────────────────────────────────────────
DB_NUMBER = 172
DB_SIZE   = 256

# INPUT area (byte 0–59)  – AGV → SoftPLC
FLAGS_IN        = 0    # byte   – bit0=ON_OFF, bit1=START, bit2=CLEAR_ERROR, bit3=SAFETY_RELAY
VALUE_VOLTAGE   = 2    # float  – Điện áp pin (V)
VALUE_CURRENT   = 6    # float  – Dòng sạc (A)
SIGNAL_EMGS     = 10   # int16  – bit array
SIGNAL_BUMPER   = 12   # int16  – bit array
STATUS_SENSOR   = 14   # int16  – bit array
SAFETY_ZONE_1   = 16   # int16  – bit array
SAFETY_ZONE_2   = 18   # int16  – bit array
STATUS_RFID     = 20   # int16  – (-1=lỗi, 0=ngoài, 1=trong vùng)
CODE_RFID       = 22   # int16  – mã RFID hiện tại
STATUS_MAGLINE  = 24   # int16  – (-1=lỗi, 1=bình thường)
VALUE_MAGLINE   = 26   # int16  – bit array
STATUS_MOTOR_1  = 28   # int16  – (-1=chưa kết nối, 1=bình thường)
WARN_MOTOR_1    = 30   # int16
ERROR_MOTOR_1   = 32   # int16
SPEED_1         = 34   # int16  – rpm
VALUE_ENCODER_1 = 36   # int32
STATUS_MOTOR_2  = 40   # int16
WARN_MOTOR_2    = 42   # int16
ERROR_MOTOR_2   = 44   # int16
SPEED_2         = 46   # int16  – rpm
VALUE_ENCODER_2 = 48   # int32
MOVE_MOVE_IN    = 52   # int16  – mode di chuyển đang chấp hành (0-13)
STATUS_MOVE     = 54   # int16
MODE_LIFT_IN    = 56   # int16  – 0=dừng, 1=nâng, 2=hạ (đang chấp hành)
STATUS_LIFT     = 58   # int16  – 0=dừng,1=đang,2=timeout,3-4=lỗi,5=nâng xong,6=hạ xong

# OUTPUT area (byte 60–83) – SoftPLC → AGV
FLAGS_OUT       = 60   # byte   – bit0=WRITE_EMG, bit1=RESET, bit2=CHARGE, bit3=BRAKE
SOUND           = 62   # int16  – 0=tắt, 1-10=mode âm thanh
LIGHT           = 64   # int16  – 0=tắt, 1-10=mode led
FIELD_SET_1     = 66   # int16
FIELD_SET_2     = 68   # int16
CONTROL_MOTION  = 70   # int16  – 1=mode cụ thể, 2=vận tốc từ PC
SPEED_MOTOR_1   = 72   # int16  – vận tốc bánh trái
SPEED_MOTOR_2   = 74   # int16  – vận tốc bánh phải
MOVE_MOVE_OUT   = 76   # int16  – mode di chuyển yêu cầu (0-13)
SPEED_MOVE      = 78   # int16  – vận tốc di chuyển
MODE_LIFT_OUT   = 80   # int16  – 0=dừng/reset, 1=nâng, 2=hạ
SPEED_LIFT      = 82   # int16  – vận tốc bàn nâng


# ─── Helpers (S7 big-endian) ──────────────────────────────────────────────────

def read_int16(buf: bytearray, offset: int) -> int:
    """Đọc int16 big-endian (có dấu)."""
    val = struct.unpack_from(">h", buf, offset)[0]
    return val

def read_int32(buf: bytearray, offset: int) -> int:
    """Đọc int32 big-endian (có dấu)."""
    return struct.unpack_from(">i", buf, offset)[0]

def read_float(buf: bytearray, offset: int) -> float:
    """Đọc float32 big-endian (IEEE 754)."""
    return struct.unpack_from(">f", buf, offset)[0]

def read_bit(buf: bytearray, offset: int, bit: int) -> bool:
    """Đọc 1 bit trong byte tại offset."""
    return bool(buf[offset] & (1 << bit))

def fmt_bit(val: bool) -> str:
    return "ON " if val else "OFF"


# ─── In raw bytes ─────────────────────────────────────────────────────────────

def print_raw(buf: bytearray) -> None:
    """In toàn bộ DB172 dưới dạng hex + decimal, 16 byte mỗi dòng."""
    print(f"RAW DB{DB_NUMBER} ({len(buf)} bytes):")
    for i in range(0, len(buf), 16):
        chunk = buf[i:i+16]
        hex_part = " ".join(f"{b:02X}" for b in chunk)
        dec_part = " ".join(f"{b:3d}" for b in chunk)
        print(f"  [{i:3d}]  {hex_part:<47}  | {dec_part}")
    print()


# ─── Phân tích và in DB172 ────────────────────────────────────────────────────

def parse_and_print(buf: bytearray) -> None:
    sep = "─" * 60

    print(sep)
    print("▶  DB172 – INPUT  (AGV → SoftPLC)")
    print(sep)

    # Flags byte 0
    on_off       = read_bit(buf, FLAGS_IN, 0)
    start        = read_bit(buf, FLAGS_IN, 1)
    clear_error  = read_bit(buf, FLAGS_IN, 2)
    safety_relay = read_bit(buf, FLAGS_IN, 3)
    print(f"  FLAGS_IN    byte[{FLAGS_IN}]  ON_OFF={fmt_bit(on_off)}  START={fmt_bit(start)}"
          f"  CLEAR_ERROR={fmt_bit(clear_error)}  SAFETY_RELAY={fmt_bit(safety_relay)}")

    # Floats
    voltage = read_float(buf, VALUE_VOLTAGE)
    current = read_float(buf, VALUE_CURRENT)
    print(f"  VALUE_VOLTAGE byte[{VALUE_VOLTAGE}..{VALUE_VOLTAGE+3}]  {voltage:.3f} V")
    print(f"  VALUE_CURRENT byte[{VALUE_CURRENT}..{VALUE_CURRENT+3}]  {current:.3f} A")

    # Signals
    print(f"  SIGNAL_EMGS   byte[{SIGNAL_EMGS}..{SIGNAL_EMGS+1}]  0x{read_int16(buf, SIGNAL_EMGS) & 0xFFFF:04X}  "
          f"({read_int16(buf, SIGNAL_EMGS)})")
    print(f"  SIGNAL_BUMPER byte[{SIGNAL_BUMPER}..{SIGNAL_BUMPER+1}]  0x{read_int16(buf, SIGNAL_BUMPER) & 0xFFFF:04X}"
          f"  ({read_int16(buf, SIGNAL_BUMPER)})")
    print(f"  STATUS_SENSOR byte[{STATUS_SENSOR}..{STATUS_SENSOR+1}]  0x{read_int16(buf, STATUS_SENSOR) & 0xFFFF:04X}")
    print(f"  SAFETY_ZONE_1 byte[{SAFETY_ZONE_1}..{SAFETY_ZONE_1+1}]  0x{read_int16(buf, SAFETY_ZONE_1) & 0xFFFF:04X}")
    print(f"  SAFETY_ZONE_2 byte[{SAFETY_ZONE_2}..{SAFETY_ZONE_2+1}]  0x{read_int16(buf, SAFETY_ZONE_2) & 0xFFFF:04X}")

    # RFID
    print(f"  STATUS_RFID   byte[{STATUS_RFID}..{STATUS_RFID+1}]   {read_int16(buf, STATUS_RFID)}"
          "  (-1=lỗi, 0=ngoài, 1=trong)")
    print(f"  CODE_RFID     byte[{CODE_RFID}..{CODE_RFID+1}]   {read_int16(buf, CODE_RFID)}")

    # MAGLINE
    print(f"  STATUS_MAGLINE byte[{STATUS_MAGLINE}..{STATUS_MAGLINE+1}]  {read_int16(buf, STATUS_MAGLINE)}"
          "  (-1=lỗi, 1=OK)")
    print(f"  VALUE_MAGLINE  byte[{VALUE_MAGLINE}..{VALUE_MAGLINE+1}]  0x{read_int16(buf, VALUE_MAGLINE) & 0xFFFF:04X}")

    # Motor 1
    print(f"  MOTOR-1  STATUS={read_int16(buf, STATUS_MOTOR_1)}"
          f"  WARN=0x{read_int16(buf, WARN_MOTOR_1) & 0xFFFF:04X}"
          f"  ERROR=0x{read_int16(buf, ERROR_MOTOR_1) & 0xFFFF:04X}"
          f"  SPEED={read_int16(buf, SPEED_1)} rpm"
          f"  ENC={read_int32(buf, VALUE_ENCODER_1)}")

    # Motor 2
    print(f"  MOTOR-2  STATUS={read_int16(buf, STATUS_MOTOR_2)}"
          f"  WARN=0x{read_int16(buf, WARN_MOTOR_2) & 0xFFFF:04X}"
          f"  ERROR=0x{read_int16(buf, ERROR_MOTOR_2) & 0xFFFF:04X}"
          f"  SPEED={read_int16(buf, SPEED_2)} rpm"
          f"  ENC={read_int32(buf, VALUE_ENCODER_2)}")

    # Motion
    print(f"  MOVE_MOVE_IN={read_int16(buf, MOVE_MOVE_IN)}"
          f"  STATUS_MOVE={read_int16(buf, STATUS_MOVE)}"
          f"  MODE_LIFT_IN={read_int16(buf, MODE_LIFT_IN)}"
          f"  STATUS_LIFT={read_int16(buf, STATUS_LIFT)}")

    print()
    print(sep)
    print("▶  DB172 – OUTPUT  (SoftPLC → AGV)")
    print(sep)

    # Flags byte 60
    write_emg = read_bit(buf, FLAGS_OUT, 0)
    reset     = read_bit(buf, FLAGS_OUT, 1)
    charge    = read_bit(buf, FLAGS_OUT, 2)
    brake     = read_bit(buf, FLAGS_OUT, 3)
    print(f"  FLAGS_OUT   byte[{FLAGS_OUT}]  WRITE_EMG={fmt_bit(write_emg)}"
          f"  RESET={fmt_bit(reset)}  CHARGE={fmt_bit(charge)}  BRAKE={fmt_bit(brake)}")

    print(f"  SOUND         byte[{SOUND}..{SOUND+1}]   {read_int16(buf, SOUND)}")
    print(f"  LIGHT         byte[{LIGHT}..{LIGHT+1}]   {read_int16(buf, LIGHT)}")
    print(f"  FIELD_SET_1   byte[{FIELD_SET_1}..{FIELD_SET_1+1}]   {read_int16(buf, FIELD_SET_1)}")
    print(f"  FIELD_SET_2   byte[{FIELD_SET_2}..{FIELD_SET_2+1}]   {read_int16(buf, FIELD_SET_2)}")
    print(f"  CONTROL_MOTION byte[{CONTROL_MOTION}..{CONTROL_MOTION+1}]   {read_int16(buf, CONTROL_MOTION)}"
          "  (1=mode, 2=vel-PC)")
    print(f"  SPEED_MOTOR_1 byte[{SPEED_MOTOR_1}..{SPEED_MOTOR_1+1}]   {read_int16(buf, SPEED_MOTOR_1)}"
          "  (trái)")
    print(f"  SPEED_MOTOR_2 byte[{SPEED_MOTOR_2}..{SPEED_MOTOR_2+1}]   {read_int16(buf, SPEED_MOTOR_2)}"
          "  (phải)")
    print(f"  MOVE_MOVE_OUT  byte[{MOVE_MOVE_OUT}..{MOVE_MOVE_OUT+1}]   {read_int16(buf, MOVE_MOVE_OUT)}")
    print(f"  SPEED_MOVE     byte[{SPEED_MOVE}..{SPEED_MOVE+1}]   {read_int16(buf, SPEED_MOVE)}")
    print(f"  MODE_LIFT_OUT  byte[{MODE_LIFT_OUT}..{MODE_LIFT_OUT+1}]   {read_int16(buf, MODE_LIFT_OUT)}"
          "  (0=stop,1=up,2=down)")
    print(f"  SPEED_LIFT     byte[{SPEED_LIFT}..{SPEED_LIFT+1}]   {read_int16(buf, SPEED_LIFT)}")
    print()


# ─── Kết nối và đọc ───────────────────────────────────────────────────────────

def connect(ip: str, port: int) -> snap7.client.Client:
    """Kết nối tới SoftPlc S7 server."""
    client = snap7.client.Client()
    client.connect(ip, 0, 1, port)
    return client


def read_db172(client: snap7.client.Client) -> bytearray:
    """Đọc toàn bộ DB172 (DB_SIZE bytes)."""
    data = client.db_read(DB_NUMBER, 0, DB_SIZE)
    return bytearray(data)


# ─── Main ─────────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Đọc DB172 từ SoftPlc.AgvSimulator qua S7 (snap7)"
    )
    parser.add_argument("--ip",    default="127.0.0.1", help="IP của SoftPlc (mặc định: 127.0.0.1)")
    parser.add_argument("--port",  default=102, type=int, help="TCP port S7 (mặc định: 102)")
    parser.add_argument("--watch", action="store_true",
                        help="Liên tục đọc mỗi 1 giây (Ctrl+C để dừng)")
    parser.add_argument("--interval", default=1.0, type=float,
                        help="Khoảng thời gian giữa các lần đọc (giây, mặc định: 1.0)")
    args = parser.parse_args()

    print(f"Kết nối tới SoftPlc tại {args.ip}:{args.port} …")
    try:
        client = connect(args.ip, args.port)
    except Exception as e:
        print(f"Không kết nối được: {e}")
        sys.exit(1)

    print("Kết nối thành công!\n")

    try:
        if args.watch:
            while True:
                buf = read_db172(client)
                print(f"\033[H\033[J", end="")   # xoá màn hình (ANSI)
                print(f"[{time.strftime('%H:%M:%S')}]  DB{DB_NUMBER}  –  watch mode  (Ctrl+C để thoát)\n")
                print_raw(buf)
                parse_and_print(buf)
                time.sleep(args.interval)
        else:
            buf = read_db172(client)
            print_raw(buf)
            parse_and_print(buf)
    except KeyboardInterrupt:
        print("\nĐã dừng.")
    finally:
        client.disconnect()
        client.destroy()


if __name__ == "__main__":
    main()
