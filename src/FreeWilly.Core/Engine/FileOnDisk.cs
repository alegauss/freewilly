using System.Runtime.InteropServices;

namespace FreeWilly.Core.Engine;

/// <summary>
/// How much of a volume a file is actually using, which for a sparse one is not its length (DD221).
/// </summary>
/// <remarks>
/// <para><b>A virtual disk handed back to Windows is a sparse file, and a sparse file keeps its
/// length.</b> NTFS records the ranges nothing was ever written to and stops charging for them, so
/// <see cref="FileInfo.Length"/> goes on reporting the size the file grew to while the volume gets
/// its space back. That is the whole point of <c>wsl --manage --set-sparse</c>, and it is also the
/// reason a compaction that measured length alone would report having reclaimed nothing.</para>
///
/// <para>Two numbers, then, and they answer different questions. The length is what the filesystem
/// inside the distribution may grow into, and this is what the user gets back on their drive.</para>
/// </remarks>
public static class FileOnDisk
{
    /// <summary>How many bytes of the volume a file occupies.</summary>
    /// <param name="path">The file.</param>
    /// <returns>
    /// The allocated size, or <see langword="null"/> where the file is not there or Windows would
    /// not say.
    /// </returns>
    /// <remarks>
    /// <c>GetCompressedFileSize</c> despite the name: it reports physical storage for compressed
    /// <em>and</em> sparse files, and returns the plain length for an ordinary one. There is no
    /// managed equivalent — <see cref="FileInfo"/> exposes the logical length only.
    /// </remarks>
    public static long? Bytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        var low = GetCompressedFileSizeW(path, out var high);

        // The documented failure signal, and it is only a failure where the last error says so: a
        // file whose low word really is 0xFFFFFFFF returns the same value having succeeded.
        if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0)
        {
            return null;
        }

        return ((long)high << 32) | low;
    }

    /// <remarks>
    /// <c>DllImport</c> rather than the source-generated <c>LibraryImport</c>, which wants
    /// <c>AllowUnsafeBlocks</c> across the whole assembly. One P/Invoke of two integers is not worth
    /// turning that on for every file in Core.
    /// </remarks>
    [DllImport(
        "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);
}
