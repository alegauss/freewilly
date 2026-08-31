using System.Diagnostics;
using FreeWilly.Core.Engine;

namespace FreeWilly.Core.Releases;

/// <summary>What fetching a release produced.</summary>
/// <param name="Path">Where the verified installer is, when that is what happened.</param>
/// <param name="Failure">Why there is no verified installer, when that is what happened.</param>
public sealed record FetchedRelease(string? Path, string? Failure)
{
    /// <summary>Whether there is an installer at <see cref="Path"/> that may be run.</summary>
    public bool Verified => Failure is null && Path is not null;
}

/// <summary>
/// Downloading a release's installer, and refusing one whose digest is not what the release published
/// (DD154).
/// </summary>
/// <remarks>
/// <b>It is <see cref="ArtefactStore"/> doing the work, on purpose.</b> Every artefact this tool
/// fetches is written, hashed and deleted unless the hash matches, and a self-update that ran an
/// unverified <c>.exe</c> would be the one download the whole product trusts blindly. The only
/// difference from a provision is where the digest comes from: the engine's are pinned inside the
/// build, and this one is published beside the installer, so it is fetched first and the artefact is
/// constructed from it.
///
/// <para><b>Which is worth being honest about.</b> A digest served from the same host as the file it
/// describes proves the download arrived intact — it is not a signature, and it does not prove who
/// built it. What it removes is the failure that actually happens: a truncated download, a proxy that
/// rewrote the body, a mirror that is stale. Signing is a separate question with a separate answer,
/// and pretending this is it would be worse than not checking at all.</para>
///
/// <para><b>%TEMP% rather than the downloads directory.</b> That one exists so a repeated provision
/// does not re-fetch a quarter of a gigabyte; an installer is used once and then is litter.</para>
/// </remarks>
public sealed class ReleaseUpdate
{
    /// <summary>
    /// What the installer is run with. Silent, and it relaunches the app it just replaced.
    /// </summary>
    /// <remarks>
    /// <c>/SILENT</c> shows a progress bar and no wizard; Restart Manager closes the running tray,
    /// which <c>CloseApplications=yes</c> in <c>installer.iss</c> is carried for.
    ///
    /// <para><c>/RELAUNCH=yes</c> is this project's own, and the installer's <c>[Run]</c> section
    /// reads it. That entry is <c>skipifsilent</c>, so that an unattended install pushed to a machine
    /// does not make a tray icon appear in somebody's session — and a silent install that is a
    /// self-update is the one case that must relaunch. A test holds this string and the installer's
    /// check to each other.</para>
    /// </remarks>
    public const string SilentArguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=yes";

    private readonly IArtefactFetcher _fetcher;
    private readonly string _directory;

    /// <summary>Construct against a fetcher and a directory.</summary>
    /// <param name="fetcher">How bytes are obtained.</param>
    /// <param name="directory">Where the sums file and the installer are written.</param>
    public ReleaseUpdate(IArtefactFetcher fetcher, string directory)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _fetcher = fetcher;
        _directory = directory;
    }

    /// <summary>Where a downloaded installer goes.</summary>
    public static string DefaultDirectory =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreeWilly-update");

    /// <summary>Get the installer onto disk, verified against the digest the release published.</summary>
    /// <param name="release">What to fetch.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>Where it is, or why it is not there.</returns>
    public async Task<FetchedRelease> FetchAsync(
        AvailableRelease release, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        Directory.CreateDirectory(_directory);

        // The sums file first, because without it there is nothing to check the installer against and
        // no reason to spend the larger download.
        var sumsPath = System.IO.Path.Combine(_directory, ReleaseCheck.SumsAssetName);
        try
        {
            await _fetcher.FetchAsync(release.SumsUrl, sumsPath, cancellation).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return new FetchedRelease(
                null, $"downloading {ReleaseCheck.SumsAssetName} failed: {failure.Message}");
        }

        string sums;
        try
        {
            sums = await File.ReadAllTextAsync(sumsPath, cancellation).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new FetchedRelease(
                null, $"reading {ReleaseCheck.SumsAssetName} failed: {failure.Message}");
        }

        if (ReleaseSums.DigestFor(sums, release.InstallerName) is not { } digest)
        {
            return new FetchedRelease(
                null,
                $"{ReleaseCheck.SumsAssetName} in {release.Tag} does not publish a SHA-256 for "
                + $"{release.InstallerName}, so there is nothing to verify the download against");
        }

        // From here it is an ordinary artefact with a pinned digest, and the store is the code that
        // already refuses one whose digest is wrong — including on a cache hit, which matters here
        // because a half-downloaded installer from an interrupted attempt is the common case.
        var artefact = new Artefact(
            "installer", release.Version.ToString(3), release.InstallerUrl,
            release.InstallerName, digest);

        var acquired = await new ArtefactStore(_fetcher, _directory)
            .AcquireAsync(artefact, cancellation).ConfigureAwait(false);

        return new FetchedRelease(acquired.Path, acquired.Failure);
    }

    /// <summary>Run a verified installer and return.</summary>
    /// <param name="installerPath">The file <see cref="FetchAsync"/> verified.</param>
    /// <remarks>
    /// The caller must be on its way out: the running <c>.exe</c> has to be released before it can be
    /// replaced, and Restart Manager closing the tray from under itself is a worse exit than quitting
    /// on purpose. Nothing is waited on — the installer outlives this process by design.
    /// </remarks>
    public static void Run(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        using var started = Process.Start(new ProcessStartInfo(installerPath)
        {
            Arguments = SilentArguments,
            UseShellExecute = true,

            // Named and not inherited, and here it is the point rather than the precaution (DD261).
            // This child outlives the process starting it in order to replace the install, and a
            // directory it inherited and held is one the install could then not write over.
            WorkingDirectory = Environment.SystemDirectory,
        });
    }
}
