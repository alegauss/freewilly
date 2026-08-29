using FreeWilly.Core.Engine;

namespace FreeWilly.Tray;

/// <summary>
/// The tray's answer to a window that is about to take the engine down (DD210).
/// </summary>
/// <param name="expected">
/// What the tray does when a stop is somebody asking rather than a fault: the same bookkeeping
/// Stop engine does, minus the stopping, because the caller is doing that itself.
/// </param>
/// <param name="startAgain">
/// The tray's own start, and deliberately not a second one. It is the thing that knows a start
/// cannot land on a machine with no distribution registered (DD120), and it owns the state the icon
/// shows.
/// </param>
/// <remarks>
/// Two methods on the tray rather than a type of its own there, and this is the join. It exists so
/// <see cref="EngineSeams"/> can carry an interface like everything else it holds: a seam that was a
/// pair of raw delegates would be one a test could not name and a fixture could not refuse.
/// </remarks>
internal sealed class TrayInterlude(Action expected, Action startAgain) : IEngineInterlude
{
    /// <inheritdoc/>
    public void Expected() => expected();

    /// <inheritdoc/>
    public void StartAgain() => startAgain();
}
