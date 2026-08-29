using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Whether the product itself is holding one of the single-instance slots (DD202).
/// </summary>
/// <remarks>
/// Asked at discovery rather than inside the test, which is what turns fourteen failures into
/// fourteen skips. The condition is knowable before any assertion runs and it is not a defect in
/// anything: a machine with FreeWilly up holds the very objects <see cref="SingleTrayTests"/> and
/// <see cref="SingleEngineTests"/> claim, which is the machine most likely to be developing it.
/// </remarks>
internal static class ProductSlot
{
    /// <summary>Whether something on this session is already serving the tray.</summary>
    /// <returns><see langword="true"/> where the slot is taken.</returns>
    /// <remarks>
    /// The claim is released the moment it is taken, because the question is whether the slot
    /// <em>was</em> free: a probe that held on would be the thing the next one finds.
    /// </remarks>
    internal static bool TrayIsHeld()
    {
        if (!SingleTray.TryClaim(out var probe))
        {
            return true;
        }

        probe!.Dispose();
        return false;
    }

    /// <summary>Whether something on this session is already serving the engine.</summary>
    /// <returns><see langword="true"/> where the slot is taken.</returns>
    internal static bool EngineIsHeld()
    {
        if (!SingleEngine.TryClaim(out var probe))
        {
            return true;
        }

        probe!.Dispose();
        return false;
    }
}

/// <summary>
/// A fact that stands aside where a live tray holds the object it claims (DD202).
/// </summary>
/// <remarks>
/// <para>A skip and not a pass, and the distinction is the whole point. The slot being held is
/// exactly what these tests exist to notice, so a green run that asserted nothing would be worse
/// than the red one this replaces — what changes is only that "this did not run" stops being spelled
/// the same way as "this is broken".</para>
///
/// <para>Through <see cref="FactAttribute.Skip"/> because xUnit 2.9 has no <c>Assert.Skip</c>: the
/// property is virtual and read at discovery, which is the version's own door for a condition known
/// before the test body. The alternative was a package for one attribute.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class FactUnlessTheTrayIsRunningAttribute : FactAttribute
{
    /// <inheritdoc/>
    public override string? Skip
    {
        get => ProductSlot.TrayIsHeld()
            ? $"FreeWilly's tray is running on this session and holds {SingleTray.Name}, which is "
              + "the object this test claims. Quit it and re-run to assert against it."
            : base.Skip;

        set => base.Skip = value;
    }
}

/// <summary>That the skip is conditional, on whichever machine this is running on (DD202).</summary>
/// <remarks>
/// The half a skip cannot show about itself. A wrong condition here would either hide these tests
/// forever — the failure mode DD202 says is worse than the red run it replaced — or never fire, and
/// neither is visible in a result that reads "skipped". Written as the equivalence rather than as an
/// expected value, so it asserts something on a developer's machine, where the slots are taken, and
/// on CI, where they are not.
/// </remarks>
public sealed class ProductSlotTests
{
    [Fact]
    public void The_single_instance_tests_are_skipped_exactly_while_the_product_holds_their_slot()
    {
        Assert.Equal(
            ProductSlot.TrayIsHeld(),
            new FactUnlessTheTrayIsRunningAttribute().Skip is not null);

        Assert.Equal(
            ProductSlot.EngineIsHeld(),
            new FactUnlessTheEngineIsRunningAttribute().Skip is not null);
    }

    [Fact]
    public void A_skip_names_the_object_and_what_to_do_about_it()
    {
        // A reason is the whole difference between a skip somebody acts on and one they scroll past.
        // Asserted only where there is one, because on CI there is not — which is the point.
        foreach (var reason in new[]
                 {
                     new FactUnlessTheTrayIsRunningAttribute().Skip,
                     new FactUnlessTheEngineIsRunningAttribute().Skip,
                 })
        {
            if (reason is null)
            {
                continue;
            }

            Assert.Contains("FreeWilly", reason, StringComparison.Ordinal);
            Assert.Contains("re-run", reason, StringComparison.Ordinal);
        }
    }
}

/// <summary>A fact that stands aside where a live engine host holds the slot (DD202).</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class FactUnlessTheEngineIsRunningAttribute : FactAttribute
{
    /// <inheritdoc/>
    public override string? Skip
    {
        get => ProductSlot.EngineIsHeld()
            ? $"FreeWilly is serving the engine on this session and holds {SingleEngine.Name}, which "
              + "is the object this test claims. Run `freewilly --stop` and re-run to assert "
              + "against it."
            : base.Skip;

        set => base.Skip = value;
    }
}
