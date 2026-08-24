using FreeWilly.Core.Engine;

namespace FreeWilly.Core.Fixtures;

/// <summary>
/// A journal that is always there, so the Engine page can be looked at without one (DD165, L6).
/// </summary>
/// <remarks>
/// <b>It is the run of 21 August 2026, which is the failure this whole page was built for.</b> The
/// engine goes quiet, the host spends its quick attempts, the machine stays down, and a human
/// eventually clicks Start — with the lines DD162, DD163 and DD174 added, which is what that day
/// was missing. A fixture chosen to look healthy would photograph the one state nobody needs to
/// see.
///
/// <para><b>Both ends of the silence are here since DD174</b>, twenty seconds apart, because that
/// is now what the host writes and a page illustrating a journal it cannot produce is the coupling
/// DD165 was about.</para>
///
/// <para><b>And the silent readings name the distribution since DD175.</b> A suspend took the
/// virtual machine that afternoon, so "the daemon is running" is the one thing this machine would
/// not have said about itself — it was the sentence that hid the failure, and a fixture is a poor
/// place to keep the last copy of it.</para>
///
/// <para><b>Fixed, never relative to now.</b> A capture is compared byte for byte, so a stamp
/// computed against the clock would make every picture differ from the last — the same rule
/// <see cref="SampleBuilds"/> follows and for the same reason.</para>
///
/// <para><b>The path is not a real one.</b> A capture of this page carries whatever is in this
/// field into a README, and the real path names the account it was taken on.</para>
/// </remarks>
public sealed class SampleJournal : IEngineJournal
{
    /// <inheritdoc/>
    public string Path => @"C:\Users\example\AppData\Local\FreeWilly\engine.log";

    /// <inheritdoc/>
    public IReadOnlyList<string> Read() =>
    [
        "2026-08-21 09:19:41  tray      running as pid 8124 (FreeWilly 1.0.1)",
        "2026-08-21 09:19:42  host      serving as pid 9330 (FreeWilly 1.0.1)",
        "2026-08-21 09:19:42  Running   the engine answered on \\\\.\\pipe\\docker_engine",
        "2026-08-21 09:19:42  Engine API 1.55",
        "2026-08-21 09:19:42  Serving the engine. Ctrl+C stops it.",
        "2026-08-21 12:04:10  power     the machine is suspending",
        "2026-08-21 12:41:55  power     the machine came back",
        "2026-08-21 12:41:58  Starting  freewilly is not running and no answer within 3s",
        "2026-08-21 12:42:07  Running   " + EngineRevival.RestartMark + " (restart 1)",
        "2026-08-21 14:34:51  Starting  freewilly is not running and no answer within 3s "
            + "— first quiet poll",
        "2026-08-21 14:35:11  Starting  freewilly is not running and no answer within 3s "
            + "— 6 polls in a row",
        "2026-08-21 14:35:11  tray      the engine stopped answering",
        "2026-08-21 14:37:45  Stopped   the daemon exited while starting — wsl.exe exited -1: "
            + "The Windows Subsystem for Linux instance has terminated.",
        "2026-08-21 14:42:00  Stopped   the daemon exited while starting — wsl.exe exited -1: "
            + "The Windows Subsystem for Linux instance has terminated.",
        "2026-08-21 14:42:00  Starting  freewilly is not running and no answer within 3s — 5 attempts "
            + "have failed; still trying, now every 5 minutes",
        "2026-08-21 15:45:41  Running   " + EngineRevival.RestartMark + " (restart 2)",
        "2026-08-21 15:45:41  tray      the engine is answering",
    ];
}
