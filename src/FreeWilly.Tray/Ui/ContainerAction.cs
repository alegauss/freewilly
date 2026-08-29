using FreeWilly.Core.Api;

namespace FreeWilly.Tray.Ui;

/// <summary>One of the four things a row can be asked to do.</summary>
public enum ContainerVerb
{
    /// <summary>Start a stopped container.</summary>
    Start,

    /// <summary>Stop a running one, with the daemon's grace period.</summary>
    Stop,

    /// <summary>Stop and start again.</summary>
    Restart,

    /// <summary>Delete it.</summary>
    Remove,

    /// <summary>
    /// Open a shell in it. Not one of the four endpoints — the wait is the probe that asks the
    /// image which shell it has, and the row owes an account of it like any other.
    /// </summary>
    Shell,
}

/// <summary>
/// What surrounds the four endpoints: the word a pending row shows, whether the user is asked
/// first, and what the question says.
/// </summary>
/// <remarks>
/// The verbs themselves are one line each on <see cref="DockerApi"/>. Everything a person actually
/// notices about pressing one of these buttons is here, which is why it is a file with tests and
/// not a switch inside a click handler.
/// </remarks>
public static class ContainerAction
{
    /// <summary>What a row reads while <paramref name="verb"/> is in flight.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>The present participle, with the ellipsis a wait deserves.</returns>
    public static string PendingLabel(ContainerVerb verb) => verb switch
    {
        ContainerVerb.Start => "Starting…",
        ContainerVerb.Stop => "Stopping…",
        ContainerVerb.Restart => "Restarting…",
        ContainerVerb.Remove => "Removing…",
        ContainerVerb.Shell => "Opening…",
        _ => MixedLabel,
    };

    /// <summary>
    /// What a row reads while more than one different thing is in flight under it (DD108).
    /// </summary>
    /// <remarks>
    /// A project header borrows its wait from its containers, and they are usually all doing the
    /// same thing because the fan-out sent one verb. Usually is not always: a container clicked on
    /// its own while the project is stopping is a second verb under one header, and naming either
    /// of them would be picking one at random.
    /// </remarks>
    public const string MixedLabel = "Working…";

    /// <summary>
    /// Whether removing <paramref name="row"/> asks first.
    /// </summary>
    /// <remarks>
    /// Only the running case. A stopped container is cheap to recreate from its image, so a dialog
    /// there is a click charged for nothing; removing a running one implies a kill, and that is the
    /// part nobody can undo.
    /// </remarks>
    /// <param name="row">The row the button belongs to.</param>
    /// <returns><see langword="true"/> when a dialog is owed.</returns>
    public static bool AsksBeforeRemoving(ContainerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // A project always asks, running or not (DD107). The reasoning that lets a stopped container
        // go without one — cheap to recreate from its image — does not survive being multiplied: a
        // misplaced click there takes a whole stack, and undoing it means finding the compose file
        // this window has never seen.
        return row.IsProject || row.IsLive;
    }

    /// <summary>
    /// The question, which names the container and says what is lost.
    /// </summary>
    /// <remarks>
    /// "Are you sure?" is a question nobody has the information to answer, so this one carries the
    /// name and the consequence and nothing else.
    /// </remarks>
    /// <param name="row">The row the button belongs to.</param>
    /// <returns>The dialog's body.</returns>
    public static string RemovalPrompt(ContainerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.IsProject)
        {
            // The count, not a container: the thing being agreed to is the number, and a dialog that
            // named one service while removing four would be technically true and actively
            // misleading. Volumes are named because they are what does NOT go — `compose down -v` is
            // a different act, and a Remove button must not quietly mean it (DD107).
            var containers = row.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var plural = row.Total == 1 ? "container" : "containers";
            return $"Remove the {row.Name} project?\n\nAll {containers} {plural} go, and anything "
                + "running is killed first. Its volumes stay, and nothing here deletes those.";
        }

