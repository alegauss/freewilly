using FreeWilly.Core.Engine;

namespace FreeWilly.Core.Fixtures;

/// <summary>
/// A machine to photograph the Engine page's readings against (DD197).
/// </summary>
/// <remarks>
/// <para>Healthy, deliberately. The panel's ordinary job is telling somebody nothing is wrong, and a
/// fixture showing a broken disk would put a picture of a failing machine in a README as though it
/// were what this product looks like. The faulted reading is worth a capture of its own one day and
/// is not what the page is opened for.</para>
///
/// <para><b>Every value is fixed and none is derived from this machine</b>, which is what makes a
/// capture the same picture on every run and the same picture everywhere (DD38). The sizes are the
/// pair DD197 exists for — a virtual disk on the Windows volume beside the space used inside the
/// distribution — and they differ from each other for the reason real ones do: a sparse file that
/// has grown holds more than the filesystem inside it is using.</para>
/// </remarks>
public sealed class SampleMachineReport : IMachineReport
{
    /// <summary>The readings and the verdict they add up to, which never change.</summary>
    private static readonly MachineHealth Health = new(
        true,
        "wsl, the distribution and the engine are well",
        [
        new MachineGroup("WSL",
        [
            new MachineReading("version", "2.7.1.0"),
            new MachineReading("kernel", "6.6.87.2"),
            new MachineReading("distribution", EnginePaths.CurrentDistribution),
            new MachineReading("registered", "yes"),
            new MachineReading("running", "yes"),
        ]),
        new MachineGroup("Filesystem",
        [
            new MachineReading("root device", "/dev/sdd"),
            new MachineReading("mount options", "rw,relatime,discard,errors=remount-ro,data=ordered"),
            new MachineReading("writable", "yes"),
        ]),
        new MachineGroup("Errors",
        [
            new MachineReading("recorded", "0"),
            new MachineReading("first in", "none"),
            new MachineReading("last in", "none"),
        ]),
        new MachineGroup("Disk",
        [
            new MachineReading("virtual disk", "58.3 GB"),
            new MachineReading("used inside", "56.5 GB"),
            new MachineReading("free on Windows", "214.7 GB"),
        ]),
        new MachineGroup("Engine",
        [
            new MachineReading("pipe", @"answers on \\.\pipe\docker_engine"),
            new MachineReading("API version", "1.55"),
        ]),
        ]);

    /// <inheritdoc/>
    /// <remarks>
    /// Completed rather than scheduled, so a caller that awaits this continues without yielding and
    /// the panel is drawn before the capture settles. A fixture that went through the thread pool
    /// would make the picture depend on how busy the machine drawing it was.
    /// </remarks>
    public Task<MachineHealth> ReadAsync(CancellationToken cancellation = default) =>
        Task.FromResult(Health);
}
