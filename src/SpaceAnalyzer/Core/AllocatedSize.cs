using System.Runtime.InteropServices;

namespace SpaceAnalyzer.Core;

/// <summary>
/// Corrects the logical length of files whose real footprint on disk is different:
/// sparse or compressed files on Windows, and sparse / cloud-evicted files on macOS
/// (for example Docker.raw reports 64 GB but may only use a few).
/// </summary>
internal static unsafe partial class AllocatedSize
{
    // Only large files are worth a second system call.
    const long MacThreshold = 16L * 1024 * 1024;
    const int StatSizeOffset = 96, StatBlocksOffset = 104, StatBufferSize = 256;

    static readonly bool s_windows = OperatingSystem.IsWindows();
    static readonly bool s_mac = OperatingSystem.IsMacOS();
    static readonly bool s_macArm = s_mac && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    static int s_macLayoutState; // 0 = unverified, 1 = verified, -1 = disabled

    public static long Adjust(string directory, string name, long length, FileAttributes attributes)
    {
        try
        {
            if (s_windows)
            {
                if ((attributes & (FileAttributes.SparseFile | FileAttributes.Compressed)) == 0)
                    return length;
                uint low = GetCompressedFileSizeW(Path.Join(directory, name), out uint high);
                if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0)
                    return length;
                long size = ((long)high << 32) | low;
                return size >= 0 && size < length ? size : length;
            }

            if (s_mac && length >= MacThreshold && s_macLayoutState >= 0)
            {
                byte* buf = stackalloc byte[StatBufferSize];
                string path = Path.Join(directory, name);
                int rc = s_macArm ? lstat_arm64(path, buf) : lstat_x64(path, buf);
                if (rc != 0) return length;
                long statSize = *(long*)(buf + StatSizeOffset);
                long blocks = *(long*)(buf + StatBlocksOffset);
                if (s_macLayoutState == 0)
                {
                    // Make sure we are reading the struct we think we are reading.
                    if (statSize != length) { s_macLayoutState = -1; return length; }
                    s_macLayoutState = 1;
                }
                long allocated = blocks * 512;
                return allocated >= 0 && allocated < length ? allocated : length;
            }
        }
        catch
        {
            // Best effort only.
        }
        return length;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetCompressedFileSizeW(string path, out uint high);

    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int lstat_arm64(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "lstat$INODE64", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int lstat_x64(string path, byte* buffer);
}
