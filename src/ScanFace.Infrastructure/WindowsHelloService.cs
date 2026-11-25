using System.Runtime.InteropServices;
using ScanFace.Application;
using Windows.Security.Credentials.UI;

namespace ScanFace.Infrastructure;

public sealed class WindowsHelloService(Func<nint>? getWindowHandle = null) : IWindowsHelloService
{
    public async Task<WindowsHelloStatus> CheckAvailabilityAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() switch
            {
                UserConsentVerifierAvailability.Available => WindowsHelloStatus.Available,
                UserConsentVerifierAvailability.DeviceBusy => WindowsHelloStatus.DeviceBusy,
                UserConsentVerifierAvailability.DeviceNotPresent => WindowsHelloStatus.DeviceNotPresent,
                UserConsentVerifierAvailability.DisabledByPolicy => WindowsHelloStatus.DisabledByPolicy,
                UserConsentVerifierAvailability.NotConfiguredForUser => WindowsHelloStatus.NotConfigured,
                _ => WindowsHelloStatus.Unavailable
            };
        }
        catch (Exception error) when (IsPlatformFailure(error))
        {
            return WindowsHelloStatus.Unavailable;
        }
    }

    public async Task<WindowsHelloStatus> RequestVerificationAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            UserConsentVerificationResult result;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var handle = getWindowHandle?.Invoke() ?? 0;
                if (handle == 0)
                {
                    return WindowsHelloStatus.Unavailable;
                }
                result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(handle, message).AsTask(cancellationToken);
            }
            else
            {
                result = await UserConsentVerifier.RequestVerificationAsync(message).AsTask(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            return result switch
            {
                UserConsentVerificationResult.Verified => WindowsHelloStatus.Verified,
                UserConsentVerificationResult.Canceled => WindowsHelloStatus.Canceled,
                UserConsentVerificationResult.RetriesExhausted => WindowsHelloStatus.RetriesExhausted,
                UserConsentVerificationResult.DeviceBusy => WindowsHelloStatus.DeviceBusy,
                UserConsentVerificationResult.DeviceNotPresent => WindowsHelloStatus.DeviceNotPresent,
                UserConsentVerificationResult.DisabledByPolicy => WindowsHelloStatus.DisabledByPolicy,
                UserConsentVerificationResult.NotConfiguredForUser => WindowsHelloStatus.NotConfigured,
                _ => WindowsHelloStatus.Unavailable
            };
        }
        catch (Exception error) when (IsPlatformFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WindowsHelloStatus.Unavailable;
        }
    }

    private static bool IsPlatformFailure(Exception error) =>
        error is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException;
}
