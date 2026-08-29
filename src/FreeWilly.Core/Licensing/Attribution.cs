using System.Text;
using FreeWilly.Core.Engine;

namespace FreeWilly.Core.Licensing;

/// <summary>One upstream thing this project puts on a user's machine.</summary>
/// <param name="ArtefactId">Which manifest entry it is, so the two cannot drift apart.</param>
/// <param name="Name">What it is called upstream.</param>
/// <param name="Version">The pinned version.</param>
/// <param name="Licence">Its terms, as an SPDX expression where one covers it.</param>
/// <param name="Origin">Where it comes from.</param>
/// <param name="Note">What the licence line does not say on its own.</param>
public sealed record BundledComponent(
    string ArtefactId,
    string Name,
    string Version,
    string Licence,
    string Origin,
    string Note);

/// <summary>
/// What this project is licensed under, and what it installs that is somebody else's.
/// </summary>
/// <remarks>
/// The licence is the feature: Docker Desktop is free only below a headcount and revenue
/// threshold, and the reason anyone tries an alternative is that their employer crossed it. So the
/// terms are stated where the choice is made rather than only in a file nobody opens.
///
/// The attribution half is the one that gets forgotten, and it is a compliance requirement rather
/// than a courtesy. It is generated from the same manifest the provisioner downloads from, so a
/// new artefact cannot be added without an attribution entry — a test asserts exactly that.
/// </remarks>
public static class Attribution
{
    /// <summary>This project's own terms.</summary>
    public const string Licence = "Apache-2.0";

    /// <summary>Who holds the copyright.</summary>
    public const string Holder = "FreeWilly contributors";

    /// <summary>
    /// The sentence that stops the claim being overstated.
    /// </summary>
    /// <remarks>
    /// This tool is Apache-2.0. The engine it installs is not this project's work and carries its
    /// own terms, and saying otherwise would be the same category of surprise the project exists to
    /// remove.
    /// </remarks>
    public const string Scope =
        "FreeWilly itself is Apache-2.0 and free at any headcount. "
        + "The engine it installs is upstream software under its own terms, listed below. "
        + "Those files are not redistributed here: they are downloaded from their official "
        + "locations at install time, against the versions and digests this build pins.";

    /// <summary>
    /// Every upstream component, keyed to the manifest entry that downloads it.
    /// </summary>
    /// <param name="manifest">The pinned artefacts.</param>
    /// <returns>The attribution list, in the order the artefacts are acquired.</returns>
    public static IReadOnlyList<BundledComponent> Components(EngineManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return
        [
            new BundledComponent(
                manifest.Rootfs.Id,
                "Alpine Linux minirootfs",
                manifest.Rootfs.Version,

                // Deliberately not a single SPDX id. A minirootfs is a collection, and naming one
                // licence for it would be a claim this project cannot support.
                "multiple; see the packages inside it",
                manifest.Rootfs.Url,
                "A base filesystem made of separately licensed packages: musl (MIT), BusyBox and "
                + "apk-tools (GPL-2.0-only) and others. Each package records its own licence "
                + "under /usr/share/licenses inside the installed distribution."),

            new BundledComponent(
                manifest.Engine.Id,
                "Docker Engine (Moby) static binaries for Linux",
                manifest.Engine.Version,
                "Apache-2.0",
                manifest.Engine.Url,
                "dockerd, containerd, runc, ctr and docker-proxy are Apache-2.0. The archive also "
                + "carries docker-init, which is tini, under MIT."),

            new BundledComponent(
                manifest.Cli.Id,
                "Docker CLI for Windows",
                manifest.Cli.Version,
                "Apache-2.0",
                manifest.Cli.Url,
                "The docker command this project installs and puts on PATH."),

            new BundledComponent(
                manifest.Compose.Id,
                "Docker Compose CLI plugin for Windows",
                manifest.Compose.Version,
                "Apache-2.0",
                manifest.Compose.Url,
                "A separate upstream release with its own version, because the Windows CLI archive "
                + "does not carry it. Installed as a CLI plugin under this install's own config "
                + "directory rather than the user's, which is what makes docker compose a command "
                + "on a machine that never had Docker Desktop."),

            new BundledComponent(
                manifest.Buildx.Id,
                "Docker Buildx CLI plugin for Windows",
                manifest.Buildx.Version,
                "Apache-2.0",
                manifest.Buildx.Url,
                "The BuildKit builder, placed beside Compose. Without it the CLI falls back to the "
                + "legacy builder, which cannot parse a cache mount, a heredoc or a secret mount, "
                + "so a Dockerfile that builds anywhere else fails here."),
        ];
    }

