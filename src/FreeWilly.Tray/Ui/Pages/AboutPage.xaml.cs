using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Core.Licensing;
using FreeWilly.Core.Releases;

namespace FreeWilly.Tray.Ui.Pages;

/// <summary>One thing the page names, and what it says.</summary>
/// <param name="Name">The component.</param>
/// <param name="Value">Its version, or what is known instead.</param>
internal sealed record Component(string Name, string Value);

/// <summary>
/// What this build is, and what it put on the machine (DD83).
/// </summary>
/// <remarks>
/// A version is the first thing a bug report asks for and the only way to tell a stale install from
/// a fresh one, and until this the only answer was <c>--version</c> and <c>--api</c> — console verbs,
/// answered where a window user never looks.
///
/// <para><b>Every value is read, never typed.</b> The build comes from <see cref="BuildVersion"/>,
/// the pinned artefacts from the manifest the provisioner downloads from, and the engine's own
/// version, API level and architecture from the daemon. A number typed here would be the defect the
/// whole of DD43's law is about, one surface away.</para>
///
/// <para><b>It draws with no daemon (L6).</b> The engine rows say so rather than blocking or
/// rendering blank, so the state a reviewer most needs to see — a machine with nothing installed —
/// is the one that needs no machine to reach.</para>
/// </remarks>
internal sealed partial class AboutPage : System.Windows.Controls.UserControl
{
    private readonly IEngineClient _api;

    /// <summary>Construct the page.</summary>
    /// <param name="api">The engine, asked once when this is opened.</param>
    internal AboutPage(IEngineClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        InitializeComponent();

        // The mark, at the largest frame the icon carries. ApplicationIcon puts this file in the
        // executable's Win32 resources, which a decoder cannot open — the csproj adds it a second
        // time as a WPF resource for exactly this.
        var frames = new IconBitmapDecoder(
            new Uri("pack://application:,,,/FreeWilly.ico"),
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad).Frames;
        Mark.Source = frames.OrderByDescending(frame => frame.PixelWidth).First();

        BuildLine.Text = $"Build {BuildVersion.Current}";
        Reaches.Text = WhatItReaches();
        Terms.Text = Attribution.Scope;
        Copyright.Text =
            $"{Attribution.Licence} · Copyright {Attribution.Holder}. "
            + "See LICENSE for the full terms and NOTICE for every upstream component.";

        // Drawn before anything is asked, and with the engine rows already present saying they have
        // no answer yet. They used to be omitted until the refresh returned, which gave the page two
        // different empty states — and the capture reached the wrong one, because the client's
        // connect timeout outlasts the settle. One state, and it says what is true.
        Show(engine: null);
    }

    /// <summary>Ask the engine what it is, and say so.</summary>
    /// <returns>A task that completes when the page has been redrawn.</returns>
    internal async Task RefreshAboutAsync()
    {
        EngineVersion? engine = null;
        try
        {
            engine = await _api.VersionAsync().ConfigureAwait(true);
        }
        catch (DockerApiException)
        {
            // Not a failure of this page. A machine with no engine yet is the ordinary state before
            // the first provision, and the rows say what is true rather than nothing at all.
        }

        Show(engine);
        BandWater.Running(engine is not null);
    }

    private void Show(EngineVersion? engine)
    {
        var manifest = EngineManifest.Current;
        var rows = new List<Component>
        {
            new("FreeWilly", BuildVersion.Current),

            // Present whether or not anything answered (L6): a row that says "not answering" is a
            // fact, and one that is absent looks like a page that failed to finish drawing.
            new(
                "Engine",
                engine is null ? "not answering" : $"{engine.Version} ({engine.Os}/{engine.Arch})"),
            new("Engine API", ApiLine(engine)),
        };

        // What the install pins, whether or not it has run. These are facts about the build rather
        // than about the machine, so they are here even before the first provision.
        foreach (var artefact in manifest.Artefacts)
        {
            rows.Add(new Component(Named(artefact.Id), artefact.Version));
        }

        rows.Add(new Component("Distribution", EnginePaths.CurrentDistribution));
        Components.ItemsSource = rows;
    }

    /// <summary>What the client asks for, and the range the daemon answers (DD176).</summary>
    /// <param name="engine">What the daemon reported, or null where nothing answered.</param>
    /// <returns>The Engine API row's value.</returns>
    /// <remarks>
    /// This row used to set the daemon's newest API version against the pinned one with a middot
    /// between them and nothing saying how they relate — <c>1.55 · this client asks for v1.43</c> —
    /// and it was read as a mismatch by the first person to see it. Two numbers a reader has to
    /// compare is the defect; the fact that settles the comparison is a third number the daemon
    /// already sends and this page already receives.
    ///
    /// <para><b>The range, not a second number.</b> <c>MinAPIVersion</c> is the oldest the daemon
    /// still answers, so stating the span it serves makes the pin's place in it self-evident. The
    /// console probe has printed all three all along; the window was the surface that dropped one.
    /// A daemon old enough to omit the floor gets a ceiling and no invented lower bound.</para>
    ///
    /// <para><b>One spelling.</b> The <c>v</c> prefix was on one side and not the other because the
    /// pin is a URL path segment and the daemon's is a JSON field. That is a fact about two call
    /// sites, not about the versions, so it does not reach the row.</para>
    /// </remarks>
    private static string ApiLine(EngineVersion? engine)
    {
        var asked = $"this client asks for {Versioned(DockerApi.ApiVersion)}";
        if (engine is null || engine.ApiVersion.Length == 0)
        {
            return asked;
        }

        var newest = Versioned(engine.ApiVersion);
        var speaks = engine.MinApiVersion.Length == 0
            ? $"the engine speaks up to {newest}"
            : $"the engine speaks {Versioned(engine.MinApiVersion)}–{newest}";

        return $"{asked} · {speaks}";
    }

    /// <summary>One spelling for a version, whichever side of the call it came from.</summary>
    /// <param name="version">The version, with or without its prefix.</param>
    /// <returns>The version, prefixed.</returns>
    private static string Versioned(string version) =>
        version.StartsWith('v') ? version : $"v{version}";

    /// <summary>Every host this build reaches, and what it asks each for (DD154).</summary>
    /// <remarks>
    /// <b>Counted, not typed.</b> "The five pinned artefacts" was a number in prose until DD157 found
    /// what that costs, so the count comes off the manifest whose rows are directly above this line —
    /// a sixth artefact changes both together or neither.
    ///
    /// <para><b>And the release check is stated plainly now that it is not a switch</b> (DD171). It
    /// used to be named as a menu tick a reader could go and find; it happens on every launch, so what
    /// this page owes them is the host, the frequency and what the request carries — the three facts
    /// somebody deciding whether they mind would actually ask for.</para>
    /// </remarks>
    private static string WhatItReaches()
    {
        var artefacts = EngineManifest.Current.Artefacts.Count();
        return $"Nothing is uploaded, there is no account and nothing is measured. This build "
            + $"downloads {artefacts} pinned artefacts, by digest, during a provision you asked for. "
            + $"It also asks {ReleaseCheck.Host} four times a day for the latest release tag, so it "
            + "can tell you a version exists. That request carries this product's name and version "
            + "and nothing about you.";
    }

    /// <summary>The manifest's own id, spelled the way a reader would say it.</summary>
    private static string Named(string id) => id switch
    {
        "rootfs" => "Alpine rootfs",
        "engine" => "Moby (pinned)",
        "cli" => "Docker CLI",
        "compose" => "Compose plugin",
        "buildx" => "Buildx plugin",
        _ => id,
    };
}
