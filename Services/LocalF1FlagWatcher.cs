using System.Net.Http;
using System.Text.Json;

namespace F1_Redefined.Services;

/// <summary>
/// Polls a locally-running `undercutf1` instance's Data API for the latest
/// race control flag, and drives Govee lights to match.
///
/// Prerequisite: `undercutf1` must be installed and running with its API enabled
/// (config.json: { "apiEnabled": true }), and a live/simulated session started via
/// its TUI (S -> L). See https://github.com/JustAman62/undercut-f1 for setup.
///
/// This avoids OpenF1's paid real-time REST API entirely — undercutf1 connects
/// directly to F1's own free live timing feed.
/// </summary>
public class LocalF1FlagWatcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly IGoveeLightController _lights;
    private readonly string[] _deviceIds;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IdleTransitionDelay = TimeSpan.FromMinutes(5);

    private string _lastFlag = "GREEN";
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _idleTransitionCts;

    public event Action<string>? Log;
    public event Action<string>? FlagChanged;

    public LocalF1FlagWatcher(IGoveeLightController lights, string[] deviceIds, HttpClient? httpClient = null)
    {
        _lights = lights;
        _deviceIds = deviceIds;
        _http = httpClient ?? new HttpClient { BaseAddress = new Uri("http://localhost:61937/") };
    }

    public void Start() => _ = RunAsync(_cts.Token);

    public void Stop() => _cts.Cancel();

    public void Dispose()
    {
        Stop();
        _idleTransitionCts?.Cancel();
        _idleTransitionCts?.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        RaiseLog($"[LocalF1FlagWatcher] Starting poll loop against {_http.BaseAddress} ...");

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Confirmed via Swagger: this is a GET endpoint on port 61937, not POST/61938.
                using var response = await _http.GetAsync("data/RaceControlMessages/latest", token);

                if (!response.IsSuccessStatusCode)
                {
                    RaiseLog($"[LocalF1FlagWatcher] API returned {(int)response.StatusCode} — is undercutf1 running with a live session started?");
                }
                else
                {
                    var json = await response.Content.ReadAsStringAsync(token);
                    await TryHandleResponseAsync(json);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                RaiseLog($"[LocalF1FlagWatcher] Could not reach undercutf1 locally: {ex.Message}. Is it running?");
            }
            catch (Exception ex)
            {
                RaiseLog($"[LocalF1FlagWatcher] Unexpected error: {ex.Message}");
            }

            await Task.Delay(PollInterval, token);
        }
    }

    private async Task TryHandleResponseAsync(string json)
    {
        RaiseLog($"[LocalF1FlagWatcher] Raw response: {json}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var flag = FindFlag(root);

            if (flag == null)
            {
                RaiseLog("[LocalF1FlagWatcher] Could not find a race-control flag in the response.");
                return;
            }

            await HandleFlagAsync(flag);
        }
        catch (JsonException ex)
        {
            RaiseLog($"[LocalF1FlagWatcher] Failed to parse response as JSON: {ex.Message}");
        }
    }

    private static string? FindFlag(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("messages", out var messages) &&
            messages.ValueKind == JsonValueKind.Object)
        {
            var latestFlag = FindLatestMessageFlag(messages);
            if (latestFlag != null) return latestFlag;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string? fallback = null;
                foreach (var property in element.EnumerateObject().Reverse())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (string.Equals(property.Name, "flag", StringComparison.OrdinalIgnoreCase))
                        {
                            var directFlag = NormalizeFlag(value);
                            if (directFlag != null) return directFlag;
                        }

                        if (IsFlagTextProperty(property.Name))
                        {
                            fallback ??= NormalizeFlag(value);
                        }
                    }

                    var nestedFlag = FindFlag(property.Value);
                    if (nestedFlag != null) return nestedFlag;
                }

                return fallback;

            case JsonValueKind.Array:
                for (var index = element.GetArrayLength() - 1; index >= 0; index--)
                {
                    var arrayFlag = FindFlag(element[index]);
                    if (arrayFlag != null) return arrayFlag;
                }

                break;
        }

        return null;
    }

    private static string? FindLatestMessageFlag(JsonElement messages)
    {
        string? latestTextFlag = null;

        foreach (var property in messages.EnumerateObject().Reverse())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;

            if (property.Value.TryGetProperty("flag", out var flagProperty) &&
                flagProperty.ValueKind == JsonValueKind.String)
            {
                var flag = NormalizeFlag(flagProperty.GetString());
                if (flag != null) return flag;
            }

            if (latestTextFlag == null)
            {
                var textFlag = FindFlag(property.Value);
                if (textFlag != null) latestTextFlag = textFlag;
            }
        }

        return latestTextFlag;
    }

    private static bool IsFlagTextProperty(string propertyName)
    {
        return propertyName.Contains("message", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("text", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("status", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("category", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("type", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Contains("DOUBLE YELLOW")) return "DOUBLE YELLOW";
        if (normalized.Contains("YELLOW")) return "YELLOW";
        if (normalized.Contains("RED")) return "RED";
        if (normalized.Contains("CHEQUERED") || normalized.Contains("CHECKERED")) return "CHEQUERED";
        if (normalized.Contains("CLEAR")) return "CLEAR";
        if (normalized.Contains("GREEN")) return "GREEN";

        return null;
    }

    private async Task HandleFlagAsync(string flag)
    {
        flag = flag.ToUpperInvariant();
        if (flag == _lastFlag) return;

        _idleTransitionCts?.Cancel();
        _lastFlag = flag;
        RaiseLog($"[LocalF1FlagWatcher] Flag changed: {flag}");
        FlagChanged?.Invoke(flag);

        (byte r, byte g, byte b)? color = flag switch
        {
            "YELLOW" or "DOUBLE YELLOW" => ((byte)255, (byte)255, (byte)0),
            "RED" => ((byte)255, (byte)0, (byte)0),
            "GREEN" => ((byte)0, (byte)255, (byte)0),
            "CLEAR" => ((byte)255, (byte)255, (byte)255),
            "CHEQUERED" => ((byte)255, (byte)255, (byte)255),
            _ => null
        };

        if (color == null) return;

        foreach (var id in _deviceIds)
        {
            await _lights.SetColorAsync(id, color.Value.r, color.Value.g, color.Value.b);
        }

        if (flag == "CLEAR")
        {
            _idleTransitionCts = new CancellationTokenSource();
            _ = SetIdleAfterClearAsync(_idleTransitionCts.Token);
        }
    }

    private async Task SetIdleAfterClearAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(IdleTransitionDelay, token);

            if (_lastFlag != "CLEAR") return;

            RaiseLog("[LocalF1FlagWatcher] No new race-control event for 5 minutes; switching lights to white idle.");
            foreach (var id in _deviceIds)
            {
                await _lights.SetColorAsync(id, 255, 255, 255);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RaiseLog(string message)
    {
        Console.WriteLine(message);
        Log?.Invoke(message);
    }
}
