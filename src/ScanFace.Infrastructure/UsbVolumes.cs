using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ScanFace.Infrastructure;

public sealed record UsbVolume(string Root, string DeviceId, string Label)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? $"Unidade USB {Root}" : $"{Label} ({Root})";
}

public static class UsbVolumes
{
    public static IReadOnlyList<UsbVolume> Find() =>
        DriveInfo.GetDrives().Select(drive => Find(drive.Name)).OfType<UsbVolume>().ToArray();

    public static UsbVolume? Find(string root)
    {
        if (root.Length != 3 || root[1] != ':' || root[2] != Path.DirectorySeparatorChar || !char.IsAsciiLetter(root[0]))
        {
            return null;
        }
        var drive = new DriveInfo(root);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
        {
            return null;
        }
        using var handle = CreateFile($@"\\.\{root[..2]}", 0, 7, 0, 3, 0, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        var response = new byte[4096];
        if (!DeviceIoControl(handle, 0x002D1400, new byte[12], 12, response, response.Length, out var returned, 0) ||
            returned < 36 || BitConverter.ToUInt32(response, 28) != 7)
        {
            return null;
        }

        string ReadField(int offset)
        {
            var start = BitConverter.ToInt32(response, offset);
            if (start < 36 || start >= returned)
            {
                return string.Empty;
            }
            var end = Array.IndexOf(response, (byte)0, start, returned - start);
            return end < 0 ? string.Empty : Encoding.ASCII.GetString(response, start, end - start).Trim().ToUpperInvariant();
        }

        var serial = ReadField(24);
        if (serial.Length < 4 || serial.All(character => character == serial[0]) ||
            serial is "0123456789" or "1234567890" or "123456789" or "UNKNOWN" or "NONE")
        {
            return null;
        }
        try
        {
            if (!drive.IsReady)
            {
                return null;
            }
            var deviceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ReadField(12)}\0{ReadField(16)}\0{serial}")));
            return new(drive.RootDirectory.FullName, deviceId, drive.VolumeLabel);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        [Out] byte[] output, int outputSize, out int returned, nint overlapped);
}
