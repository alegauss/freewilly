using FreeWilly.Core.Preflight;
using FreeWilly.Core.Preflight.Windows;

namespace FreeWilly.Core.Agent;

/// <summary>Where this user's own <c>docker</c> command points, behind a seam.</summary>
/// <remarks>
/// <see cref="DockerContextProbe"/> reads a per-user config file and an environment variable, so the
/// answer differs between a developer's machine and a runner with no Docker on it. That is the right
/// behaviour and the wrong input to a measurement — hence the interface, shaped after
/// <see cref="IServiceProbe"/>.
/// </remarks>
public interface IContextProbe
{
    /// <summary>Read where the CLI points.</summary>
    /// <returns>The target.</returns>
    DockerClientTarget Read();
}

/// <summary>The real read, which is this machine's own config.</summary>
public sealed class ContextProbe : IContextProbe
{
    /// <inheritdoc/>
    public DockerClientTarget Read() => DockerContextProbe.Read();
}

/// <summary>What other container engines are on this machine.</summary>
/// <remarks>
/// One member of <see cref="IMachineFacts"/> rather than the whole of it (DD98). The surface needs
/// this to say which of the three causes of "cannot connect" a machine has, and a fake standing in
/// for the whole interface would have to answer eight questions to be handed back one.
/// </remarks>
public interface IRivalEngines
{
    /// <summary>The engines found. Empty is the state an install wants.</summary>
    /// <returns>Them.</returns>
    IReadOnlyList<RivalEngine> Found();
}

/// <summary>The real read, which walks this machine.</summary>
public sealed class RivalEngines : IRivalEngines
{
    /// <inheritdoc/>
    public IReadOnlyList<RivalEngine> Found() => new WindowsMachineFacts().RivalEngines;
}

/// <summary>When it is, behind a seam.</summary>
/// <remarks>
/// The one input on this list that is not a read of the machine at all, which is why no seam named it
/// for three tasks (DD178). A row that states a span rather than a date is as much a function of the
/// clock as of the fixture behind it, so a figure measured to the token is a figure the calendar can
/// move.
/// </remarks>
public interface IClock
{
    /// <summary>Read the time.</summary>
    /// <returns>Now.</returns>
    DateTimeOffset Now();
}

/// <summary>The real read, which is this machine's own clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset Now() => DateTimeOffset.UtcNow;
}

/// <summary>
/// What a read verb learns from Windows rather than from the engine (DD78).
/// </summary>
/// <remarks>
/// Every other input to a read verb arrives through <c>IEngineReads</c>, which a measurement can
/// serve from fixtures. These two could not: <c>read context</c> constructed a
/// <see cref="WindowsMachineFacts"/> to name the CLI's context and <c>read doctor</c> constructed a
/// <see cref="HostPorts"/> to ask whether anything held the container's published port, both inside
/// the verb where nothing could reach them.
///
/// <para><b>What that cost.</b> DD65's shaped token figure had to be banded at 15% to survive the
/// two, against a measured variance of about 5% — so a response that grew by 100 tokens landed inside
/// the band and the gate said nothing. A gate is only as tight as its least deterministic input, and
/// these were it.</para>
///
/// <para><b><c>read verify</c>'s probe is here too</b>, though it was not one of the two: it connects
/// to a host port, the dispatcher constructed it, and it stays silent on the measured task only
/// because that fixture's container is exited and nothing is probed for one of those. A seam that
/// holds while a fixture does is not a seam.</para>
///
/// <para><b>Lazy on purpose.</b> Every member is a seam rather than a value, so a verb that needs
/// none reads none. Constructing this must not make <c>read ps</c> open a config file.</para>
/// </remarks>
public sealed class MachineReads
{
    /// <summary>This machine, which is what every caller but a measurement wants.</summary>
    public static MachineReads OfThisMachine { get; } = new();

