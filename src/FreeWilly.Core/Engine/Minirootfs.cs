namespace FreeWilly.Core.Engine;

/// <summary>
/// Shell that works inside the distribution this project installs (DD201).
/// </summary>
/// <remarks>
/// <para><b>BusyBox is not util-linux, and two tasks shipped code that assumed it was.</b> The root
/// filesystem is an Alpine minirootfs, so <c>findmnt</c>, <c>lsblk</c>, <c>dumpe2fs</c> and
/// <c>e2fsck</c> are simply absent, and <c>blkid</c> is BusyBox's applet rather than the one whose
/// manual page everybody has read. Measured against the live distribution on 29 August 2026, which
/// is the only way any of this was going to be found: nothing on a Windows build machine runs a
/// command inside a distribution that is not there.</para>
///
/// <para>The absences are worse than they look because two of them fail quietly. <c>findmnt</c>
/// exits 127 and is obvious. <c>blkid -U &lt;uuid&gt;</c> is <em>accepted</em> by BusyBox, exits
/// zero, and prints nothing — so a caller that reads its output gets an empty string and concludes
/// the disk is gone.</para>
///
/// <para>Here rather than at either call site, because both of them need the same lookup and a
/// second spelling of it is where the pair drifts apart. What is written here is what a minirootfs
/// answers, and a fragment that stops being true is one edit rather than a hunt.</para>
/// </remarks>
internal static class Minirootfs
{
    /// <summary>
    /// Prints the device the root filesystem is mounted from, e.g. <c>/dev/sdd</c>.
    /// </summary>
    /// <remarks>
    /// <c>/proc/mounts</c> is the kernel's, so this needs no package at all and answers on a
    /// distribution provisioned before DD196 put any in one. Matched on the mount point being
    /// exactly <c>/</c> rather than on a substring: a path with a space around a slash is
    /// implausible and a field comparison costs nothing to be sure of.
    /// </remarks>
    internal const string RootDevice = "awk '$2==\"/\"{print $1;exit}' /proc/mounts";

    /// <summary>
    /// Prints every block device with the filesystem on it, one per line.
    /// </summary>
    /// <remarks>
    /// Bare, because BusyBox's <c>blkid</c> answers a device and answers a listing, and takes
    /// <c>-U</c> without doing anything with it. A caller wanting one direction or the other reads
    /// the listing rather than asking for the lookup that silently is not there.
    /// </remarks>
    internal const string BlockDevices = "blkid";

    /// <summary>The line a listing gives for one device, as this shell prints it.</summary>
    /// <example><c>/dev/sdd: UUID="9cb04147-49e5-4515-8e3c-0ecdca70eb3f" TYPE="ext4"</c></example>
    private const string UuidField = "UUID=\"";

    /// <summary>Read the filesystem identifier out of a <c>blkid</c> line.</summary>
    /// <param name="said">One line, or a listing whose first UUID is wanted.</param>
    /// <returns>The identifier, or <see langword="null"/> where the line carries none.</returns>
    /// <remarks>
    /// A device with no filesystem on it prints a line with no <c>UUID=</c> at all, which is an
    /// answer rather than a parse failure: it is a swap partition or an unformatted disk, and
    /// neither is the one being asked about.
    /// </remarks>
    internal static string? UuidIn(string said)
    {
        var at = said.IndexOf(UuidField, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var from = at + UuidField.Length;
        var to = said.IndexOf('"', from);
        return to > from ? said[from..to] : null;
    }

    /// <summary>Find which device carries a filesystem, in a <c>blkid</c> listing.</summary>
    /// <param name="listing">What <see cref="BlockDevices"/> printed.</param>
    /// <param name="uuid">The filesystem's identifier.</param>
    /// <returns>The device path, or <see langword="null"/> where nothing in the listing carries it.</returns>
    /// <remarks>
    /// The direction <c>blkid -U</c> would have answered if BusyBox implemented it. Case-insensitive
    /// on the identifier, because the two ends of this comparison are printed by two different
    /// distributions and a hexadecimal digit's case is not a difference between filesystems.
    /// </remarks>
    internal static string? DeviceIn(string listing, string uuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uuid);

        foreach (var line in listing.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.Equals(UuidIn(line), uuid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var device = colon > 0 ? line[..colon] : "";
            if (device.StartsWith("/dev/", StringComparison.Ordinal))
            {
                return device;
            }
        }

        return null;
    }
}
