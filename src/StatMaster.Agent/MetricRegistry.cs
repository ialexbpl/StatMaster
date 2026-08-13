using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StatMaster.Agent;

public sealed class MetricRegistry
{
    private readonly object _cpuUsageLock = new();
    private CpuTimes? _lastCpuSample;

    private readonly Dictionary<string, Func<string>> _collectors;

    public MetricRegistry()
    {
        _collectors = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // klucze do ktorych moge zapytac w serwerze
            ["system.hostname"] = () => Environment.MachineName,
            ["cpu.count"] = () => Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["cpu.usage.percent"] = GetCpuUsagePercent,
            ["os.description"] = () => RuntimeInformation.OSDescription,

            ["ram.total.mb"] = GetTotalRamMb,
            ["ram.used.mb"] = GetUsedRamMb,

            ["disk.total.gb"] = GetDiskTotalGb,
            ["disk.free.gb"] = GetDiskFreeGb,
        };

    //probka poprzedniego odczytu, żeby liczyć delta czasu CPU.
        _lastCpuSample = ReadCpuTimes();
    }

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

//metoda do obliczania procentu użycia CPU
    private string GetCpuUsagePercent()
    {
        lock (_cpuUsageLock)// tutaj blokujemy dostęp do odczytu CPU times aby uniknąć konkurencyjnych odczytów
        {
            CpuTimes current = ReadCpuTimes();// odczytujemy aktualne czasy CPU

            if (_lastCpuSample is null)
            {
                _lastCpuSample = current;
                return "0.0";
            }

            CpuTimes previous = _lastCpuSample.Value;// odczytujemy poprzednie czasy CPU
            _lastCpuSample = current;

            ulong idleDelta = current.Idle - previous.Idle;
            ulong totalDelta = current.Total - previous.Total;
            if (totalDelta == 0)
                return "0.0";

            //obliczamy procent użycia CPU
            double usagePercent = (1d - (double)idleDelta / totalDelta) * 100d;
            usagePercent = Math.Clamp(usagePercent, 0d, 100d);// ograniczamy wartość do 0-100%
            return usagePercent.ToString("F1", CultureInfo.InvariantCulture);
        }
    }


//metoda do odczytu czasu CPU z Windows API
    private static CpuTimes ReadCpuTimes()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            throw new InvalidOperationException("Failed to read CPU times from Windows API.");

        ulong idle = FileTimeToUInt64(idleTime);
        ulong kernel = FileTimeToUInt64(kernelTime);
        ulong user = FileTimeToUInt64(userTime);
        ulong total = kernel + user;
        return new CpuTimes(idle, total);
    }

//metoda do konwersji FileTime na ulong bo to jest typ w Windows API
    private static ulong FileTimeToUInt64(FileTime fileTime)
        => ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    private readonly record struct CpuTimes(ulong Idle, ulong Total);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime lpIdleTime, out FileTime lpKernelTime, out FileTime lpUserTime);
}