namespace FreeWilly.Core.Engine;

/// <summary>
/// The named object one engine host per session holds while it is serving (DD133, DD231).
/// </summary>
/// <remarks>
/// <para><b>The name is here rather than where it is claimed, because the two places that care are
/// not the same assembly.</b> The host claims it to keep a second <c>--run</c> from starting
/// (DD133), and the preflight opens it to answer a different question: whether the engine holding
/// <c>docker_engine</c> is this tool's own. A literal at each end is a probe that quietly stops
/// recognising its own engine the day somebody renames the slot, which is the same argument
/// <see cref="FilesystemRepair.StopStep"/> makes about a step name.</para>
///
/// <para><b>What it is worth to the preflight.</b> That row identifies a rival by what it left on
/// the machine, and an open pipe was the one signal with no way of telling whose it is: DD56 removed
/// this project's own distribution from the distribution signal and DD16's fix skipped its own
/// <c>docker.exe</c>, while the pipe went on reporting a running FreeWilly as an unidentified engine
/// and telling the user to uninstall it. This is the missing exclusion, and it costs nothing: the
/// alternative is asking Windows which process serves the pipe, which means opening the pipe as a
/// client and taking an instance from the engine being asked about.</para>
/// </remarks>
public static class EngineHostSlot
{
    /// <summary>The name both halves agree on. Unprefixed, so it is this session's.</summary>
    /// <remarks>
    /// Session-local for the reason the claim is: the contended object is really the machine-wide
    /// pipe, and creating a global name needs a privilege a standard user does not have. The pipe's
    /// own single-account ACL already refuses the other user this would be protecting against, so
    /// what is left is two hosts under one login, which is the case that was observed.
    /// </remarks>
    public const string Name = "FreeWilly.engine";

    /// <summary>
    /// Whether an engine host of this tool's is alive on this session.
    /// </summary>
    /// <remarks>
    /// Opened rather than created, so asking never becomes claiming: a probe that created the object
    /// would be a second process holding the slot the host uses to stay the only one. Existence is
    /// the answer, because the host holds its handle for as long as it lives and releases it on the
    /// way out.
    /// </remarks>
    public static bool Held
    {
        get
        {
            try
            {
                using var held = System.Threading.Mutex.OpenExisting(Name);
                return true;
            }
            catch (Exception exception)
                when (exception is System.Threading.WaitHandleCannotBeOpenedException
                    or UnauthorizedAccessException)
            {
                // Not there, or another account's. Either way there is no host of ours here, which
                // is the answer rather than an error.
                return false;
            }
        }
    }
}
