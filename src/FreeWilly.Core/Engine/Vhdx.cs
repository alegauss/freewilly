using System.Buffers.Binary;
using System.Text;

namespace FreeWilly.Core.Engine;

/// <summary>
/// Which ext4 filesystem a WSL virtual disk carries, read out of the file on the Windows side (DD280).
/// </summary>
/// <remarks>
/// <para><b>The answer for a distribution that cannot give it.</b> The check used to learn its disk's
/// UUID by asking the distribution, and on 16 September 2026 the distribution that needed a check
/// could not be asked: ext4 found a bad block bitmap checksum on mount, aborted the journal, remounted
/// read-only, and every launch exited <c>Wsl/Service/CreateInstance/E_UNEXPECTED</c>. The file was
/// still there, and the superblock in it still said which filesystem it was.</para>
///
/// <para><b>Only as much of the format as that takes.</b> A VHDX locates its payload blocks through a
/// block allocation table, and the table is found through the region table at 192 KiB. Payload block
/// 0 is entry 0, and a block is never smaller than 1 MiB, so the ext4 superblock at offset 1024 is
/// always inside it. WSL's disk carries no partition table, so that offset is the filesystem's own.
/// </para>
///
/// <para><b>A wrong answer here cannot pick the wrong disk</b>, which is why nothing is verified
/// beyond the signatures. The log is not replayed and the checksums are not computed, so a read taken
/// mid-write could be stale. That costs nothing: the UUID read here is matched against the
/// <c>blkid</c> listing in the rescue, and a UUID nothing carries is reported as the disk not being
/// found. Measured readable while the utility VM held the file open.</para>
/// </remarks>
internal static class Vhdx
{
    /// <summary>What the first eight bytes of every VHDX say.</summary>
    private const string Identifier = "vhdxfile";

    /// <summary>Where the region table is, and where its copy is.</summary>
    private static readonly long[] RegionTables = [0x30000, 0x40000];

    /// <summary>The region table's own signature.</summary>
    private const string RegionSignature = "regi";

    /// <summary>The most entries the format allows in a region table.</summary>
    private const uint MostRegions = 2047;

    /// <summary>The region holding the block allocation table.</summary>
    private static readonly Guid AllocationTable = new("2DC27766-F623-4200-9D64-115E9BFD4A08");

    /// <summary>The low three bits of a table entry, which say where the block's data is.</summary>
    private const ulong StateMask = 7;

    /// <summary>A block whose data is all in this file.</summary>
    private const ulong FullyPresent = 6;

    /// <summary>Where the ext4 superblock starts, from the start of the filesystem.</summary>
    private const long Superblock = 1024;

    /// <summary>Where ext4's magic number sits in the superblock.</summary>
    private const int MagicAt = 0x38;

    /// <summary>What ext4 writes there.</summary>
    private const ushort Ext4Magic = 0xEF53;

    /// <summary>Where the filesystem's UUID sits in the superblock.</summary>
    private const int UuidAt = 0x68;

    /// <summary>Read the UUID of the ext4 filesystem on a virtual disk.</summary>
    /// <param name="path">The <c>ext4.vhdx</c>.</param>
    /// <returns>
    /// The UUID, spelled the way <c>blkid</c> prints it, or <see langword="null"/> where the file is
    /// absent, unreadable, not a VHDX, or does not start with an ext4 filesystem.
    /// </returns>
    /// <remarks>
    /// Opened sharing everything. The utility VM may hold the file for writing, and this must neither
    /// be refused by that nor get in its way.
    /// </remarks>
    internal static string? FilesystemUuid(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Read(Stream file)
    {
        if (!Encoding.ASCII.GetString(At(file, 0, Identifier.Length)).Equals(
            Identifier, StringComparison.Ordinal))
        {
            return null;
        }

        if (RegionTables.Select(offset => TableOffset(file, offset)).FirstOrDefault(
            found => found is not null) is not { } table)
        {
            return null;
        }

        var entry = BinaryPrimitives.ReadUInt64LittleEndian(At(file, table, sizeof(ulong)));
        if ((entry & StateMask) != FullyPresent)
        {
            return null;
        }

        // The upper 44 bits are the block's offset in the file, in megabytes.
        var megabytes = entry >> 20;
        if (megabytes > (ulong)(file.Length >> 20))
        {
            return null;
        }

        var superblock = At(file, ((long)megabytes << 20) + Superblock, UuidAt + 16);
        if (BinaryPrimitives.ReadUInt16LittleEndian(superblock.AsSpan(MagicAt)) != Ext4Magic)
        {
            return null;
        }

        var uuid = Convert.ToHexStringLower(superblock, UuidAt, 16);
        return $"{uuid[..8]}-{uuid[8..12]}-{uuid[12..16]}-{uuid[16..20]}-{uuid[20..]}";
    }

    /// <summary>Where the block allocation table starts, read out of one copy of the region table.</summary>
    /// <param name="file">The disk.</param>
    /// <param name="offset">Where that copy is.</param>
    /// <returns>The file offset, or <see langword="null"/> where this copy does not name one.</returns>
    private static long? TableOffset(Stream file, long offset)
    {
        var header = At(file, offset, 16);
        if (!Encoding.ASCII.GetString(header, 0, RegionSignature.Length).Equals(
            RegionSignature, StringComparison.Ordinal))
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        if (count > MostRegions)
        {
            return null;
        }

        var entries = At(file, offset + 16, checked((int)count * 32));
        for (var at = 0; at < entries.Length; at += 32)
        {
            if (new Guid(entries.AsSpan(at, 16)) == AllocationTable)
            {
                return (long)BinaryPrimitives.ReadUInt64LittleEndian(entries.AsSpan(at + 16));
            }
        }

        return null;
    }

    /// <summary>Read exactly this many bytes from this offset.</summary>
    /// <remarks>
    /// Anything past the end throws <see cref="EndOfStreamException"/>, which is an IOException, so
    /// an offset a damaged table names reads as no answer rather than as a crash.
    /// </remarks>
    private static byte[] At(Stream file, long offset, int count)
    {
        if (offset < 0 || offset > file.Length - count)
        {
            throw new EndOfStreamException($"{count} bytes at {offset} is past the end of the disk");
        }

        var bytes = new byte[count];
        file.Position = offset;
        file.ReadExactly(bytes);
        return bytes;
    }
}
