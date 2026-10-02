using Legacy.Maliev.CatalogService.Api;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

[Collection("Read-only catalog process environment")]
public sealed class ReadOnlyCatalogStartupTests(ReadOnlyCatalogStartupFixture fixture)
    : IClassFixture<ReadOnlyCatalogStartupFixture>, IAsyncLifetime
{
    private const string FailureCode = "InstantQuotationCatalogReconciliationRequired";
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_readonly_optin_overrides_mutation_config_and_reads_all_six_tables_without_side_effects(bool? enabled)
    {
        // Catches the current default-off early return and the enabled reconciliation/cache branch.
        using var environment = new CatalogEnvironmentScope("true");
        string before = await fixture.SnapshotAsync();
        await using var caller = fixture.Context();
        var pla = await caller.Materials.SingleAsync(row => row.Name == "PLA");
        pla.Comment = "Unsaved fixture change";
        caller.Colors.Add(new Color { Name = "Unsaved fixture color" });
        var states = caller.ChangeTracker.Entries().Select(row => (row.Entity, row.State)).ToArray();
        var probe = new CatalogReadOnlyProbe();
        using var host = fixture.Host(enabled, probe);
        var error = Record.Exception(() => _ = host.Services);
        Assert.Null(error);
        Assert.Equal(new[] { "Color", "Material", "MaterialGroup", "MaterialHasColor", "MaterialHasSurfaceFinish", "SurfaceFinish" },
            probe.ReadTables.Distinct().Order(StringComparer.Ordinal).ToArray());
        AssertNoSideEffects(probe);
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.Equal(states, caller.ChangeTracker.Entries().Select(row => (row.Entity, row.State)).ToArray());
        Assert.Equal("Unsaved fixture change", pla.Comment);
        await using var stored = fixture.Context();
        var persisted = await stored.Materials.AsNoTracking().SingleAsync(row => row.Name == "PLA");
        Assert.Equal(1350m, persisted.DensityKilogramPerCubicMeter);
        Assert.Equal(456m, persisted.PricePerKilogram);
        Assert.Equal(17, persisted.CurrencyId);
        Assert.True(persisted.Machinable);
        Assert.Equal(new DateTime(2020, 1, 2), persisted.CreatedDate);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("material")]
    [InlineData("color")]
    [InlineData("finish")]
    [InlineData("color-link")]
    [InlineData("finish-link")]
    [InlineData("printable")]
    [InlineData("material-group")]
    [InlineData("density")]
    public async Task Required_catalog_drift_refuses_actual_startup_with_fixed_code_instead_of_skipping_validation(string drift)
    {
        // Each literal fixture mutation catches omission of its required entity/field/link check.
        await using (var context = fixture.Context())
        {
            var pla = await context.Materials.SingleAsync(row => row.Name == "PLA");
            var white = await context.Colors.SingleAsync(row => row.Name == "White");
            var finish = await context.SurfaceFinishes.SingleAsync(row => row.Name == "As printed");
            switch (drift)
            {
                case "group": (await context.MaterialGroups.SingleAsync(row => row.Name == "Plastics")).Name = "Fixture absent group"; break;
                case "material": pla.Name = "Fixture absent material"; break;
                case "color": white.Name = "Fixture absent color"; break;
                case "finish": finish.Name = "Fixture absent finish"; break;
                case "color-link": context.MaterialHasColors.Remove(await context.MaterialHasColors.SingleAsync(row => row.MaterialId == pla.Id && row.ColorId == white.Id)); break;
                case "finish-link": context.MaterialHasSurfaceFinishes.Remove(await context.MaterialHasSurfaceFinishes.SingleAsync(row => row.MaterialId == pla.Id && row.SurfaceFinishId == finish.Id)); break;
                case "printable": pla.Printable = false; break;
                case "material-group": pla.MaterialGroupId = (await context.MaterialGroups.SingleAsync(row => row.Name == "Other")).Id; break;
                case "density": pla.DensityKilogramPerCubicMeter = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(drift));
            }
            await context.SaveChangesAsync();
        }
        await AssertReadOnlyRefusalAsync();
    }

    [Theory]
    [InlineData("material")]
    [InlineData("group")]
    [InlineData("color")]
    [InlineData("finish")]
    public async Task Duplicate_required_identity_refuses_first_match_selection_and_leaks_no_catalog_value(string kind)
    {
        // Catches source-style GroupBy.First/FirstOrDefault instead of existing ambiguity refusal.
        await using (var context = fixture.Context())
        {
            switch (kind)
            {
                case "material": context.Materials.Add(new Material { Name = "pla", MaterialGroupId = (await context.MaterialGroups.SingleAsync(row => row.Name == "Plastics")).Id }); break;
                case "group": context.MaterialGroups.Add(new MaterialGroup { Name = "PLASTICS" }); break;
                case "color": context.Colors.Add(new Color { Name = "white" }); break;
                case "finish": context.SurfaceFinishes.Add(new SurfaceFinish { Name = "AS PRINTED" }); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            await context.SaveChangesAsync();
        }
        await AssertReadOnlyRefusalAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData(" true")]
    [InlineData("true ")]
    public async Task Nonexact_optin_does_not_turn_default_off_into_validation_or_mutation(string? value)
    {
        // Catches case-insensitive/trimmed opt-in and unconditional validation of incomplete legacy stores.
        await using (var context = fixture.Context())
        {
            (await context.Materials.SingleAsync(row => row.Name == "PLA")).Printable = false;
            await context.SaveChangesAsync();
        }
        using var environment = new CatalogEnvironmentScope(value);
        string before = await fixture.SnapshotAsync();
        var probe = new CatalogReadOnlyProbe();
        using var host = fixture.Host(null, probe);
        Assert.Null(Record.Exception(() => _ = host.Services));
        Assert.Empty(probe.ReadTables);
        AssertNoSideEffects(probe);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData(" true")]
    [InlineData("true ")]
    public async Task Nonexact_optin_preserves_explicit_enabled_reconciliation_and_four_cache_invalidations(string? value)
    {
        // Catches a read-only branch accidentally suppressing the owner's existing explicit mutation opt-in.
        await using (var context = fixture.Context())
        {
            (await context.Materials.SingleAsync(row => row.Name == "PLA")).Printable = false;
            await context.SaveChangesAsync();
        }
        using var environment = new CatalogEnvironmentScope(value);
        var probe = new CatalogReadOnlyProbe(false);
        using var host = fixture.Host(true, probe, readOnly: false);
        Assert.Null(Record.Exception(() => _ = host.Services));
        Assert.NotEmpty(probe.ForbiddenCommands);
        Assert.True(probe.Saves > 0);
        Assert.Equal(new[] { "materials:all:v1", "material-groups:all:v1", "colors:all:v1", "surface-finishes:all:v1" }, probe.RemovedKeys);
        await using var stored = fixture.Context();
        Assert.True((await stored.Materials.SingleAsync(row => row.Name == "PLA")).Printable);
    }

    [Fact]
    public async Task Registered_readonly_startup_propagates_precanceled_token_instead_of_returning_success()
    {
        using var environment = new CatalogEnvironmentScope(null);
        var probe = new CatalogReadOnlyProbe();
        using var host = fixture.Host(false, probe);
        var startup = host.Services.GetServices<IHostedService>().OfType<InstantQuotationCatalogStartupService>().Single();
        using var optin = new CatalogEnvironmentScope("true");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup.StartAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        AssertNoSideEffects(probe);
    }

    [Fact]
    public async Task Registered_readonly_startup_cancels_inflight_query_without_saves_or_cache_removal()
    {
        using var environment = new CatalogEnvironmentScope(null);
        var probe = new CatalogReadOnlyProbe { BlockFirstRead = true };
        using var host = fixture.Host(false, probe);
        var startup = host.Services.GetServices<IHostedService>().OfType<InstantQuotationCatalogStartupService>().Single();
        using var optin = new CatalogEnvironmentScope("true");
        using var cancellation = new CancellationTokenSource();
        var operation = startup.StartAsync(cancellation.Token);
        try
        {
            // Current missing validation completes instead of entering a query: a genuine assertion RED, not a timeout.
            await Task.WhenAny(operation, probe.Entered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(probe.Entered.Task.IsCompletedSuccessfully);
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            AssertNoSideEffects(probe);
        }
        finally
        {
            cancellation.Cancel();
            try { await operation; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Readonly_validation_does_not_take_reconciliation_lock_against_ordinary_writer()
    {
        // Row-exclusive ordinary writer is compatible with SELECT, not reconciliation SHARE ROW EXCLUSIVE.
        await using var writer = fixture.Context();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("LOCK TABLE \"Color\" IN ROW EXCLUSIVE MODE");
        using var environment = new CatalogEnvironmentScope("true");
        var probe = new CatalogReadOnlyProbe();
        using var host = fixture.Host(true, probe);
        Assert.Null(Record.Exception(() => _ = host.Services));
        Assert.Contains("Color", probe.ReadTables);
        AssertNoSideEffects(probe);
    }

    private async Task AssertReadOnlyRefusalAsync()
    {
        using var environment = new CatalogEnvironmentScope("true");
        string before = await fixture.SnapshotAsync();
        var probe = new CatalogReadOnlyProbe();
        using var host = fixture.Host(false, probe);
        var error = Record.Exception(() => _ = host.Services);
        Assert.NotNull(error);
        while (error.InnerException is not null) error = error.InnerException;
        var refusal = Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(FailureCode, refusal.Message);
        Assert.Null(refusal.InnerException);
        AssertNoSideEffects(probe);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    private static void AssertNoSideEffects(CatalogReadOnlyProbe probe)
    {
        Assert.Empty(probe.ForbiddenCommands);
        Assert.Empty(probe.RemovedKeys);
        Assert.Equal(0, probe.Tracked);
        Assert.Equal(0, probe.Saves);
    }
}
