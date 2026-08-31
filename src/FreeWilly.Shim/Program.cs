using System.Diagnostics;
using System.IO.Pipes;

namespace FreeWilly.Shim;

/// <summary>
/// The <c>docker</c> on PATH: the real CLI, plus the one sentence its failure could not know (DD141).
/// </summary>
/// <remarks>
/// An agent driving this install meets a stopped engine as a raw connection failure — "failed to
/// connect to the docker API at npipe:////./pipe/docker_engine … check if the daemon is running".
/// That message is docker's own, written for a world where the daemon could be anyone's. Here it is
/// not: FreeWilly ships this command and owns the engine behind it, so the one thing the reader
/// needs is known at the point the error is printed and was being left out of it.
///
/// <para><b>It starts nothing.</b> An agent told what to run will run it, and starting a daemon as a
/// side effect of an unrelated command is a bigger decision than a missing sentence warrants.</para>
/// </remarks>
internal static class Program
{
    /// <summary>The pipe this install's engine serves.</summary>
    /// <remarks>
    /// Spelled here rather than read from the product, because this binary deliberately references
    /// nothing: it has to start in milliseconds on every docker command anybody runs. A packaging
    /// test holds this equal to <c>DockerApi.DefaultPipeName</c>, which is the same shape the
    /// installer's own restatements of <c>DistroName</c> and the task list are held to.
    /// </remarks>
    private const string EnginePipe = "docker_engine";

    /// <summary>The verb that fixes it.</summary>
    private const string TheVerb = "freewilly do engine start";

    /// <summary>How long to wait for the pipe before calling it unanswered.</summary>
    /// <remarks>
    /// Short on purpose. This runs only after a docker command has already failed, so the user is
    /// waiting on a diagnosis rather than on work, and a slow answer to "is the engine up" would be
    /// this shim adding a delay to every failure it did not cause.
    /// </remarks>
    private const int PipeWaitMs = 300;

    private static int Main(string[] args)
    {
        var cli = RealCli();
        if (cli is null)
        {
            Console.Error.WriteLine(
                "docker: FreeWilly's copy of the Docker CLI is not on this machine. "
                + "Run: freewilly --provision");
            return 127;
        }

        var start = new ProcessStartInfo(cli)
        {
            // No redirection anywhere, and that is the whole design of this forwarder. The child
            // inherits this process's standard handles, so `docker run -it` keeps its terminal,
            // `docker build` keeps its progress rendering, colour survives, and a pipeline behaves
            // exactly as it did before this file existed. Reading docker's stderr to match on its
            // wording would cost all of that, which is why the check below asks the machine instead.
            //
            // The working directory is inherited for the same reason, and it is the one place in
            // this product where that is deliberate (DD261). `docker build .`, `-f compose.yaml`
            // and `-v .\data:/data` all resolve against the directory the user typed in, so naming
            // one here would change what those arguments mean. Everything else this product starts
            // names its own, because a child holds a lock on whichever directory it is given.
            UseShellExecute = false,
        };

        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        int code;
        using (var child = Process.Start(start))
        {
            if (child is null)
            {
                Console.Error.WriteLine($"docker: could not start {cli}");
                return 127;
            }

            child.WaitForExit();
            code = child.ExitCode;
        }

        // The discriminator is the machine, not the message. A `docker build` that fails on a bad
        // Dockerfile exits non-zero with the engine perfectly up, and gets nothing added; a command
        // that failed while nothing is serving the pipe gets the sentence. No parsing, no locale to
        // get wrong, and no wording of docker's to track across versions.
        if (code != 0 && !EngineIsAnswering())
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"The FreeWilly engine is not running. Start it with:");
            Console.Error.WriteLine($"    {TheVerb}");
        }

        return code;
    }

    /// <summary>The Docker CLI this install placed, found relative to this file.</summary>
    /// <remarks>
    /// Relative and never through PATH: resolving `docker` by name from inside the thing that IS
    /// `docker` on PATH is a forwarder that calls itself. The layout is fixed by the installer —
    /// this sits in the directory on PATH, and the vendor CLI sits beside it in a directory that is
    /// deliberately not.
    /// </remarks>
    private static string? RealCli()
    {
        var here = Path.GetDirectoryName(Environment.ProcessPath);
        if (here is null)
        {
            return null;
        }

        var cli = Path.GetFullPath(Path.Combine(here, "..", "cli", "docker.exe"));
        return File.Exists(cli) ? cli : null;
    }

    /// <summary>Whether anything is serving the engine's pipe.</summary>
    private static bool EngineIsAnswering()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", EnginePipe, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(PipeWaitMs);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException
            or IOException or UnauthorizedAccessException)
        {
            // Unanswered, which is the whole question. Anything else that could go wrong here is
            // this shim's problem and not the user's, so it is not allowed to change the exit code
            // of the command they actually ran.
            return false;
        }
    }
}
