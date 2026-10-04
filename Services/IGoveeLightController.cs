namespace F1_Redefined.Services;

public interface IGoveeLightController
{
    /// <summary>Set a solid RGB color on a specific device (strip or gaming bar).</summary>
    Task SetColorAsync(string deviceId, byte r, byte g, byte b);

    /// <summary>Turn a device off, or return it to whatever "idle" state you use between races.</summary>
    Task SetIdleAsync(string deviceId);
}
