using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StatMaster.Agent;

public sealed class MetricRegistry
{
    private readonly Dictionary<string, Func<string>> _collectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Real built-in metrics
            ["system.hostname"] = () => Environment.MachineName,
            ["cpu.count"] = () => Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["os.description"] = () => RuntimeInformation.OSDescription,

            ["ram.total.mb"] = GetTotalRamMb,
            ["ram.used.mb"] = GetUsedRamMb,

            ["disk.total.gb"] = GetDiskTotalGb,
            ["disk.free.gb"] = GetDiskFreeGb,
        };

    public string Collect(string key)
    {
        if (!_collectors.TryGetValue(key, out var collector))
            return "ERR|unknown-key";

        try
        {
            return $"OK|{collector()}";
        }
        catch (Exception ex)
        {
            return $"ERR|collector-failed:{ex.Message}";
        }
    }

    private static string GetTotalRamMb()
    {
        var memory = GetMemoryStatus();
        double totalMb = memory.TotalPhysicalBytes / (1024d * 1024d);
        return totalMb.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string GetUsedRamMb()
    {
        var memory = GetMemoryStatus();
        double usedMb = (memory.TotalPhysicalBytes - memory.AvailablePhysicalBytes) / (1024d * 1024d);
        return usedMb.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string GetDiskTotalGb()
    {
        var drive = GetSystemDrive();
        double totalGb = drive.TotalSize / (1024d * 1024d * 1024d);
        return totalGb.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string GetDiskFreeGb()
    {
        var drive = GetSystemDrive();
        double freeGb = drive.TotalFreeSpace / (1024d * 1024d * 1024d);
        return freeGb.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static DriveInfo GetSystemDrive()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        return new DriveInfo(root);
    }

    private static MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Unsafe.SizeOf<MemoryStatusEx>();

        if (!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException("Failed to read memory status from Windows API.");

        return new MemoryStatus(status.TotalPhysical, status.AvailablePhysical);
    }

    private readonly record struct MemoryStatus(ulong TotalPhysicalBytes, ulong AvailablePhysicalBytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}