using System.Text.Json;
using System.Text.Json.Serialization;
using FreeWilly.Core.Preflight;
using FreeWilly.Core.Preflight.Windows;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// The standalone form of the check. The install runs the same <see cref="PreflightInspection"/>;
/// this is what a user runs when a working setup stopped working and the question is what changed.
/// </summary>
internal static class PreflightCommand
{
    /// <summary>Exit code when every blocking row is green.</summary>
    private const int Ready = 0;

    /// <summary>Exit code when something blocks an install — the whole point of an exit code here.</summary>
    private const int Blocked = 1;

    /// <summary>Exit code for an argument this program does not have.</summary>
    private const int Usage = 2;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Run the preflight verb.</summary>
    /// <param name="args">Everything after <c>--preflight</c>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Run(string[] args)
    {
        var json = false;
        foreach (var argument in args)
        {
            switch (argument)
            {
                case "--json":
                    json = true;
                    break;
                case "-h" or "--help":
                    Console.Out.Write(HelpText);
                    return Ready;
                default:
                    Console.Error.WriteLine(
                        $"{CommandLine.ExecutableName} {CommandLine.PreflightVerb}: "
                        + $"unknown argument {argument}");
                    Console.Error.Write(HelpText);
                    return Usage;
            }
        }

        var report = PreflightInspection.Run(new WindowsMachineFacts());

        Console.Out.Write(json
            ? JsonSerializer.Serialize(report, Json) + Environment.NewLine
            : ReportText.Render(report));

        return report.CanHostEngine ? Ready : Blocked;
    }

    private static string HelpText =>
        $"""
        {CommandLine.ExecutableName} {CommandLine.PreflightVerb} reads what this machine can host,
        and changes nothing.

          --json    the same report as JSON, for an installer rather than a person
          --help    this

        Exit code 0 means every blocking row is green; 1 means at least one is not.

        """;
}
