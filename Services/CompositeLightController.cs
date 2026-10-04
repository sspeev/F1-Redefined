namespace F1_Redefined.Services;

public sealed class CompositeLightController(
    IReadOnlyDictionary<string,
    IGoveeLightController> controllers) : IGoveeLightController
{
    private readonly IReadOnlyDictionary<string, IGoveeLightController> _controllers = controllers;

    public Task SetColorAsync(string deviceId, byte r, byte g, byte b) =>
        Resolve(deviceId).SetColorAsync(deviceId, r, g, b);

    public Task SetIdleAsync(string deviceId) => Resolve(deviceId).SetIdleAsync(deviceId);

    private IGoveeLightController Resolve(string deviceId)
    {
        if (_controllers.TryGetValue(deviceId, out var controller)) return controller;
        throw new InvalidOperationException($"No light controller configured for {deviceId}.");
    }
}
