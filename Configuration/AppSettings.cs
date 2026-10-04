using System.IO;

namespace F1_Redefined.Configuration;

public sealed record AppSettings(
    string SofiaDeviceId,
    string? BurgasDeviceId,
    string GamingBarsDeviceId,
    string SofiaIpAddress,
    string? BurgasIpAddress,
    string GamingBarsIpAddress,
    string TuyaLocalKey,
    string UndercutF1ApiUrl)
{
    public static AppSettings Load()
    {
        DotEnv.Load();

        return new AppSettings(
            Required("GOVEE_SOFIA_DEVICE_ID"),
            Optional("GOVEE_BURGAS_DEVICE_ID"),
            Required("TUYA_DEVICE_ID"),
            Required("GOVEE_SOFIA_IP"),
            Optional("GOVEE_BURGAS_IP"),
            Required("TUYA_DEVICE_IP"),
            Required("TUYA_LOCAL_KEY"),
            Environment.GetEnvironmentVariable("UNDERCUT_F1_API_URL") ?? "http://localhost:61937/");
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing required environment setting: {name}");

    private static string? Optional(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : null;
}

internal static class DotEnv
{
    public static void Load()
    {
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, ".env"),
            Path.Combine(Environment.CurrentDirectory, ".env")
        }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

                var separator = trimmed.IndexOf('=');
                if (separator <= 0) continue;

                var name = trimmed[..separator].Trim();
                var value = trimmed[(separator + 1)..].Trim().Trim('"');
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
