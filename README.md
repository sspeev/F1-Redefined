# F1 Flag Lights

F1 Flag Lights is a Windows WPF application that mirrors live F1 race-control
flags to local smart lights.

This is a personal project built for the author's own setup. It is not an
official Govee or Tuya application, and it is not intended to support every
Govee or Tuya device.

It reads race-control messages from a locally running
[UndercutF1](https://github.com/JustAman62/undercut-f1) instance and controls:

- A Govee light over the Govee LAN UDP protocol.
- Tuya gaming bars through a small Python/TinyTuya bridge.
- An optional second Govee device.

The application is intended for a local network. Device credentials and local
network addresses are supplied through environment variables and are never
required in the repository.

## Features

- Polls UndercutF1's local API every three seconds.
- Detects the newest explicit race-control flag.
- Controls all configured lights together.
- Shows the current flag and activity log in the WPF interface.
- Starts UndercutF1 with its API enabled when it is not already running.
- Uses the generated application icon from `Resources/Images/app.ico`.

### Flag mapping

| Race-control state | Light color |
|---|---|
| `YELLOW` | Yellow |
| `DOUBLE YELLOW` | Yellow |
| `RED` | Red |
| `GREEN` | Green |
| `CLEAR` | White |
| `CHEQUERED` | White |

After a `CLEAR` event, the application schedules a five-minute idle transition.
If no newer event arrives, the lights remain white.

## Requirements

- Windows
- .NET 10 SDK
- Python 3
- The `tinytuya` Python package
- Govee devices with LAN Control enabled
- A Tuya-compatible light or gaming-bar device
- [UndercutF1](https://github.com/JustAman62/undercut-f1)

Compatibility is device-specific. Govee support requires a model that exposes
the expected Govee LAN protocol and has LAN Control enabled. Tuya support
requires a device whose local protocol, datapoint layout, color mode, firmware,
and TinyTuya compatibility match the tested gaming-bar setup. Other Govee or
Tuya devices may require different commands, datapoints, authentication, or
additional implementation work.

The project targets `net10.0-windows` and uses WPF, so it must be built and
run on Windows with a compatible .NET SDK.

## Setup

### 1. Install Python dependencies

From the repository root:

```powershell
python -m pip install tinytuya
```

### 2. Install UndercutF1

Install UndercutF1 separately and make sure `undercutf1.exe` is available on
your `PATH`.

The application starts it with:

```text
undercutf1.exe --with-api
```

If UndercutF1 is already running, the application reuses the existing process.
After starting a session in UndercutF1, use its normal controls to begin live
timing.

### 3. Configure local devices

Copy the example configuration:

```powershell
Copy-Item .env.example .env
```

Edit `.env` and fill in the local values:

```env
# Required Govee device
GOVEE_SOFIA_DEVICE_ID=
GOVEE_SOFIA_IP=

# Optional second Govee device
GOVEE_BURGAS_DEVICE_ID=
GOVEE_BURGAS_IP=

# Required Tuya device
TUYA_DEVICE_ID=
TUYA_DEVICE_IP=
TUYA_LOCAL_KEY=

# Usually the default is sufficient
UNDERCUT_F1_API_URL=http://localhost:61937/
```

For Govee, `*_DEVICE_ID` is the device's MAC-like LAN identifier. LAN Control
must be enabled in the Govee Home app.

Never commit `.env`, device exports, local keys, or generated device-state
files. The repository `.gitignore` excludes these files.

## Build and run

From the repository root:

```powershell
dotnet restore
dotnet build .\F1-Redefined.csproj
dotnet run --project .\F1-Redefined.csproj
```

To validate the Python bridge:

```powershell
python -m py_compile .\tuya_control.py
```

The application window displays startup diagnostics and flag changes in the
activity log.

## Architecture

```text
Views/MainWindow.xaml
    └── Services/SmartHomeService.cs
        ├── Services/LocalF1FlagWatcher.cs
        │   └── HTTP GET: /data/RaceControlMessages/latest
        ├── Services/GoveeLanLightController.cs
        ├── Services/TuyaLightController.cs
        │   └── tuya_control.py
        └── Services/CompositeLightController.cs
```

### Main components

- `Services/LocalF1FlagWatcher.cs` polls UndercutF1, extracts the newest
  explicit flag, and maps it to an RGB color.
- `Services/GoveeLanLightController.cs` sends LAN UDP commands to Govee
  devices on port `4003`.
- `Services/TuyaLightController.cs` launches the Python bridge with the Tuya
  device ID, IP address, and local key supplied through the child-process
  environment.
- `tuya_control.py` uses TinyTuya's `BulbDevice.set_colour()` helper to send
  correctly encoded Tuya HSV16 color values.
- `Services/CompositeLightController.cs` routes each configured device ID to
  the correct provider.
- `Models/AppSettings.cs` loads `.env` values and validates required settings.

## Troubleshooting

### The app starts but no flags are detected

Check that:

1. UndercutF1 is running.
2. Its local API is enabled.
3. A live or simulated session has been started.
4. `UNDERCUT_F1_API_URL` points to the correct API address.

The expected endpoint is:

```text
GET http://localhost:61937/data/RaceControlMessages/latest
```

### Govee does not respond

Check that:

- The device is on the same local network.
- LAN Control is enabled.
- The configured device ID is the device's LAN MAC-like identifier.
- The configured IP address is current.
- UDP port `4003` is not blocked.

### Tuya gaming bars do not respond

Check that:

- `TUYA_DEVICE_ID`, `TUYA_DEVICE_IP`, and `TUYA_LOCAL_KEY` are populated.
- Python can import TinyTuya:

  ```powershell
  python -c "import tinytuya; print(tinytuya.__version__)"
  ```

- The device is reachable from the computer.
- The Python bridge runs successfully:

  ```powershell
  python .\tuya_control.py color 255 0 0
  ```

Do not paste local keys or other credentials into issues, pull requests, or
logs.

## Third-party software

This project integrates with
[UndercutF1](https://github.com/JustAman62/undercut-f1), an open-source F1
live-timing client licensed under the
[GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html).

This project communicates with UndercutF1 through its local API and is not
affiliated with or endorsed by the UndercutF1 project or its authors.

The Tuya bridge uses [TinyTuya](https://github.com/jasonacox/tinytuya).
TinyTuya is installed separately through Python's package manager and is not
vendored into this repository.

## Security and privacy

Do not publish:

- `.env`
- Tuya local keys
- Device exports such as `devices.json` or `tuya-raw.json`
- Generated runtime state
- Private IP addresses unless you intentionally want to document an example

Use `.env.example` for configuration names and empty placeholders only.

## License

Unless otherwise stated in a file, the application code in this repository is
provided without an explicit license. Add a project license before distributing
the application publicly.
