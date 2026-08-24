using System.Text.Json;

namespace FreeWilly.Core.Settings;

/// <summary>
/// Everything the user has decided about how this tool behaves, and the file that remembers it.
/// </summary>
/// <remarks>
/// <b>One record because there is one file.</b> This was <c>EngineOnLaunch</c>, which held the single
/// setting DD135 introduced and was named after it. DD154 needed a second, and a second record over
/// the same path would have deleted the first value on every write: <see cref="Write"/> serialises
/// the object it is called on, so two writers each round-trip only their own property and each one's
/// save is the other one's reset. So the type is named after the file rather than after either
/// setting, and a third setting joins it here.
///
/// <para><b>Still not a settings system.</b> One value in a small file beside everything else this
/// tool owns, read the way <c>WindowMemory</c> is read: every failure answers with the defaults,
/// because a truncated preference file is not a reason to refuse to start an engine.</para>
///
/// <para><b>DD171 took the second setting back out.</b> <c>CheckForReleases</c> lived here and shipped
/// off, and a check nobody turns on is a check nobody has. The tray now asks on every launch the way
/// claude-tray does, so there is no switch to remember — and a file written by an older install still
/// carrying the property is read without it, because an unknown member is not an error.</para>
/// </remarks>
public sealed record TraySettings
{
    /// <summary>What an install with nothing written down does about the engine.</summary>
    /// <remarks>
    /// A constant rather than a literal on the property, because two things have to agree about it:
    /// the default a missing file resolves to, and the test that holds this to shipping on. Written
    /// once, they cannot drift.
    /// </remarks>
    public const bool EngineShipsOn = true;

    private static readonly JsonSerializerOptions Layout = new() { WriteIndented = true };

    /// <summary>Whether opening the tray also starts the engine (DD135).</summary>
    public bool StartWithTheTray { get; init; } = EngineShipsOn;

    /// <summary>Read the settings.</summary>
    /// <param name="path">The file <see cref="Write"/> wrote.</param>
    /// <returns>What it held, or the defaults where there is nothing usable.</returns>
    /// <remarks>
    /// Never null, unlike the window's own reader. The caller of that one has a meaningful "no
    /// history" branch — open where a window with no past opens — and this one does not: there are
    /// defaults, and handing back null would only move the same decision to every call site.
    /// </remarks>
    public static TraySettings Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return (File.Exists(path)
                ? JsonSerializer.Deserialize<TraySettings>(File.ReadAllText(path))
                : null) ?? new TraySettings();
        }
        catch (Exception failure) when (failure is IOException or JsonException
            or UnauthorizedAccessException or NotSupportedException)
        {
            return new TraySettings();
        }
    }

    /// <summary>Write this down.</summary>
    /// <param name="path">Where to write it.</param>
    /// <remarks>
    /// Silent on failure, because this runs from a menu click on the UI thread and an unhandled
    /// exception there takes the tray icon with it — the defect a click handler that threw already
    /// caused once. The setting not sticking is a smaller loss than the icon vanishing.
    /// </remarks>
    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Layout));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
            or NotSupportedException)
        {
        }
    }
}
