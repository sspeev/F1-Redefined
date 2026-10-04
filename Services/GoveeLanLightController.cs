using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace F1_Redefined.Services;

/// <summary>
/// Controls Govee devices over LAN using Govee's local UDP protocol.
/// - Discovery multicast: 239.255.255.250:4001 (send), listens on 4002 (receive responses)
/// - Control commands: sent via UDP to the device's IP on port 4003
///
/// deviceId here = the device's MAC address from the environment configuration.
/// Requires "LAN Control" enabled for the device in the Govee Home app.
/// </summary>
public class GoveeLanLightController : IGoveeLightController
{
    private const int DiscoveryPort = 4001;
    private const int ListenPort = 4002;
    private const int ControlPort = 4003;
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), DiscoveryPort);

    // Cache of MAC -> last known IP, so we don't re-discover on every command
    private readonly ConcurrentDictionary<string, IPAddress> _deviceIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPAddress> _fixedIps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fired for every log-worthy event — subscribe from your UI to display activity.</summary>
    public event Action<string>? Log;

    /// <summary>Fired whenever a device is discovered/cached, with its MAC and IP.</summary>
    public event Action<string, IPAddress>? DeviceDiscovered;

    /// <summary>Sets the fixed IP for one device, bypassing discovery for that device.</summary>
    public void SetFixedIp(string deviceId, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            _fixedIps.TryRemove(deviceId, out _);
            RaiseLog($"[GoveeLan] Fixed IP cleared for {deviceId}; device discovery will be used.");
            return;
        }

        if (!IPAddress.TryParse(ipAddress.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Enter a valid IPv4 address.", nameof(ipAddress));
        }

        _fixedIps[deviceId] = ip;
        RaiseLog($"[GoveeLan] Fixed IP for {deviceId} set to {ip}.");
    }

    private void RaiseLog(string message)
    {
        Console.WriteLine(message); // harmless if no console is attached, useful if one is
        Log?.Invoke(message);
    }

    /// <summary>
    /// Broadcasts a discovery request and listens briefly for responses.
    /// Populates the MAC -> IP cache. Call this at startup and whenever a send fails.
    /// </summary>
    public async Task DiscoverDevicesAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(3);

        using var sender = new UdpClient();
        using var listener = new UdpClient(ListenPort) { EnableBroadcast = true };

        var scanMessage = JsonSerializer.Serialize(new
        {
            msg = new { cmd = "scan", data = new { account_topic = "reserve" } }
        });
        var bytes = Encoding.UTF8.GetBytes(scanMessage);

        RaiseLog($"[GoveeLan] Sending discovery scan to {MulticastEndpoint} (listening on port {ListenPort})...");
        await sender.SendAsync(bytes, bytes.Length, MulticastEndpoint);

        var cts = new CancellationTokenSource(timeout.Value);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var receiveTask = listener.ReceiveAsync();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(timeout.Value, cts.Token));
                if (completed != receiveTask) break;

                var result = await receiveTask;
                var json = Encoding.UTF8.GetString(result.Buffer);
                TryCacheDeviceFromResponse(json);
            }
        }
        catch (OperationCanceledException) { /* timeout elapsed, expected */ }

        RaiseLog($"[GoveeLan] Scan window closed. {_deviceIps.Count} device(s) known so far.");
    }

    private void TryCacheDeviceFromResponse(string json)
    {
        RaiseLog($"[GoveeLan] Raw discovery response: {json}");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("msg").GetProperty("data");

            var ip = data.GetProperty("ip").GetString();
            // Response may not always include MAC in every firmware version —
            // fall back to matching by IP if you only have one device.
            string? mac = data.TryGetProperty("device", out var deviceProp) ? deviceProp.GetString() : null;

            if (ip != null && mac != null)
            {
                _deviceIps[mac] = IPAddress.Parse(ip);
                RaiseLog($"[GoveeLan] Discovered {mac} at {ip}");
                DeviceDiscovered?.Invoke(mac, IPAddress.Parse(ip));
            }
            else
            {
                RaiseLog($"[GoveeLan] Response parsed but missing ip/device fields — cannot cache.");
            }
        }
        catch (Exception ex)
        {
            RaiseLog($"[GoveeLan] Failed to parse response: {ex.Message}");
        }
    }

    public async Task SetColorAsync(string deviceId, byte r, byte g, byte b)
    {
        var ip = await ResolveIpAsync(deviceId);
        if (ip == null)
        {
            RaiseLog($"[GoveeLan] Could not resolve IP for {deviceId} — skipping color set.");
            return;
        }

        var command = JsonSerializer.Serialize(new
        {
            msg = new
            {
                cmd = "colorwc",
                data = new
                {
                    color = new { r, g, b },
                    colorTemInKelvin = 0
                }
            }
        });

        await SendCommandAsync(ip, command);
    }

    public async Task SetIdleAsync(string deviceId)
    {
        var ip = await ResolveIpAsync(deviceId);
        if (ip == null) return;

        // "Idle" here = turn off. Swap for a dim white / low-brightness command if you'd rather
        // it stay on between races.
        var command = JsonSerializer.Serialize(new
        {
            msg = new { cmd = "turn", data = new { value = 0 } }
        });

        await SendCommandAsync(ip, command);
    }

    private async Task<IPAddress?> ResolveIpAsync(string deviceId)
    {
        if (_fixedIps.TryGetValue(deviceId, out var fixedIp)) return fixedIp;

        if (_deviceIps.TryGetValue(deviceId, out var ip)) return ip;

        // Not cached yet — run discovery once and try again
        await DiscoverDevicesAsync();
        return _deviceIps.TryGetValue(deviceId, out ip) ? ip : null;
    }

    private async Task SendCommandAsync(IPAddress ip, string jsonCommand)
    {
        try
        {
            using var client = new UdpClient();
            var bytes = Encoding.UTF8.GetBytes(jsonCommand);
            var endpoint = new IPEndPoint(ip, ControlPort);
            RaiseLog($"[GoveeLan] Sending {bytes.Length} bytes to {endpoint}: {jsonCommand}");
            await client.SendAsync(bytes, bytes.Length, endpoint);
            RaiseLog($"[GoveeLan] UDP send completed for {endpoint}. The device should now show the color.");
        }
        catch (Exception ex)
        {
            RaiseLog($"[GoveeLan] Failed to send command to {ip}: {ex.Message}");
        }
    }
}

// ============================================================
// WIRE-UP (replace the GoveeApiLightController example from before)
// ============================================================
//
// var lightController = new GoveeLanLightController();
// await lightController.DiscoverDevicesAsync(); // populate IP cache at startup
//
// var deviceIds = new[] { "device-mac-from-env" }; // MAC addresses, one per light/strip/bar
// var f1LightService = new F1FlagLightService(lightController, deviceIds);
// f1LightService.Start();