    /// <summary>What Windows is listening on.</summary>
    public IHostPorts Ports { get; init; } = new HostPorts();

    /// <summary>
    /// What state WSL, the distribution and the disk under the engine are in (DD198).
    /// </summary>
    /// <remarks>
    /// A function of the engine rather than a value, because the report asks the pipe one question
    /// and a verb on this surface is handed the engine it is to use. One that opened its own would
    /// be the single read here that no fake daemon could stand behind, which is exactly the hole
    /// <c>Every_read_verb_issues_only_GET_requests</c> exists to find — and did.
    ///
    /// <para>The window reaches the same report by its other door, so the two surfaces answer from
    /// one implementation rather than each asking the machine in its own spelling.</para>
    /// </remarks>
    public Engine.IMachineReports Health { get; init; } = new Engine.LiveMachineReport.Reports();

    /// <summary>The engine's own journal, for the tail <c>read health --journal</c> asks for.</summary>
    /// <remarks>
    /// Behind the seam for the reason the rest are: it is a file on the machine, and a verb reading
    /// one from inside its own body is a verb no test can put a fixture under.
    /// </remarks>
    public Engine.IEngineJournal Journal { get; init; } = new Engine.EngineJournalFile();

    /// <summary>Where the user's own <c>docker</c> command points.</summary>
    public IContextProbe Client { get; init; } = new ContextProbe();

    /// <summary>What reaches a published port from Windows.</summary>
    public IServiceProbe Service { get; init; } = new ServiceProbe();

    /// <summary>What process on Windows holds a port.</summary>
    /// <remarks>
    /// The fourth, and DD98 is how it was found: it was constructed in the dispatcher exactly as the
    /// probe was, and the design that named the seams did not name it. Off the measured task only
    /// because <c>read ports</c> is not one of the four verbs the benchmark drives — which is a fact
    /// about the benchmark and not about this being safe.
    /// </remarks>
    public IPortOwners Owners { get; init; } = new PortOwners();

    /// <summary>What other container engines are on this machine.</summary>
    /// <remarks>
    /// Reached only on the refusal path, where the surface says which of the three causes of "cannot
    /// connect" this machine has. That path is off the measurement because the benchmark's daemon
    /// answers — so it is here for the reason the others are, and one step ahead of needing to be.
    /// </remarks>
    public IRivalEngines Rivals { get; init; } = new RivalEngines();

    /// <summary>Whether a bind source is there inside the distribution the engine runs in.</summary>
    /// <remarks>
    /// The first read on this list that is squarely on the measured task: <c>read doctor</c> is one
    /// of the four verbs the benchmark drives, and DD101 gives its mounts row a subprocess. DD98
    /// said the seam was held by memory alone and this is the change that would have tested it — so
    /// the read arrives here rather than inside the verb, and the benchmark keeps answering from
    /// fixtures instead of running <c>wsl.exe</c>.
    /// </remarks>
    public IBindSources Sources { get; init; } = new BindSources();

    /// <summary>When the verb is reading, for a row that states a span rather than a date.</summary>
    /// <remarks>
    /// The seventh, and the one three tasks of seam-finding walked past because it is not a read of
    /// Windows: <c>read doctor</c>'s restarts row says how long ago the container last started, the
    /// fixture's <c>StartedAt</c> is a fixed date, and <c>DateTimeOffset.UtcNow</c> inside the verb
    /// made the width of that string a function of today. The span went from <c>9d</c> to <c>10d</c>
    /// eleven days after the fixture was written, and the exact figure DD78 made exact went red on a
    /// tree nobody had touched — the same defect as a response that grew, with no commit to blame.
    ///
    /// <para><c>ReadChanges</c> already took its own <c>now</c> for this reason and now defaults to
    /// this one, so the surface has a single clock rather than a seam and a parameter that could
    /// disagree.</para>
    /// </remarks>
    public IClock Clock { get; init; } = new SystemClock();
}
