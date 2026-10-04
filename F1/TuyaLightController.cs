using System.Diagnostics;
using System.IO;

namespace F1_Redefined.F1;

public sealed class TuyaLightController(
    string scriptPath,
    string deviceId,
    string deviceIp,
    string localKey) : IGoveeLightController
{
    private readonly string _scriptPath = scriptPath;
    private readonly string _deviceId = deviceId;
    private readonly string _deviceIp = deviceIp;
    private readonly string _localKey = localKey;

    public event Action<string>? Log;

    public Task SetColorAsync(string deviceId, byte r, byte g, byte b) =>
        RunAsync("color", r.ToString(), g.ToString(), b.ToString());

    public Task SetIdleAsync(string deviceId) => RunAsync("off");

    private async Task RunAsync(string command, params string[] values)
    {
        if (!File.Exists(_scriptPath))
        {
            Log?.Invoke($"[Tuya] Missing bridge script: {_scriptPath}");
            return;
        }

        var arguments = $"-3 \"{_scriptPath}\" {command} {string.Join(" ", values)}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "py",
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(_scriptPath)!,
            Environment =
            {
                ["TUYA_DEVICE_ID"] = _deviceId,
                ["TUYA_DEVICE_IP"] = _deviceIp,
                ["TUYA_LOCAL_KEY"] = _localKey
            },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log?.Invoke("[Tuya] Could not start Python bridge.");
                return;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output)) Log?.Invoke($"[Tuya] {output.Trim()}");
            if (process.ExitCode != 0) Log?.Invoke($"[Tuya] {error.Trim()}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[Tuya] Could not run Python bridge: {ex.Message}");
        }
    }
}
