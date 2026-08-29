namespace FreeWilly.Tray.Cli;

/// <summary>Who heard a stop before it happened (DD136, DD213).</summary>
/// <param name="Host">
/// Whether an engine host heard it, so it will not read the teardown as the engine dying and put it
/// straight back.
/// </param>
/// <param name="Tray">
/// Whether a tray heard it, so it will not wait out the silence and then announce a stop the user
/// asked for as an outage.
/// </param>
internal readonly record struct StopHeardBy(bool Host, bool Tray);

/// <summary>
/// Says a stop is coming to everything on this session that would otherwise misread it (DD213).
/// </summary>
/// <remarks>
/// <para><b>Two listeners, two different mistakes, one announcement.</b> DD136 gave the host a
/// signal because a <c>--stop</c> from another process is indistinguishable in there from WSL2 dying
/// under a suspend, and a host that could not tell those apart would either ignore the stop or ignore
/// a crash. DD210 gave the tray the same thing for the window, in process, because a tray watching an
/// engine that went quiet waits out the blip and then announces a failure. DD213 is the half that was
/// left: a verb typed at a prompt is somebody asking too, and it reached neither listener.</para>
///
/// <para><b>One call site rather than two lines repeated at each.</b> The two signals are always sent
/// together and always before the teardown, which is the kind of pair that stays right until somebody
/// adds a third caller and copies one of them. DD204 made the same argument about the check's
/// sequence and it holds here for the same reason.</para>
/// </remarks>
internal static class AskedStop
{
    /// <summary>Announce the stop, before anything is taken down.</summary>
    /// <returns>Who was there to hear it.</returns>
    /// <remarks>
    /// Before, and that ordering is the whole mechanism. Both listeners decide what a disappearing
    /// engine means at the moment they notice it, so an announcement arriving afterwards is a
    /// balloon already on its way and a revival already started.
    /// </remarks>
    internal static StopHeardBy Announce() => new(
        SingleEngine.TellTheLiveOneToStop(),
        SingleTray.AskTheLiveOneToExpectAStop());
}
