using System.Globalization;

namespace FreeWilly.Core.Engine;

/// <summary>
/// What the owned distribution's root filesystem says about itself, in one reading (DD197).
/// </summary>
/// <param name="Device">The block device it is mounted from, as the distribution sees it.</param>
/// <param name="Options">The mount options, comma-separated, exactly as the kernel lists them.</param>
/// <param name="Errors">How many errors ext4 has recorded against it, or null where it would not say.</param>
/// <param name="FirstError">The function that recorded the first one, or null where there is none.</param>
/// <param name="LastError">The function that recorded the most recent one, or null.</param>
/// <param name="BlocksKb">How large the filesystem says it is, in kilobytes.</param>
/// <param name="UsedKb">How much of it is in use, in kilobytes.</param>
/// <remarks>
/// <para><b>One reading for two callers.</b> The start check asks whether anything is wrong (DD200)
/// and the Engine page shows a user what the answer was made of (DD197). Those are the same six
/// questions, and asking them twice is how the page comes to disagree with the journal beside it.
/// </para>
///
/// <para><b>Nothing here is a package</b>, which is the constraint DD201 established the hard way.
/// <c>/proc/mounts</c>, <c>/sys/fs/ext4</c> and BusyBox's <c>df</c> and <c>awk</c> are all a
/// minirootfs has, and every distribution provisioned before DD196 has nothing else.</para>
/// </remarks>
public sealed record DistributionState(
    string Device,
    string Options,
    int? Errors,
    string? FirstError,
    string? LastError,
    long? BlocksKb,
    long? UsedKb)
{
    /// <summary>The one shell call that answers all of it.</summary>
    /// <remarks>
    /// <c>df</c> against <c>/</c> rather than against the device, because a filesystem reports its
    /// own size and a virtual disk that grows reports the ceiling it may grow to. Both numbers are
    /// wanted and they are different questions: this is the one asked from inside.
    /// </remarks>
    internal const string Script =
        "d=$(" + Minirootfs.RootDevice + "); b=${d##*/}; s=/sys/fs/ext4/$b; "
        + "echo device=$d; "
        + "echo options=$(awk '$2==\"/\"{print $4;exit}' /proc/mounts); "
        + "echo errors=$(cat $s/errors_count 2>/dev/null || echo unknown); "
        + "echo first=$(cat $s/first_error_func 2>/dev/null || echo unknown); "
        + "echo last=$(cat $s/last_error_func 2>/dev/null || echo unknown); "
        + "echo blocks=$(df -k / | awk 'NR==2{print $2}'); "
        + "echo used=$(df -k / | awk 'NR==2{print $3}')";

    /// <summary>Whether the root is still writable.</summary>
    /// <remarks>
    /// Split rather than searched, because the options carry <c>errors=remount-ro</c> on every
    /// healthy mount: a filesystem that has never had a fault says the word this is looking for, and
    /// the difference is that <c>ro</c> stands alone.
    /// </remarks>
    public bool Writable => !Options.Split(',').Contains("ro", StringComparer.Ordinal);

    /// <summary>Whether ext4 has recorded anything against it.</summary>
    public bool Faulted => Errors is > 0 || !Writable;

    /// <summary>Read what <see cref="Script"/> printed.</summary>
    /// <param name="said">Its output.</param>
    /// <returns>The state, or <see langword="null"/> where it named no device.</returns>
    /// <remarks>
    /// A field present but empty is the ordinary reading of <c>first_error_func</c> on a healthy
    /// filesystem: <c>cat</c> succeeds and prints nothing, so the fallback has to catch that as well
    /// as a file that is not there.
    /// </remarks>
    public static DistributionState? Of(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        var fields = said
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        var device = Said(fields, "device");
        return device is null
            ? null
            : new DistributionState(
                device,
                Said(fields, "options") ?? "",
                Number(fields, "errors") is { } errors ? (int)errors : null,
                Said(fields, "first"),
                Said(fields, "last"),
                Number(fields, "blocks"),
                Number(fields, "used"));
    }

    private static string? Said(Dictionary<string, string> fields, string name) =>
        fields.GetValueOrDefault(name, "") is { Length: > 0 } value
            && !string.Equals(value, "unknown", StringComparison.Ordinal)
            ? value
            : null;

    private static long? Number(Dictionary<string, string> fields, string name) =>
        Said(fields, name) is { } value
            && long.TryParse(value, CultureInfo.InvariantCulture, out var read)
            ? read
            : null;
}
