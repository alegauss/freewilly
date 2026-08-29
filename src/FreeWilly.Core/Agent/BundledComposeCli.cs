using System.Diagnostics;
using System.Text;
using FreeWilly.Core.Engine;

namespace FreeWilly.Core.Agent;

/// <summary>
/// The <c>docker</c> this install placed, run in the caller's own directory (DD63).
/// </summary>
/// <remarks>
/// <see cref="EnginePaths.DockerCli"/> and not whatever <c>docker</c> is on <c>PATH</c>. The
/// difference is the whole of DD20: a machine can carry a rival's CLI and a context pointing at its
/// engine, and composing through that would bring the project up somewhere else entirely — on an
/// engine this tool does not manage, with a stamp whose reclaim would then find nothing. The
/// executable beside our own install is the one that talks to our own pipe.
///
/// <para>The working directory is the caller's, because compose resolves a relative build context, a
/// bind mount and an <c>env_file</c> against the project — and the project is where the user ran
/// this, not where this executable happens to live.</para>
///
/// <para>Not <c>ConsoleTool</c>: that one is a preflight probe with a fifteen-second deadline, and
/// pulling an image is minutes. The deadline here is generous and still finite, so a build waiting
/// on a prompt nobody can answer ends as a refusal rather than as an agent that never returns.</para>
/// </remarks>
public sealed class BundledComposeCli : IComposeCli
{
    /// <summary>How long an up may take before this gives up on it.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

    /// <summary>The variable that tells the CLI which config directory to read.</summary>
    /// <remarks>
    /// Set on the child regardless (DD73). It is what makes <c>compose</c> a subcommand at all: the
    /// plugin this install placed sits under its own config directory, and the CLI finds a plugin in
    /// <c>$DOCKER_CONFIG/cli-plugins</c> and in no other place this project may write.
    ///
    /// <para>DD124 made <see cref="DockerConfigEntry"/> set the same variable in the user's own
    /// environment, so a shell has <c>docker compose</c> too. This still assigns it on the child, and
    /// deliberately: that one is a user-owned value they may point anywhere, and this call has to
    /// read the directory holding the plugin <i>this</i> install placed. One spelling of the name,
    /// though — the constant is <see cref="DockerConfigEntry.Variable"/>, so the two writers cannot
    /// drift onto different variables.</para>
    /// </remarks>
    public const string ConfigVariable = DockerConfigEntry.Variable;

    private readonly string _docker;
    private readonly string _config;

    /// <summary>Construct against this install's own CLI.</summary>
    public BundledComposeCli()
        : this(new EnginePaths())
    {
    }

    /// <summary>Construct against a layout, which is what says where both halves are.</summary>
    /// <param name="paths">The install this runs out of.</param>
    public BundledComposeCli(EnginePaths paths)
        : this(
            (paths ?? throw new ArgumentNullException(nameof(paths))).DockerCli,
            paths.ConfigDirectory)
    {
    }

    /// <summary>Construct against an explicit executable and config directory.</summary>
    /// <param name="dockerCli">The <c>docker.exe</c> to run.</param>
    /// <param name="configDirectory">What <c>DOCKER_CONFIG</c> is set to for the child.</param>
    public BundledComposeCli(string dockerCli, string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerCli);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _docker = dockerCli;
        _config = configDirectory;
    }

    /// <inheritdoc/>
    public ComposeResult Run(string workingDirectory, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!File.Exists(_docker))
        {
            // Named rather than thrown: an install that never provisioned has no CLI, and that is a
            // sentence the caller can act on rather than a stack trace.
            return new ComposeResult(null, "", $"{_docker} is not there: run the install first");
        }

        var startInfo = new ProcessStartInfo(_docker)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // On the child alone, and assigned rather than appended: whatever the caller's own
        // DOCKER_CONFIG says, this call has to read the config directory holding the plugin this
        // install placed, or `compose` is not a subcommand of the docker.exe being run (DD73).
        startInfo.Environment[ConfigVariable] = _config;

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ComposeResult(null, "", $"{_docker} could not be started");
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)Deadline.TotalMilliseconds))
            {
                Kill(process);
                return new ComposeResult(
                    null, "", $"docker did not finish within {Deadline.TotalMinutes:0} minutes");
            }

            Task.WaitAll(stdout, stderr);

            // Compose writes its progress to stderr and its answers to stdout, so both are the
            // output: a failure whose reason was only on stderr would come back as an exit code and
            // no words at all.
            var text = new StringBuilder(stdout.Result).Append(stderr.Result).ToString();
            return new ComposeResult(process.ExitCode, text, null);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new ComposeResult(null, "", $"{_docker}: {exception.Message}");
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It exited between the wait and the kill.
        }
    }
}