    /// <summary>
    /// The NOTICE file, rendered.
    /// </summary>
    /// <remarks>
    /// Generated rather than hand-written so that adding a download without its attribution is a
    /// failing test rather than a thing nobody notices until somebody's legal review does.
    /// </remarks>
    /// <param name="manifest">The pinned artefacts.</param>
    /// <returns>The whole file, including its trailing newline.</returns>
    public static string Notice(EngineManifest manifest)
    {
        var text = new StringBuilder()
            .Append("FreeWilly\n")
            .Append("Copyright ").Append(Holder).Append('\n')
            .Append('\n')
            .Append("This product includes software developed by the FreeWilly contributors,\n")
            .Append("licensed under the Apache License, Version 2.0 (see LICENSE).\n")
            .Append('\n')
            .Append("THIRD-PARTY COMPONENTS\n")
            .Append('\n')
            .Append(Wrap(Scope)).Append('\n');

        foreach (var component in Components(manifest))
        {
            text.Append('\n')
                .Append(component.Name).Append('\n')
                .Append("  version  ").Append(component.Version).Append('\n')
                .Append("  licence  ").Append(component.Licence).Append('\n')
                .Append("  source   ").Append(component.Origin).Append('\n')
                .Append(Wrap(component.Note, "  "));
        }

        text.Append('\n')
            .Append("This file is generated from the same manifest the installer downloads from.\n")
            .Append("Adding an artefact without an attribution entry fails a test.\n");

        return text.ToString();
    }

    /// <summary>
    /// What the window's About says.
    /// </summary>
    /// <remarks>
    /// The same facts as NOTICE, short enough to read in a dialog: a visitor who cannot establish
    /// the terms in the first ten seconds assumes the same trap they came here to escape.
    /// </remarks>
    /// <param name="manifest">The pinned artefacts.</param>
    /// <param name="version">This build's version.</param>
    /// <returns>The dialog's body.</returns>
    public static string About(EngineManifest manifest, string version)
    {
        var text = new StringBuilder()
            .Append("FreeWilly ").Append(version).Append('\n')
            .Append(Licence).Append(" · free at any headcount, with no licence to buy.\n\n")
            .Append("Copyright ").Append(Holder).Append(". ")
            .Append("The full terms are in LICENSE; the patent grant is why Apache-2.0 rather ")
            .Append("than MIT.\n\nIt installs, and does not redistribute:\n");

        foreach (var component in Components(manifest))
        {
            text.Append("  · ").Append(component.Name)
                .Append(' ').Append(component.Version)
                .Append(", ").Append(component.Licence).Append('\n');
        }

        text.Append("\nSee NOTICE for the full attribution.");
        return text.ToString();
    }

    /// <summary>Fill to a column, because a NOTICE is read in a terminal as often as a browser.</summary>
    private static string Wrap(string prose, string indent = "", int column = 88)
    {
        var text = new StringBuilder();
        var line = new StringBuilder(indent);
        foreach (var word in prose.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > indent.Length && line.Length + 1 + word.Length > column)
            {
                text.Append(line).Append('\n');
                line.Clear().Append(indent);
            }

            if (line.Length > indent.Length)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        return text.Append(line).Append('\n').ToString();
    }
}
