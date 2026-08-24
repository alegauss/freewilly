using FreeWilly.Core.Releases;

namespace FreeWilly.Tray;

/// <summary>
/// When the tray asks whether a newer release exists, and how often (DD154).
/// </summary>
/// <remarks>
/// One check shortly after launch and then one every six hours, which is claude-tray's cadence and is
/// chosen for the same reason: a release happens a few times a year, and anything more frequent is
/// spending somebody's rate-limit budget to learn nothing. Sixty unauthenticated requests an hour is
/// a shared NAT's whole allowance, so four a day per machine is the point.
///
/// <para><b>There is no switch (DD171).</b> The check shipped off and was gated on a menu tick, and a
/// check nobody turns on is a check nobody has — the 1.0.1 notes already had to tell readers that
/// upgrading from 1.0.0 was a manual download, and off by default is that same failure by choice.
/// claude-tray asks on every launch with nothing to enable, and this is the surface it is the
/// reference for.</para>
///
/// <para><b>It announces a version once.</b> The menu item stays offered for as long as the release
/// is newer, but a balloon every six hours about the same release is nagging, and this product's
/// whole argument is about not being the tool that does that.</para>
/// </remarks>
internal sealed class ReleaseWatch : IDisposable
{
    /// <summary>How long after a launch the first check happens.</summary>
    /// <remarks>
    /// Not immediately. The tray's constructor may be starting an engine and provisioning a
    /// distribution, and a release check is the least urgent thing this process does — it can wait
    /// until that has had the machine to itself.
    /// </remarks>
    internal static readonly TimeSpan FirstCheckAfter = TimeSpan.FromSeconds(20);

    /// <summary>How often it asks after that.</summary>
    internal static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private readonly Func<IReleaseFeed> _feed;
    private readonly Action<AvailableRelease, bool> _found;
    private readonly Lock _gate = new();
    private System.Threading.Timer? _timer;
    private Version? _announced;

    /// <summary>Construct a watch.</summary>
    /// <param name="found">
    /// What to do about a newer release, and whether this is the first tick to name that version —
    /// which is what separates offering it from announcing it. Called off the UI thread; the caller
    /// marshals.
    /// </param>
    /// <param name="feed">
    /// How to build a feed. A factory rather than a feed, so the one thing here that touches the
    /// network is constructed per check and disposed with it.
    /// </param>
    internal ReleaseWatch(Action<AvailableRelease, bool> found, Func<IReleaseFeed>? feed = null)
    {
        ArgumentNullException.ThrowIfNull(found);
        _found = found;
        _feed = feed ?? (() => new GitHubReleaseFeed());
    }

    /// <summary>Start asking.</summary>
    internal void Start()
    {
        lock (_gate)
        {
            // System.Threading's and not WinForms': the check has no business on the UI thread, and
            // the callback is marshalled by whoever passed `found` rather than by the timer.
            _timer ??= new System.Threading.Timer(_ => Tick(), null, FirstCheckAfter, Every);
        }
    }

    /// <summary>Ask now, whatever the timer was going to do.</summary>
    /// <returns>The release, or <see langword="null"/> where there is none or nothing answered.</returns>
    /// <remarks>
    /// Awaitable because a test has to be able to observe one check without waiting twenty seconds
    /// for a timer. <see cref="Tick"/> is the same call with the result dropped.
    /// </remarks>
    internal async Task<AvailableRelease?> CheckAsync(CancellationToken cancellation = default)
    {
        IReleaseFeed feed;
        try
        {
            feed = _feed();
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // A client that could not even be constructed is the same answer as an unreachable host,
            // and for the same reason: nobody asked to be told about this.
            return null;
        }

        try
        {
            var release = await new ReleaseCheck(feed).NewerAsync(cancellation).ConfigureAwait(false);
            if (release is null)
            {
                return null;
            }

            bool first;
            lock (_gate)
            {
                first = _announced is null || release.Version > _announced;
                if (first)
                {
                    _announced = release.Version;
                }
            }

            _found(release, first);
            return release;
        }
        finally
        {
            (feed as IDisposable)?.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Tick() => _ = CheckAsync();
}
