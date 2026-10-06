using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.CatalogService.Data.Migrations;

/// <inheritdoc />
public partial class PreserveLiteralCountryIsoCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Consolidated Country is not the independently owned Country role. Without
        // independently supplied source preimages, retained rows cannot be guessed.
        RequireEmptyCountry(migrationBuilder);
        migrationBuilder.AlterColumn<string>(name: "ISO2", table: "Country", type: "character varying(2)",
            maxLength: 2, nullable: true, oldClrType: typeof(string), oldType: "character(2)", oldMaxLength: 2, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "ISO3", table: "Country", type: "character varying(3)",
            maxLength: 3, nullable: true, oldClrType: typeof(string), oldType: "character(3)", oldMaxLength: 3, oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RequireEmptyCountry(migrationBuilder);
        migrationBuilder.AlterColumn<string>(name: "ISO2", table: "Country", type: "character(2)",
            maxLength: 2, nullable: true, oldClrType: typeof(string), oldType: "character varying(2)", oldMaxLength: 2, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "ISO3", table: "Country", type: "character(3)",
            maxLength: 3, nullable: true, oldClrType: typeof(string), oldType: "character varying(3)", oldMaxLength: 3, oldNullable: true);
    }

    private static void RequireEmptyCountry(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL search_path = public;
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Country" IN ACCESS EXCLUSIVE MODE;
        DO $guard$
        BEGIN
            IF EXISTS (SELECT 1 FROM "Country") THEN
                RAISE EXCEPTION 'Retained consolidated Country rows require independent source-preimage handling; no schema changes made';
            END IF;
        END
        $guard$;
        """);
}
