using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>An authoritative original Country ISO preimage, supplied independently of padded storage.</summary>
/// <param name="Id">Original Country identifier.</param>
/// <param name="Iso2">Original literal alpha-2 value.</param>
/// <param name="Iso3">Original literal alpha-3 value.</param>
public sealed record CountryIsoPreimage(int Id, string? Iso2, string? Iso3);

/// <summary>Explicitly updates only the owned Country schema after complete source-preimage verification.</summary>
public static class CountryIsoSchemaUpdater
{
    /// <summary>
    /// Changes fixed-width ISO columns to variable width without guessing or rewriting retained values.
    /// Missing, duplicate, ambiguous or mismatching source preimages fail before DDL. This method is
    /// never invoked automatically by API startup. Callers must independently authorize schema work.
    /// </summary>
    /// <param name="context">The independently owned Country database context.</param>
    /// <param name="preimages">Complete authoritative original literals, not inferred by trimming current rows.</param>
    /// <param name="cancellationToken">Cancellation for the bounded transaction.</param>
    public static async Task UpdateAsync(CatalogCountryDbContext context,
        IReadOnlyList<CountryIsoPreimage> preimages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preimages);
        if (preimages.Count > 10000 || preimages.Any(value => value.Id <= 0
                || value.Iso2?.Length > 2 || value.Iso3?.Length > 3)
            || preimages.Select(value => value.Id).Distinct().Count() != preimages.Count)
            throw new InvalidOperationException("Require complete bounded original Country ISO preimages.");
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Country schema updates require their own transaction.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await ExecuteAsync(connection, transaction.GetDbTransaction(),
            "SET LOCAL search_path = public; SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '30s'; LOCK TABLE \"Country\" IN ACCESS EXCLUSIVE MODE;",
            cancellationToken);
        var types = new List<(string Name, string Type, int? Length)>();
        await using (var command = CreateCommand(connection, transaction.GetDbTransaction(),
            "SELECT column_name, data_type, character_maximum_length FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'Country' AND column_name IN ('ISO2', 'ISO3') ORDER BY column_name"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                types.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        bool fixedWidth = types.SequenceEqual(new[] { ("ISO2", "character", (int?)2), ("ISO3", "character", (int?)3) });
        bool varying = types.SequenceEqual(new[] { ("ISO2", "character varying", (int?)2), ("ISO3", "character varying", (int?)3) });
        if (!fixedWidth && !varying)
            throw new InvalidOperationException("Unexpected owned Country ISO schema; no changes made.");
        var originalRows = await RowsAsync(connection, transaction.GetDbTransaction(), cancellationToken);
        if (!originalRows.Select(row => row.Preimage).SequenceEqual(preimages.OrderBy(value => value.Id)))
            throw new InvalidOperationException("Original literals are incomplete or cannot be preserved by schema-only conversion.");
        if (fixedWidth)
            await ExecuteAsync(connection, transaction.GetDbTransaction(),
                "ALTER TABLE \"Country\" ALTER COLUMN \"ISO2\" TYPE character varying(2) USING \"ISO2\"::text, "
                + "ALTER COLUMN \"ISO3\" TYPE character varying(3) USING \"ISO3\"::text;", cancellationToken);
        var convertedRows = await RowsAsync(connection, transaction.GetDbTransaction(), cancellationToken);
        if (!originalRows.SequenceEqual(convertedRows))
            throw new InvalidOperationException("Country row preservation verification failed; transaction must roll back.");
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<List<(CountryIsoPreimage Preimage, string OtherColumns)>> RowsAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = new List<(CountryIsoPreimage, string)>();
        await using var command = CreateCommand(connection, transaction,
            "SELECT \"ID\", \"ISO2\"::text, \"ISO3\"::text, (to_jsonb(c) - 'ISO2' - 'ISO3')::text FROM \"Country\" c ORDER BY \"ID\"");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= 10000) throw new InvalidOperationException("Country preimage bound exceeded.");
            rows.Add((new CountryIsoPreimage(reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)), reader.GetString(3)));
        }
        return rows;
    }

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 30;
        command.CommandText = sql;
        return command;
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
