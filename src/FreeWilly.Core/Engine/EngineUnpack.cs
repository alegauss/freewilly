namespace FreeWilly.Core.Engine;

/// <summary>
/// The window in which the engine's binaries are being rewritten, made visible across processes
/// (DD269).
/// </summary>
/// <remarks>
/// Measured on 31 August 2026: a start launched the daemon while <c>--provision</c> was four seconds
/// into unpacking 85 MB into <c>/usr/local/bin</c>. dockerd had not been reached yet, so the previous
/// version's copy exec'd perfectly and then forked a <c>containerd</c> that tar still held open. The
/// journal read "text file busy" and the engine stayed down until somebody asked again.
///
/// <para>Retrying is the other half of that (DD265, DD267) and it stays, because a start cannot know
/// what else on the machine holds a file. But the provisioner is the one writer this product owns,
/// and a writer that can be asked about does not need to be guessed at: three pauses of 700ms cannot
/// outlast an unpack, and no retry budget that could would be one worth spending on the failures that
/// are not this.</para>
///
/// <para><b>A file and not a mutex.</b> A named mutex would do the signalling and nothing else, and
/// what is wanted here is the thing a stale one gets wrong: a provision killed mid-unpack must leave
/// behind something that answers "nobody is writing", because by then nobody is. An abandoned mutex
/// is granted with an exception that every caller has to remember to treat as success. A file whose
/// holder is gone simply opens.</para>
/// </remarks>
public static class EngineUnpack
{
    /// <summary>Hold the lock for as long as the returned handle lives (DD269).</summary>
    /// <param name="path">Where the lock file lives.</param>
    /// <returns>The handle, or <see langword="null"/> where the lock could not be taken.</returns>
    /// <remarks>
    /// Null rather than a throw, and the caller provisions anyway. This exists to stop a start from
    /// racing an unpack, and an unpack that cannot announce itself is still the work somebody asked
    /// for: refusing it would turn a missing courtesy into a failed install.
    ///
    /// <para><see cref="FileShare.None"/> is the whole mechanism. It is what makes
    /// <see cref="InFlight"/>'s read fail, and it is asked for on a file opened
    /// <see cref="FileAccess.Write"/> because a share mode is only ever about the handle carrying
    /// it.</para>
    /// </remarks>
    public static IDisposable? Hold(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether a provision is rewriting the binaries right now (DD269).</summary>
    /// <param name="path">Where the lock file lives.</param>
    /// <returns>Whether something holds it.</returns>
    /// <remarks>
    /// Three answers collapse to false, and each is the same fact: no file, a file nothing holds, and
    /// a path this account cannot reach all mean there is no unpack to wait for. The last one is the
    /// only debatable case, and it resolves the way every other reading in this project does under
    /// load or refusal, which is towards letting the start proceed rather than manufacturing a
    /// blocker out of a question that did not get an answer (DD134).
    /// </remarks>
    public static bool InFlight(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>How often a waiting start asks again (DD269).</summary>
    /// <remarks>
    /// The question is a file open, so it costs nothing to ask and the interval is about how long a
    /// start is willing to sit still after the unpack has finished. A quarter second is under the
    /// noise of the boot it is about to do.
    /// </remarks>
    private static readonly TimeSpan BetweenAsking = TimeSpan.FromMilliseconds(250);

    /// <summary>Wait until no provision is unpacking, or until the budget runs out (DD269).</summary>
    /// <param name="path">Where the lock file lives.</param>
    /// <param name="budget">How long to wait.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>How long was spent waiting, which is zero on the common path.</returns>
    /// <remarks>
    /// Returning the duration rather than a flag, because the caller's job is to say what happened
    /// and a start that waited is a fact about the machine worth a clause in the journal. Zero is the
    /// answer on every start that did not collide with an install, which is nearly all of them.
    ///
    /// <para>Bounded, and running out is not an error. The lock is a courtesy between two processes
    /// of this product and not a correctness barrier: a provision wedged forever must not turn every
    /// subsequent start into a hang, and past the budget the retry the start already carries is what
    /// the failure falls back to.</para>
    /// </remarks>
    public static async Task<TimeSpan> WaitForIdleAsync(
        string path, TimeSpan budget, CancellationToken cancellation = default)
    {
        if (!InFlight(path))
        {
            return TimeSpan.Zero;
        }

        var began = DateTimeOffset.UtcNow;
        var deadline = began + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(BetweenAsking, cancellation).ConfigureAwait(false);
            if (!InFlight(path))
            {
                break;
            }
        }

        return DateTimeOffset.UtcNow - began;
    }
}
