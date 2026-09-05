using ScanFace.Application;
using Windows.Security.Credentials.UI;

namespace ScanFace.Infrastructure;

public sealed class WindowsHelloService : IWindowsHelloService
{
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RequestVerificationAsync(string message)
    {
        try
        {
            return await UserConsentVerifier.RequestVerificationAsync(message) == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
        }
    }
}
