using System.Diagnostics;
using System.IO;
using System.Net.Http;
using F1_Redefined.Configuration;

namespace F1_Redefined.Services;

public sealed class SmartHomeService(Action<string> log, Action<string> flagChanged) : IDisposable
{
    private readonly Action<string> _log = log;
    private readonly Action<string> _flagChanged = flagChanged;
    private LocalF1FlagWatcher? _watcher;

    public void Start()
    {
        var settings = AppSettings.Load();
        var govee = new GoveeLanLightController();
        govee.Log += _log;
        govee.SetFixedIp(settings.SofiaDeviceId, settings.SofiaIpAddress);

        if (settings.BurgasDeviceId is not null)
        {
            govee.SetFixedIp(settings.BurgasDeviceId, settings.BurgasIpAddress);
        }

        var tuya = new TuyaLightController(
            Path.Combine(AppContext.BaseDirectory, "tuya_control.py"),
            settings.GamingBarsDeviceId,
            settings.GamingBarsIpAddress,
            settings.TuyaLocalKey);
        tuya.Log += _log;

        var controllers = new Dictionary<string, IGoveeLightController>
        {
            [settings.GamingBarsDeviceId] = tuya
        };

        controllers[settings.SofiaDeviceId] = govee;

        if (settings.BurgasDeviceId is not null)
        {
            controllers[settings.BurgasDeviceId] = govee;
        }

        var lights = new CompositeLightController(controllers);

        StartUndercutF1();
        var deviceIds = new[] { settings.SofiaDeviceId, settings.BurgasDeviceId, settings.GamingBarsDeviceId }
            .Where(static id => id is not null)
            .Select(static id => id!)
            .ToArray();
        _watcher = new LocalF1FlagWatcher(
            lights,
            deviceIds,
            new HttpClient { BaseAddress = new Uri(settings.UndercutF1ApiUrl) });
        _watcher.Log += _log;
        _watcher.FlagChanged += _flagChanged;
        _watcher.Start();
    }

    public void Dispose() => _watcher?.Dispose();

    private void StartUndercutF1()
    {
        if (Process.GetProcessesByName("undercutf1").Length > 0)
        {
            _log("[App] UndercutF1 is already running.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "undercutf1.exe",
                Arguments = "--with-api",
                UseShellExecute = true
            });
            _log("[App] Started UndercutF1 with the API enabled. Press S, then L in its window to start live timing.");
        }
        catch (Exception ex)
        {
            _log($"[App] Could not start UndercutF1. Install it or add it to PATH: {ex.Message}");
        }
    }
}
