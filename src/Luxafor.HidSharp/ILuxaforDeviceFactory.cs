namespace Luxafor.HidSharp;

/// <summary>
/// Factory for creating Luxafor device instances.
/// </summary>
public interface ILuxaforDeviceFactory
{
    /// <inheritdoc cref="LuxaforDevice.TryOpen"/>
    ILuxaforDevice? TryOpen();
}
