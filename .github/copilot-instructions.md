# Copilot instructions for F1-Redefined

## Build and validation

Run commands from the repository root.

- Restore dependencies: `dotnet restore`
- Build the WPF application: `dotnet build .\F1-Redefined.csproj`
- Run the application: `dotnet run --project .\F1-Redefined.csproj`
- Validate the Python bridge syntax: `python -m py_compile .\tuya_control.py`

There is currently no test project, test suite, or configured lint command. There is therefore no single-test command. When adding tests, keep them separate from the WPF executable project and document the test runner command here.

The project targets `net10.0-windows` and uses WPF, so building and running requires a Windows environment with a compatible .NET 10 SDK.

## Architecture

The application has three cooperating layers:

1. **WPF presentation**  
   [`MainWindow`](../MainWindow.xaml.cs) owns the window and translates service callbacks into log text, flag text, and indicator colors. It should not contain device IDs, IP addresses, protocol commands, or polling logic.

2. **Smart-home composition and configuration**  
   [`SmartHomeService`](../SmartHome/SmartHomeService.cs) creates the light controllers, combines them by device ID, starts UndercutF1 when needed, and connects the flag watcher to the UI callbacks. [`AppSettings`](../Configuration/AppSettings.cs) loads required values from `.env` or process environment variables.

3. **Integration and event processing**  
   The `F1` namespace contains protocol adapters and the flag pipeline:
   - [`LocalF1FlagWatcher`](../F1/LocalF1FlagWatcher.cs) polls UndercutF1's local API at `data/RaceControlMessages/latest`, finds and normalizes race-control flags, and emits color commands.
   - [`IGoveeLightController`](../F1/IGoveeLightController.cs) is the common light-controller contract.
   - [`CompositeLightController`](../F1/CompositeLightController.cs) routes a device ID to its configured adapter.
   - [`GoveeLanLightController`](../F1/GoveeLanLightController.cs) sends Govee LAN UDP discovery/control traffic.
   - [`TuyaLightController`](../F1/TuyaLightController.cs) launches [`tuya_control.py`](../tuya_control.py), passing Tuya connection values through the child process environment.

The Python bridge uses TinyTuya and reads `TUYA_DEVICE_ID`, `TUYA_DEVICE_IP`, and `TUYA_LOCAL_KEY` from its environment or a local `.env`. It must remain copied to the build output by the project file because the C# controller invokes the output-directory copy.

## Repository-specific conventions

- Keep secrets and local network values in `.env`; use [`.env.example`](../.env.example) for names and placeholders. Never add real keys, local keys, device credentials, or private network details to source or committed JSON.
- Device IDs are configuration keys used by `CompositeLightController`; if a device is added or renamed, update the environment variables and the controller map in `SmartHomeService` together.
- Govee device IDs are MAC-like identifiers and may use fixed IPv4 addresses from configuration. Govee LAN control requires LAN Control enabled on the device and uses UDP discovery/control ports defined in `GoveeLanLightController`.
- The watcher emits only normalized flag names (`YELLOW`, `DOUBLE YELLOW`, `RED`, `GREEN`, and `CHEQUERED`). Preserve this vocabulary when changing parsing or UI behavior.
- Integration classes report operational failures through their `Log` events rather than throwing into the WPF event handlers. Preserve those log prefixes (`[App]`, `[GoveeLan]`, `[Tuya]`, `[LocalF1FlagWatcher]`) when adding diagnostics.
- Long-running work is asynchronous and cancellation-aware. `LocalF1FlagWatcher` is stopped through `IDisposable` when the main window closes; new background integration work must follow the same lifecycle.
- `GoveeLanLightController` and `TuyaLightController` both implement the shared interface, so new smart-home providers should implement `IGoveeLightController` and be wired in the composition layer rather than in `MainWindow`.
- The Tuya bridge's command interface is `python tuya_control.py color <red> <green> <blue>` or `python tuya_control.py off`; color values are bytes in the range 0-255.
- Avoid committing generated output (`bin/`, `obj/`), local credentials/state files, `.env`, or Python cache files; the existing `.gitignore` defines these repository boundaries.
