using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Every process this product starts names its own working directory (DD261).
/// </summary>
/// <remarks>
/// A child holds a lock on its working directory for as long as it lives, and this product starts
/// children that live a long time: the engine host, the daemon's launcher, a VM hold, a terminal
/// somebody is about to work in. Given no directory of its own, each one holds whichever directory
/// its caller happened to be in — which for the engine host is the install's own, and an update or
/// an uninstall is exactly the thing that then cannot write there. The failure reads as "the
/// installer could not replace a file" and says nothing about a child process at all.
///
/// <para>Found the way most of these are: a test pointed the process working directory at its own
/// scratch directory and then could not delete it, because a relay had opened a channel per
/// connection and every <c>wsl.exe</c> was holding it.</para>
///
/// <para><b>Asserted over the source rather than over behaviour, deliberately.</b> What has to hold
/// is a property of every starter that will ever be written, and the eleventh one is not going to
/// be added to a list of ten. Counting is the whole check: a file with a starter and no working
/// directory beside it fails, and the count is what makes a starter added tomorrow fail too. The
/// suite already reads its own source in this way for the same reason.</para>
/// </remarks>
public sealed class ChildProcessTests
{
    /// <summary>The one starter that inherits, and it is the reason this is not a blanket rule.</summary>
    private const string Forwarder = "src/FreeWilly.Shim/Program.cs";

    private const string Starter = "new ProcessStartInfo(";

    private const string Named = "WorkingDirectory =";

    [Fact]
    public void Every_process_this_product_starts_names_a_working_directory()
    {
        var root = RepositoryRoot();
        var unnamed = new List<string>();

        foreach (var file in Sources(root))
        {
            var text = File.ReadAllText(file);
            var starters = Occurrences(text, Starter);
            if (starters == 0)
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            // One starter in the forwarder is allowed to have no directory, and only one. A second
            // one added there would have to argue for itself the way the first does.
            var owed = relative == Forwarder ? starters - 1 : starters;
            var named = Occurrences(text, Named);

            if (named < owed)
            {
                unnamed.Add($"{relative}: {starters} starter(s) and {named} working director(ies)");
            }
        }

        Assert.True(
            unnamed.Count == 0,
            "a process is started with no working directory of its own, so it will hold whichever "
            + "directory its caller was in for as long as it lives:\n  "
            + string.Join("\n  ", unnamed)
            + "\nAdd `WorkingDirectory = Environment.SystemDirectory` unless the child genuinely "
            + "needs the caller's directory, and if it does, say so where "
            + Forwarder + " says so.");
    }

    [Fact]
    public void The_forwarder_inherits_on_purpose_and_says_which_arguments_depend_on_it()
    {
        // The exception, held as tightly as the rule. This one has to inherit: `docker build .` and
        // `-v .\data:/data` resolve against the directory the user typed in, so a directory named
        // here would change what somebody's own arguments mean. The risk is not that it is wrong —
        // it is that a later reader tidying up the count above sees the one file that breaks the
        // pattern and fixes it. So the file has to carry the argument, and this asserts it does.
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), Forwarder));

        Assert.Equal(1, Occurrences(text, Starter));
        Assert.DoesNotContain(Named, text, StringComparison.Ordinal);
        Assert.Contains("DD261", text, StringComparison.Ordinal);
        Assert.Contains("docker build .", text, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Sources(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal));

    private static int Occurrences(string text, string what)
    {
        var found = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal);
             at >= 0;
             at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("the repository root is not above this test");
    }
}
