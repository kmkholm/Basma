using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Basma
{
    /// <summary>Raw, read-only access to physical disks and volumes (\\.\PhysicalDriveN, \\.\C:).</summary>
    public static class Native
    {
        public const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
        public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        public static SafeFileHandle Open(string path, bool read)
        {
            var h = CreateFile(path, read ? GENERIC_READ : 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                h.Dispose();
                if (err == 5) throw new UnauthorizedAccessException(L.T("Administrator rights are required to read a disk directly."));
                throw new Win32Exception(err);
            }
            return h;
        }

        public static byte[] Ioctl(SafeFileHandle h, uint code, byte[] input, int outSize)
        {
            var o = new byte[outSize];
            if (!DeviceIoControl(h, code, input, input?.Length ?? 0, o, o.Length, out int got, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Array.Resize(ref o, got);
            return o;
        }

        public static bool IsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public sealed class DiskInfo
    {
        public int Number;
        public string Path => $@"\\.\PhysicalDrive{Number}";
        public string Vendor, Product, Serial, Revision, Bus;
        public bool Removable;
        public long Size;
        public int SectorSize = 512;
        public List<string> Volumes = new();
        public string Model => string.Join(" ", new[] { Vendor, Product }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        public string Title => $"PhysicalDrive{Number}  ·  {(string.IsNullOrEmpty(Model) ? "?" : Model)}  ·  {Fmt.Bytes(Size)}";
    }

    public static class Disks
    {
        static readonly string[] BusNames = { "Unknown", "SCSI", "ATAPI", "ATA", "IEEE1394", "SSA", "Fibre", "USB", "RAID", "iSCSI", "SAS", "SATA", "SD", "MMC", "Virtual", "FileBackedVirtual", "Spaces", "NVMe", "SCM", "UFS" };

        /// <summary>Enumerates disks. Works without admin (no read access is requested).</summary>
        public static List<DiskInfo> List()
        {
            var list = new List<DiskInfo>();
            for (int i = 0; i < 32; i++)
            {
                SafeFileHandle h;
                try { h = Native.Open($@"\\.\PhysicalDrive{i}", false); }
                catch { continue; }
                using (h)
                {
                    var d = new DiskInfo { Number = i };
                    try
                    {
                        var q = new byte[12]; // StorageDeviceProperty, PropertyStandardQuery
                        var o = Native.Ioctl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, q, 1024);
                        string S(int off)
                        {
                            int p = BitConverter.ToInt32(o, off);
                            if (p <= 0 || p >= o.Length) return "";
                            int e = Array.IndexOf(o, (byte)0, p);
                            return Encoding.ASCII.GetString(o, p, (e < 0 ? o.Length : e) - p).Trim();
                        }
                        d.Removable = o[10] != 0;
                        d.Vendor = S(12); d.Product = S(16); d.Revision = S(20); d.Serial = S(24);
                        int bus = BitConverter.ToInt32(o, 28);
                        d.Bus = bus >= 0 && bus < BusNames.Length ? BusNames[bus] : bus.ToString();
                    }
                    catch { /* some devices refuse the query */ }
                    try
                    {
                        var g = Native.Ioctl(h, Native.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 256);
                        d.SectorSize = BitConverter.ToInt32(g, 20);
                        d.Size = BitConverter.ToInt64(g, 24);
                    }
                    catch { }
                    if (d.SectorSize <= 0) d.SectorSize = 512;
                    list.Add(d);
                }
            }
            foreach (var drv in DriveInfo.GetDrives())
            {
                try
                {
                    string letter = drv.Name.Substring(0, 2);
                    using var h = Native.Open($@"\\.\{letter}", false);
                    var o = Native.Ioctl(h, Native.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 512);
                    int n = BitConverter.ToInt32(o, 0);
                    for (int k = 0; k < n; k++)
                    {
                        int disk = BitConverter.ToInt32(o, 8 + k * 24);
                        var di = list.FirstOrDefault(x => x.Number == disk);
                        string lab = letter;
                        try { if (drv.IsReady) lab += $" ({drv.VolumeLabel} · {drv.DriveFormat} · {Fmt.Bytes(drv.TotalSize)})"; } catch { }
                        di?.Volumes.Add(lab);
                    }
                }
                catch { }
            }
            return list;
        }

        /// <summary>Length of a device in bytes (needs read access → admin).</summary>
        public static (long size, int sector) Geometry(SafeFileHandle h)
        {
            long size = 0; int sector = 512;
            try { var g = Native.Ioctl(h, Native.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 256); sector = BitConverter.ToInt32(g, 20); size = BitConverter.ToInt64(g, 24); } catch { }
            try { var l = Native.Ioctl(h, Native.IOCTL_DISK_GET_LENGTH_INFO, null, 8); size = BitConverter.ToInt64(l, 0); } catch { }
            if (sector <= 0) sector = 512;
            return (size, sector);
        }

        public static bool IsDevicePath(string p) => p != null && p.StartsWith(@"\\.\", StringComparison.Ordinal);

        /// <summary>Reads <paramref name="count"/> bytes at a sector-aligned offset from a file or device.</summary>
        public static byte[] ReadAt(string path, long offset, int count)
        {
            if (!IsDevicePath(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Position = offset;
                var b = new byte[count];
                int got = 0, n;
                while (got < count && (n = fs.Read(b, got, count - got)) > 0) got += n;
                Array.Resize(ref b, got);
                return b;
            }
            using var h = Native.Open(path, true);
            var (_, sector) = Geometry(h);
            long start = offset / sector * sector;
            int len = (int)(((offset + count - start) + sector - 1) / sector * sector);
            var buf = new byte[len];
            int r = RandomAccess.Read(h, buf, start);
            int skip = (int)(offset - start);
            var res = new byte[Math.Max(0, Math.Min(count, r - skip))];
            Array.Copy(buf, skip, res, 0, res.Length);
            return res;
        }

        public static long LengthOf(string path)
        {
            if (!IsDevicePath(path)) return new FileInfo(path).Length;
            using var h = Native.Open(path, true);
            return Geometry(h).size;
        }
    }
}
