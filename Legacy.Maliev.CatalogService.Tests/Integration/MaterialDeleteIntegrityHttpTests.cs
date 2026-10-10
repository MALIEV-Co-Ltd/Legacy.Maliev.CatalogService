using System.Data.Common;
using System.Net;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class MaterialDeleteIntegrityHttpTests(MaterialCollectionFailureFixture fixture)
    : IClassFixture<MaterialCollectionFailureFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Delete_preserves_unrelated_linked_graph_and_rejects_missing_or_unauthorized_without_mutation()
    {
        await SeedLinksAsync();
        var before = await GraphAsync();
        var sentinel = await GraphAsync(2);
        using var host = fixture.Start();
        AssertRetriesEnabled(host.Services);
        using var authorized = fixture.Client(host);
        foreach (var (authority, status) in new[] { ("none", HttpStatusCode.Unauthorized), ("read-only", HttpStatusCode.Forbidden), ("wildcard", HttpStatusCode.Forbidden) })
        {
            using var denied = fixture.Client(host, authority);
            using var response = await denied.DeleteAsync("/Materials/1");
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(before, await GraphAsync());
        }
        using var missing = await authorized.DeleteAsync("/Materials/99999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, await GraphAsync());
        using var deleted = await authorized.DeleteAsync("/Materials/1");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(sentinel, await GraphAsync(2));
        await using var context = fixture.Context();
        Assert.False(await context.Materials.AnyAsync(row => row.Id == 1));
        Assert.False(await context.MaterialHasColors.AnyAsync(row => row.MaterialId == 1));
        Assert.False(await context.MaterialHasSuppliers.AnyAsync(row => row.MaterialId == 1));
        Assert.False(await context.MaterialHasSurfaceFinishes.AnyAsync(row => row.MaterialId == 1));
        Assert.Equal(2, await context.MaterialGroups.CountAsync());
        Assert.Equal(2, await context.Colors.CountAsync());
        Assert.Equal(2, await context.SurfaceFinishes.CountAsync());
        using var repeated = await authorized.DeleteAsync("/Materials/1");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal(sentinel, await GraphAsync(2));
    }

    [Fact]
    public async Task PostgreSql_rejection_of_final_material_delete_rolls_back_all_link_deletes()
    {
        await SeedLinksAsync();
        var before = await GraphAsync();
        await using var setup = fixture.Context();
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.catalog_test_reject_material_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD."ID" = 1 THEN
                    RAISE EXCEPTION 'owned fixture material deletion rejected';
                END IF;
                RETURN OLD;
            END $$;
            CREATE TRIGGER catalog_test_reject_material_delete
                BEFORE DELETE ON "Material" FOR EACH ROW
                EXECUTE FUNCTION public.catalog_test_reject_material_delete();
            """);
        try
        {
            using var host = fixture.Start();
            AssertRetriesEnabled(host.Services);
            using var client = fixture.Client(host);
            using var response = await client.DeleteAsync("/Materials/1");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(before, await GraphAsync());
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS catalog_test_reject_material_delete ON "Material";
                DROP FUNCTION IF EXISTS public.catalog_test_reject_material_delete();
                """);
        }
        using var recoveredHost = fixture.Start();
        using var recovered = fixture.Client(recoveredHost);
        using var deleted = await recovered.DeleteAsync("/Materials/1");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Caller_abort_after_link_deletes_before_final_save_rolls_back_entire_graph()
    {
        await SeedLinksAsync();
        var before = await GraphAsync();
        var barrier = new MaterialSaveCancellationBarrier();
        using var host = fixture.Start(saveBarrier: barrier);
        AssertRetriesEnabled(host.Services);
        using var client = fixture.Client(host);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var request = client.DeleteAsync("/Materials/1", cancellation.Token);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var response = await request; });
            await barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            try { using var response = await request.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
        }
        // Stop the owned request host before independent readback so rollback/connection disposal has completed.
        await host.DisposeAsync();
        Assert.Equal(before, await GraphAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_after_save_preserves_deleted_state_and_does_not_fabricate_ambiguous_commit_success(bool afterCommit)
    {
        await SeedLinksAsync();
        var sentinel = await GraphAsync(2);
        var observer = new DeleteSaveStateObserver();
        var failure = new DeleteCommitFailure(afterCommit);
        using var originalHost = fixture.Start();
        using var host = originalHost.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(observer, failure))));
        AssertRetriesEnabled(host.Services);
        using var client = fixture.Client(host);
        using var response = await client.DeleteAsync("/Materials/1");
        Assert.Equal(afterCommit ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(failure.Injected);
        Assert.Equal(new[] { EntityState.Deleted, EntityState.Deleted }, observer.States);
        Assert.Equal(sentinel, await GraphAsync());
        using var repeated = await client.DeleteAsync("/Materials/1");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal(sentinel, await GraphAsync());
    }

    private static void AssertRetriesEnabled(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    private sealed class DeleteSaveStateObserver : SaveChangesInterceptor
    {
        public List<EntityState> States { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            States.Add(Assert.Single(eventData.Context!.ChangeTracker.Entries<Material>()).State);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeleteCommitFailure(bool afterCommit) : DbTransactionInterceptor
    {
        public bool Injected { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!afterCommit) FailOnce();
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (afterCommit) FailOnce();
            return Task.CompletedTask;
        }

        private void FailOnce()
        {
            if (Injected) return;
            Injected = true;
            throw new NpgsqlException("owned test transient commit transport failure", new IOException("owned test transport"));
        }
    }

    private async Task SeedLinksAsync()
    {
        await using var context = fixture.Context();
        var date = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);
        foreach (int id in new[] { 1, 2 })
        {
            context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = id, ColorId = id, CreatedDate = date, ModifiedDate = date });
            context.MaterialHasSuppliers.Add(new MaterialHasSupplier { MaterialId = id, SupplierId = id * 111, CreatedDate = date, ModifiedDate = date });
            context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = id, SurfaceFinishId = id, CreatedDate = date, ModifiedDate = date });
        }
        await context.SaveChangesAsync();
    }

    private async Task<string> GraphAsync(int? materialId = null)
    {
        await using var context = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Groups = await context.MaterialGroups.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            ColorParents = await context.Colors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            FinishParents = await context.SurfaceFinishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Materials = await context.Materials.AsNoTracking().Where(row => materialId == null || row.Id == materialId).OrderBy(row => row.Id).ToArrayAsync(),
            Colors = await context.MaterialHasColors.AsNoTracking().Where(row => materialId == null || row.MaterialId == materialId).OrderBy(row => row.Id).ToArrayAsync(),
            Suppliers = await context.MaterialHasSuppliers.AsNoTracking().Where(row => materialId == null || row.MaterialId == materialId).OrderBy(row => row.Id).ToArrayAsync(),
            Finishes = await context.MaterialHasSurfaceFinishes.AsNoTracking().Where(row => materialId == null || row.MaterialId == materialId).OrderBy(row => row.Id).ToArrayAsync(),
        });
    }
}
