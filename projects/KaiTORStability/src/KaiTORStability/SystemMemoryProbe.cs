using System;
using System.Runtime.InteropServices;

namespace KaiTORStability
{
    internal sealed class SystemMemorySnapshot
    {
        public double TotalPhysicalGb { get; set; }
        public double AvailablePhysicalGb { get; set; }
        public double TotalCommitGb { get; set; }
        public double AvailableCommitGb { get; set; }
        public uint MemoryLoadPercent { get; set; }

        public string Describe()
        {
            return "physicalGB=" + TotalPhysicalGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                   "; availablePhysicalGB=" + AvailablePhysicalGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                   "; commitLimitGB=" + TotalCommitGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                   "; availableCommitGB=" + AvailableCommitGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                   "; memoryLoad=" + MemoryLoadPercent + "%";
        }
    }

    internal static class SystemMemoryProbe
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
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

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        internal static SystemMemorySnapshot TryRead()
        {
            try
            {
                var status = new MemoryStatusEx();
                status.dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
                if (!GlobalMemoryStatusEx(ref status)) return null;

                const double gib = 1024d * 1024d * 1024d;
                return new SystemMemorySnapshot
                {
                    TotalPhysicalGb = status.ullTotalPhys / gib,
                    AvailablePhysicalGb = status.ullAvailPhys / gib,
                    TotalCommitGb = status.ullTotalPageFile / gib,
                    AvailableCommitGb = status.ullAvailPageFile / gib,
                    MemoryLoadPercent = status.dwMemoryLoad
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
