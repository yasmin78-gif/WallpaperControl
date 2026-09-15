using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal sealed class SystemMonitorSnapshot
    {
        public float? CpuLoad { get; init; }
        public float? CpuTemperature { get; init; }
        public float? MemoryLoad { get; init; }
        public float? MemoryUsedGb { get; init; }
        public float? MemoryTotalGb { get; init; }
        public float? GpuLoad { get; init; }
        public float? GpuTemperature { get; init; }
        public float? VramUsedGb { get; init; }
        public float? VramTotalGb { get; init; }
        public double DownloadBytesPerSecond { get; init; }
        public double UploadBytesPerSecond { get; init; }
        public IReadOnlyList<DriveSnapshot> Drives { get; init; } = Array.Empty<DriveSnapshot>();
    }

    internal sealed record DriveSnapshot(string Name, long FreeBytes, long TotalBytes);

    internal sealed class SystemMonitorService : IDisposable
    {
        private readonly Computer computer;
        private readonly Stopwatch networkWatch = Stopwatch.StartNew();
        private long previousReceived;
        private long previousSent;
        private bool networkInitialized;
        private bool disposed;
        private readonly object syncRoot = new();

        public SystemMonitorService()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true
            };
            computer.Open();
        }

        public SystemMonitorSnapshot Sample()
        {
            lock (syncRoot)
            {
                if (disposed)
                    return new SystemMonitorSnapshot();

                return SampleCore();
            }
        }

        private SystemMonitorSnapshot SampleCore()
        {
            float? cpuLoad = null;
            float? cpuTemp = null;
            float? cpuTempFallback = null;
            float? memoryLoad = null;
            float? memoryUsed = null;
            float? memoryAvailable = null;
            float? gpuLoad = null;
            float? gpuTemp = null;
            float? vramUsed = null;
            float? vramTotal = null;

            foreach (IHardware hardware in EnumerateHardware())
            {
                try { hardware.Update(); }
                catch { continue; }

                foreach (ISensor sensor in hardware.Sensors)
                {
                    float? value = sensor.Value;
                    if (!value.HasValue) continue;

                    string name = sensor.Name ?? string.Empty;
                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            if (sensor.SensorType == SensorType.Load &&
                                (name.Contains("Total", StringComparison.OrdinalIgnoreCase) || cpuLoad == null))
                                cpuLoad = PreferNamed(cpuLoad, value, name, "Total");
                            else if (sensor.SensorType == SensorType.Temperature)
                                cpuTemp = PickCpuTemperature(cpuTemp, value, name);
                            break;

                        case HardwareType.Motherboard:
                        case HardwareType.SuperIO:
                            if (sensor.SensorType == SensorType.Temperature &&
                                (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)))
                                cpuTempFallback ??= value;
                            break;

                        case HardwareType.Memory:
                            if (sensor.SensorType == SensorType.Load)
                                memoryLoad ??= value;
                            else if (sensor.SensorType == SensorType.Data && name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                                memoryUsed ??= value;
                            else if (sensor.SensorType == SensorType.Data && name.Contains("Available", StringComparison.OrdinalIgnoreCase))
                                memoryAvailable ??= value;
                            break;

                        case HardwareType.GpuAmd:
                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuIntel:
                            if (sensor.SensorType == SensorType.Load &&
                                (name.Contains("Core", StringComparison.OrdinalIgnoreCase) || name.Contains("D3D", StringComparison.OrdinalIgnoreCase) || gpuLoad == null))
                                gpuLoad = PreferNamed(gpuLoad, value, name, "Core");
                            else if (sensor.SensorType == SensorType.Temperature)
                                gpuTemp = PickTemperature(gpuTemp, value, name, "Core", "GPU");
                            else if (sensor.SensorType == SensorType.SmallData || sensor.SensorType == SensorType.Data)
                            {
                                if (name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase))
                                    vramUsed ??= NormalizeMemoryGb(value.Value, sensor.SensorType);
                                else if (name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
                                    vramTotal ??= NormalizeMemoryGb(value.Value, sensor.SensorType);
                            }
                            break;
                    }
                }
            }

            // Use the Windows physical-memory counters for the user-facing RAM values.
            // These correspond much more closely to Task Manager than LHM's memory data sensors
            // on systems where firmware/reserved-memory reporting differs.
            if (TryReadWindowsMemory(out float windowsLoad, out float windowsUsedGb, out float windowsTotalGb))
            {
                memoryLoad = windowsLoad;
                memoryUsed = windowsUsedGb;
                memoryAvailable = windowsTotalGb - windowsUsedGb;
            }

            float? memoryTotal = memoryUsed.HasValue && memoryAvailable.HasValue
                ? memoryUsed.Value + memoryAvailable.Value
                : null;

            cpuTemp ??= cpuTempFallback;

            (double down, double up) = ReadNetworkRates();
            List<DriveSnapshot> drives = ReadDrives();

            return new SystemMonitorSnapshot
            {
                CpuLoad = ClampPercent(cpuLoad),
                CpuTemperature = cpuTemp,
                MemoryLoad = ClampPercent(memoryLoad),
                MemoryUsedGb = memoryUsed,
                MemoryTotalGb = memoryTotal,
                GpuLoad = ClampPercent(gpuLoad),
                GpuTemperature = gpuTemp,
                VramUsedGb = vramUsed,
                VramTotalGb = vramTotal,
                DownloadBytesPerSecond = down,
                UploadBytesPerSecond = up,
                Drives = drives
            };
        }

        private IEnumerable<IHardware> EnumerateHardware()
        {
            foreach (IHardware hardware in computer.Hardware)
            {
                yield return hardware;
                foreach (IHardware child in EnumerateChildren(hardware))
                    yield return child;
            }
        }

        private static IEnumerable<IHardware> EnumerateChildren(IHardware hardware)
        {
            foreach (IHardware child in hardware.SubHardware)
            {
                yield return child;
                foreach (IHardware nested in EnumerateChildren(child))
                    yield return nested;
            }
        }

        private (double Down, double Up) ReadNetworkRates()
        {
            long received = 0;
            long sent = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                try
                {
                    IPv4InterfaceStatistics stats = nic.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch { }
            }

            double elapsed = networkWatch.Elapsed.TotalSeconds;
            networkWatch.Restart();
            if (!networkInitialized || elapsed <= 0)
            {
                networkInitialized = true;
                previousReceived = received;
                previousSent = sent;
                return (0, 0);
            }

            double down = Math.Max(0, received - previousReceived) / elapsed;
            double up = Math.Max(0, sent - previousSent) / elapsed;
            previousReceived = received;
            previousSent = sent;
            return (down, up);
        }

        private static List<DriveSnapshot> ReadDrives()
        {
            List<DriveSnapshot> result = new();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        result.Add(new DriveSnapshot(drive.Name.TrimEnd('\\'), drive.AvailableFreeSpace, drive.TotalSize));
                }
                catch { }
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static bool TryReadWindowsMemory(out float load, out float usedGb, out float totalGb)
        {
            MEMORYSTATUSEX status = new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            {
                load = usedGb = totalGb = 0;
                return false;
            }

            const double gib = 1024d * 1024d * 1024d;
            totalGb = (float)(status.ullTotalPhys / gib);
            usedGb = (float)((status.ullTotalPhys - status.ullAvailPhys) / gib);
            load = (float)(usedGb / totalGb * 100d);
            return true;
        }

        private static float? ClampPercent(float? value) =>
            value.HasValue ? Math.Clamp(value.Value, 0f, 100f) : null;

        private static float? PreferNamed(float? current, float? candidate, string name, string preferred)
        {
            if (!candidate.HasValue) return current;
            if (current == null || name.Contains(preferred, StringComparison.OrdinalIgnoreCase)) return candidate;
            return current;
        }

        private static float? PickCpuTemperature(float? current, float? candidate, string name)
        {
            if (!candidate.HasValue) return current;

            // LibreHardwareMonitor uses different names depending on CPU vendor/generation.
            // Prefer one representative package/average sensor over individual cores.
            string[] priority = { "Package", "Core Average", "Core Max", "Tctl/Tdie", "Tctl", "Tdie", "CPU" };
            int candidatePriority = Array.FindIndex(priority, p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (candidatePriority < 0)
                return current ?? candidate;

            // Sample() starts with null and hardware sensors are normally enumerated in a stable order.
            // Package is the preferred value; once found, keep it.
            if (name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                return candidate;

            return current ?? candidate;
        }

        private static float? PickTemperature(float? current, float? candidate, string name, params string[] preferred)
        {
            if (!candidate.HasValue) return current;
            if (preferred.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) return candidate;
            return current ?? candidate;
        }

        private static float NormalizeMemoryGb(float value, SensorType type) =>
            type == SensorType.SmallData ? value / 1024f : value;

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed) return;
                disposed = true;
                try { computer.Close(); } catch { }
            }
        }
    }
}
