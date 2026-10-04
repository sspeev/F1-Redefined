import argparse
import os
import sys
from pathlib import Path
import tinytuya


def load_env():
    for path in (Path(__file__).with_name(".env"), Path.cwd() / ".env"):
        if not path.exists():
            continue
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            name, value = line.split("=", 1)
            os.environ.setdefault(name.strip(), value.strip().strip('"'))


def load_device():
    device_id = os.environ["TUYA_DEVICE_ID"]
    return tinytuya.BulbDevice(
        device_id,
        os.environ["TUYA_DEVICE_IP"],
        os.environ["TUYA_LOCAL_KEY"],
        version=3.3,
    )


def main():
    load_env()
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("color", "off"))
    parser.add_argument("values", nargs="*", type=int)
    args = parser.parse_args()
    device = load_device()

    if args.command == "color":
        if len(args.values) != 3:
            raise ValueError("color requires red green blue values")
        if any(value < 0 or value > 255 for value in args.values):
            raise ValueError("color values must be between 0 and 255")
        result = device.set_colour(*args.values)
    else:
        result = device.set_value(20, False)

    if result:
        print(result)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