        return $"Remove {row.Name}?\n\nIt is {row.State} and will be killed first. "
            + "Anything written inside it that is not on a volume goes with it.";
    }

    /// <summary>
    /// Ask the engine to do <paramref name="verb"/> to <paramref name="id"/>.
    /// </summary>
    /// <param name="api">The client.</param>
    /// <param name="verb">What to do.</param>
    /// <param name="id">The container.</param>
    /// <param name="force">Whether a removal may kill a running container.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public static Task InvokeAsync(
        IEngineClient api,
        ContainerVerb verb,
        string id,
        bool force = false,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        return verb switch
        {
            ContainerVerb.Start => api.StartContainerAsync(id, cancellation),
            ContainerVerb.Stop => api.StopContainerAsync(id, cancellation),
            ContainerVerb.Restart => api.RestartContainerAsync(id, cancellation),
            ContainerVerb.Remove => api.RemoveContainerAsync(id, force, cancellation),
            _ => throw new ArgumentOutOfRangeException(nameof(verb)),
        };
    }
}

/// <summary>
/// What each row is waiting for and what its last failure was, between refreshes.
/// </summary>
/// <remarks>
/// The list is rebuilt from the daemon on every event, so a row is a new object each time and
/// cannot carry this itself. It lives here, keyed by container id, and is projected onto the rows
/// as they are built.
/// </remarks>
public sealed class RowActivity
{
    private readonly Dictionary<string, ContainerVerb> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);

    /// <summary>An action was sent. The row goes pending immediately, before any response.</summary>
    /// <param name="id">The container.</param>
    /// <param name="verb">What was asked.</param>
    public void Began(string id, ContainerVerb verb)
    {
        _pending[id] = verb;

        // Pressing a button is how somebody says they have read the last failure.
        _failures.Remove(id);
    }

    /// <summary>
    /// The event stream confirmed something happened to this container.
    /// </summary>
    /// <remarks>
    /// The HTTP response is not what ends the pending state. A 204 from <c>/stop</c> means the
    /// daemon accepted the call, not that the container is down — the <c>die</c> event is the one
    /// that says so, and it is the same event that makes the list correct.
    /// </remarks>
    /// <param name="id">The container.</param>
    public void Settled(string id) => _pending.Remove(id);

    /// <summary>
    /// Forget this row's last failure without claiming anything is in flight (DD108).
    /// </summary>
    /// <remarks>
    /// <see cref="Began"/> does this too, and says so — pressing a button is how somebody says they
    /// have read the last failure. A project header needs the clearing without the claim: its wait
    /// is derived from its containers rather than stored here, so marking it pending would put a
    /// second, staler answer beside the one that is actually correct.
    /// </remarks>
    /// <param name="id">The row.</param>
    public void Acknowledged(string id) => _failures.Remove(id);

    /// <summary>The engine refused. The row stops waiting and says why, where it was clicked.</summary>
    /// <param name="id">The container.</param>
    /// <param name="message">The engine's own sentence.</param>
    public void Failed(string id, string message)
    {
        _pending.Remove(id);
        _failures[id] = message;
    }

    /// <summary>Forget everything about containers that are no longer in the list.</summary>
    /// <param name="present">The ids the daemon just returned.</param>
    public void Prune(IEnumerable<string> present)
    {
        var alive = new HashSet<string>(present, StringComparer.Ordinal);
        foreach (var id in _pending.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _pending.Remove(id);
        }

        foreach (var id in _failures.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _failures.Remove(id);
        }
    }

    /// <summary>Put this row's pending state and failure on it.</summary>
    /// <param name="row">The freshly projected row.</param>
    /// <returns>The same row, carrying whatever is known about it.</returns>
    public ContainerRow Dress(ContainerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row with { Pending = PendingFor(row.Id), Failure = FailureFor(row.Id) };
    }

    /// <summary>
    /// The same, for an image row.
    /// </summary>
    /// <remarks>
    /// Images are rebuilt from the daemon on every refresh exactly as containers are, and a removal
    /// that is refused has the same thing to say in the same place. One tracker, two shapes of row.
    /// </remarks>
    /// <param name="row">The freshly projected row.</param>
    /// <returns>The same row, carrying whatever is known about it.</returns>
    public ImageRow Dress(ImageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row with { Pending = PendingFor(row.Id), Failure = FailureFor(row.Id) };
    }

    /// <summary>The same, for a volume row.</summary>
    /// <param name="row">The freshly projected row.</param>
    /// <returns>The same row, carrying whatever is known about it.</returns>
    public VolumeRow Dress(VolumeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row with { Pending = PendingFor(row.Name), Failure = FailureFor(row.Name) };
    }

    private string? PendingFor(string id) =>
        _pending.TryGetValue(id, out var verb) ? ContainerAction.PendingLabel(verb) : null;

    private string? FailureFor(string id) =>
        _failures.TryGetValue(id, out var failure) ? failure : null;
}
