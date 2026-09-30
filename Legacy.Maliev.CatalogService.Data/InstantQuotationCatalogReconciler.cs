using Legacy.Maliev.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>Reconciles reviewed additive offers without replacing legacy identities or commercial data.</summary>
public sealed class InstantQuotationCatalogReconciler(DbContextOptions<CatalogDbContext> options, TimeProvider timeProvider)
{
    /// <summary>Atomically reconciles under ordinary-writer-compatible locks, using a fresh context on each retry.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var strategyContext = new CatalogDbContext(options);
        await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var context = new CatalogDbContext(options);
            // Command timeout must not expire before the reviewed 60-second lock bound.
            context.Database.SetCommandTimeout(65);
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            // Statement timeout bounds the complete six-table acquisition, not each table separately.
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '60s'; SET LOCAL statement_timeout = '60s'", token);
            await context.Database.ExecuteSqlRawAsync(
                "LOCK TABLE \"Color\", \"Material\", \"MaterialGroup\", \"MaterialHasColor\", \"MaterialHasSurfaceFinish\", \"SurfaceFinish\" IN SHARE ROW EXCLUSIVE MODE", token);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '0'", token);
            await ReconcileCoreAsync(context, token);
            await transaction.CommitAsync(token);
        }, cancellationToken);
    }

    private async Task ReconcileCoreAsync(CatalogDbContext context, CancellationToken token)
    {
        var definitions = InstantQuotationMaterialCatalog.Materials;
        var groups = Unique(await context.MaterialGroups.ToListAsync(token), row => row.Name, ["Plastics"]);
        var materials = Unique(await context.Materials.ToListAsync(token), row => row.Name, definitions.Select(row => row.Name));
        var requiredColors = definitions.SelectMany(row => row.Colors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var colors = Unique(await context.Colors.ToListAsync(token), row => row.Name, requiredColors);
        var finishes = Unique(await context.SurfaceFinishes.ToListAsync(token), row => row.Name, ["As printed"]);
        var colorLinks = (await context.MaterialHasColors.ToListAsync(token)).Select(row => (row.MaterialId, row.ColorId)).ToHashSet();
        var finishLinks = (await context.MaterialHasSurfaceFinishes.ToListAsync(token)).Select(row => (row.MaterialId, row.SurfaceFinishId)).ToHashSet();
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        if (!groups.TryGetValue("Plastics", out var plastics))
        {
            plastics = new MaterialGroup { Name = "Plastics", Description = "Printable polymers and resins.", CreatedDate = now, ModifiedDate = now };
            context.MaterialGroups.Add(plastics);
        }
        foreach (var definition in definitions)
        {
            if (!materials.TryGetValue(definition.Name, out var material))
            {
                material = new Material
                {
                    Name = definition.Name,
                    MaterialGroup = plastics,
                    Printable = true,
                    DensityKilogramPerCubicMeter = definition.Density,
                    Comment = $"Instant quotation catalog key: {definition.Key}",
                    CreatedDate = now,
                    ModifiedDate = now
                };
                context.Materials.Add(material);
                materials.Add(definition.Name, material);
                continue;
            }
            if (!material.Printable || material.MaterialGroupId != plastics.Id ||
                (material.DensityKilogramPerCubicMeter is null && definition.Density is not null))
            {
                material.Printable = true;
                material.MaterialGroup = plastics;
                material.DensityKilogramPerCubicMeter ??= definition.Density;
                material.ModifiedDate = now;
            }
        }
        foreach (string name in requiredColors)
        {
            if (colors.ContainsKey(name)) continue;
            var color = new Color { Name = name, CreatedDate = now, ModifiedDate = now };
            context.Colors.Add(color);
            colors.Add(name, color);
        }
        if (!finishes.TryGetValue("As printed", out var asPrinted))
        {
            asPrinted = new SurfaceFinish { Name = "As printed", CreatedDate = now, ModifiedDate = now };
            context.SurfaceFinishes.Add(asPrinted);
        }
        await context.SaveChangesAsync(token);
        foreach (var definition in definitions)
        {
            var material = materials[definition.Name];
            foreach (string name in definition.Colors)
            {
                var color = colors[name];
                if (colorLinks.Add((material.Id, color.Id)))
                    context.MaterialHasColors.Add(new MaterialHasColor { MaterialId = material.Id, ColorId = color.Id, CreatedDate = now, ModifiedDate = now });
            }
            if (finishLinks.Add((material.Id, asPrinted.Id)))
                context.MaterialHasSurfaceFinishes.Add(new MaterialHasSurfaceFinish { MaterialId = material.Id, SurfaceFinishId = asPrinted.Id, CreatedDate = now, ModifiedDate = now });
        }
        await context.SaveChangesAsync(token);
    }

    private static Dictionary<string, T> Unique<T>(IEnumerable<T> rows, Func<T, string> name, IEnumerable<string> required)
    {
        var wanted = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            string key = name(row);
            if (wanted.Contains(key) && !result.TryAdd(key, row))
                throw new InvalidOperationException($"Ambiguous {typeof(T).Name} catalog name '{key}'; reconciliation refused.");
        }
        return result;
    }
}
