using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Writes the least of a VHDX that <see cref="Vhdx"/> reads: the identifier, a region table naming the
/// block allocation table, one table entry, and an ext4 superblock in the block it points at.
/// </summary>
/// <remarks>
/// Laid out the way the file measured on 16 September 2026 was, scaled down: that one had its table
/// at 3 MiB and payload block 0 at 12 MiB, and this puts them at 320 KiB and 1 MiB so a test disk is
/// a megabyte rather than twelve.
/// </remarks>
internal static class VirtualDiskFile
{
    internal const long RegionTable = 0x30000;
    internal const long RegionTableCopy = 0x40000;
    private const long AllocationTableAt = 0x50000;
    private const long BlockMegabytes = 1;

    /// <summary>A block whose data is all in the file.</summary>
    internal const ulong FullyPresent = 6;

    /// <summary>A block the disk has never written.</summary>
    internal const ulong NotPresent = 0;

    private static readonly Guid AllocationTable = new("2DC27766-F623-4200-9D64-115E9BFD4A08");
    private static readonly Guid Metadata = new("8B7CA206-4790-4B9A-B8FE-575F050F886E");

    /// <summary>Write a disk carrying this filesystem.</summary>
    /// <param name="path">Where.</param>
    /// <param name="uuid">The filesystem's UUID, as <c>blkid</c> prints it.</param>
    /// <param name="state">What the table says about block 0.</param>
    /// <param name="ext4">Whether the block carries ext4's magic number.</param>
    /// <param name="regionTableAt">Which copy of the region table to write.</param>
    /// <param name="identifier">The first eight bytes.</param>
    internal static void Write(
        string path,
        string uuid,
        ulong state = FullyPresent,
        bool ext4 = true,
        long regionTableAt = RegionTable,
        string identifier = "vhdxfile")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);

        var block = BlockMegabytes << 20;
        file.SetLength(block + 4096);

        Put(file, 0, System.Text.Encoding.ASCII.GetBytes(identifier));

        // Two regions, the metadata first, so the reader has to look for the one it wants.
        var table = new byte[16 + 2 * 32];
        System.Text.Encoding.ASCII.GetBytes("regi").CopyTo(table, 0);
        BitConverter.GetBytes(2u).CopyTo(table, 8);
        Metadata.TryWriteBytes(table.AsSpan(16));
        BitConverter.GetBytes((ulong)0x60000).CopyTo(table, 32);
        AllocationTable.TryWriteBytes(table.AsSpan(48));
        BitConverter.GetBytes((ulong)AllocationTableAt).CopyTo(table, 64);
        Put(file, regionTableAt, table);

        Put(file, AllocationTableAt, BitConverter.GetBytes((BlockMegabytes << 20) | state));

        if (ext4)
        {
            Put(file, block + 1024 + 0x38, BitConverter.GetBytes((ushort)0xEF53));
        }

        Put(file, block + 1024 + 0x68, Convert.FromHexString(uuid.Replace("-", "", StringComparison.Ordinal)));
    }

    private static void Put(FileStream file, long offset, byte[] bytes)
    {
        file.Position = offset;
        file.Write(bytes);
    }
}

/// <summary>Reading a disk's filesystem UUID without the distribution on it (DD280).</summary>
public sealed class VhdxTests : IDisposable
{
    private const string Uuid = "9cb04147-49e5-4515-8e3c-0ecdca70eb3f";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fw-vhdx-{Guid.NewGuid():N}");

    private string Disk => Path.Combine(_directory, "ext4.vhdx");

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void The_uuid_is_spelled_the_way_blkid_prints_it()
    {
        // Lower case with the four dashes, because it is compared against a blkid listing and a
        // reading that spelled it differently would be a disk that is never found.
        VirtualDiskFile.Write(Disk, Uuid);

        Assert.Equal(Uuid, Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void The_copy_of_the_region_table_answers_where_the_first_is_not_there()
    {
        VirtualDiskFile.Write(Disk, Uuid, regionTableAt: VirtualDiskFile.RegionTableCopy);

        Assert.Equal(Uuid, Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void A_disk_held_open_for_writing_is_still_read()
    {
        // Measured with the utility VM holding the file. The reader must share rather than be
        // refused, and must not stop the VM writing either.
        VirtualDiskFile.Write(Disk, Uuid);
        using var held = new FileStream(Disk, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(Uuid, Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void A_disk_that_is_not_there_is_no_answer()
    {
        Assert.Null(Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void A_file_that_is_not_a_vhdx_is_no_answer()
    {
        VirtualDiskFile.Write(Disk, Uuid, identifier: "notvhdx!");

        Assert.Null(Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void A_block_the_disk_never_wrote_is_no_answer()
    {
        // Reading the bytes where the block would be would find whatever else is there.
        VirtualDiskFile.Write(Disk, Uuid, state: VirtualDiskFile.NotPresent);

        Assert.Null(Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void A_block_without_ext4_in_it_is_no_answer()
    {
        // Sixteen bytes at 0x468 are a UUID only where the superblock says it is one.
        VirtualDiskFile.Write(Disk, Uuid, ext4: false);

        Assert.Null(Vhdx.FilesystemUuid(Disk));
    }

    [Fact]
    public void A_file_cut_short_is_no_answer_rather_than_a_crash()
    {
        VirtualDiskFile.Write(Disk, Uuid);
        using (var file = new FileStream(Disk, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(VirtualDiskFile.RegionTableCopy);
        }

        Assert.Null(Vhdx.FilesystemUuid(Disk));
    }
}
