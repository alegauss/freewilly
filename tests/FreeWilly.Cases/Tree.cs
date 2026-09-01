using Winwright.Projects;

namespace FreeWilly.Cases;

/// <summary>
/// Where this checkout is, found by walking up to the file the project declares itself in.
/// <para>
/// WW87. Written once rather than in each case: the two scripts this replaces each computed it
/// their own way — <c>Split-Path -Parent $PSScriptRoot</c> — and each was therefore a file that
/// stopped working if it moved. This one is addressed by what it is looking for.
/// </para>
/// </summary>
internal static class Tree
{
    /// <summary>The repository root.</summary>
    /// <exception cref="InvalidOperationException">
    /// Where nothing above the test binary declares a project, which means the runner is somewhere
    /// this repository is not — a fact worth a sentence rather than a null to dereference later.
    /// </exception>
    public static string Root()
    {
        var walking = new DirectoryInfo(AppContext.BaseDirectory);
        while (walking is not null && !File.Exists(Path.Combine(walking.FullName, ProjectDeclaration.FileName)))
            walking = walking.Parent;

        return walking?.FullName
            ?? throw new InvalidOperationException(
                $"no {ProjectDeclaration.FileName} above {AppContext.BaseDirectory}, so this is not a checkout of this project");
    }
}
