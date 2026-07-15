using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Legacy.Maliev.CatalogService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgresCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Color",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Color", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "Country",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Continent = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ISO2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ISO3 = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Country", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "Currency",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShortName = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LongName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "MaterialGroup",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialGroup", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "SurfaceFinish",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurfaceFinish", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "Material",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialGroupID = table.Column<int>(type: "integer", nullable: false),
                    Machinable = table.Column<bool>(type: "boolean", nullable: false),
                    Printable = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AISI = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DIN = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BTS = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    JIS = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UNS = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EN = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AFNOR = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UNI = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SIS = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SAE = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ASTM = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AMS = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MaterialNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ManufacturerReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HardnessBrinell = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    HardnessKnoop = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    HardnessRockwellA = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    HardnessRockwellB = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    HardnessRockwellC = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    HardnessVickers = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    DensityKilogramPerCubicMeter = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    TensileStrengthUltimateGigaPascal = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    TensileStrengthYieldMegaPascal = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    MachinabilityPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ShearModulusGigaPascal = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    ThermalConductivityWattPerMeterKelvin = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    URL = table.Column<string>(type: "text", nullable: true),
                    PricePerKilogram = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyID = table.Column<int>(type: "integer", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Material", x => x.ID);
                    table.ForeignKey(
                        name: "FK_Material_MaterialGroup",
                        column: x => x.MaterialGroupID,
                        principalTable: "MaterialGroup",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "MaterialHasColor",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialID = table.Column<int>(type: "integer", nullable: false),
                    ColorID = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialHasColor", x => x.ID);
                    table.ForeignKey(
                        name: "FK_MaterialHasColor_Color",
                        column: x => x.ColorID,
                        principalTable: "Color",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_MaterialHasColor_Material",
                        column: x => x.MaterialID,
                        principalTable: "Material",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "MaterialHasSupplier",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialID = table.Column<int>(type: "integer", nullable: false),
                    SupplierID = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialHasSupplier", x => x.ID);
                    table.ForeignKey(
                        name: "FK_MaterialHasSupplier_Material",
                        column: x => x.MaterialID,
                        principalTable: "Material",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "MaterialHasSurfaceFinish",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialID = table.Column<int>(type: "integer", nullable: false),
                    SurfaceFinishID = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialHasSurfaceFinish", x => x.ID);
                    table.ForeignKey(
                        name: "FK_MaterialHasSurfaceFinish_Material",
                        column: x => x.MaterialID,
                        principalTable: "Material",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_MaterialHasSurfaceFinish_SurfaceFinish",
                        column: x => x.SurfaceFinishID,
                        principalTable: "SurfaceFinish",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Material_MaterialGroupID",
                table: "Material",
                column: "MaterialGroupID");

            migrationBuilder.CreateIndex(
                name: "IX_Material_Name",
                table: "Material",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialHasColor_ColorID",
                table: "MaterialHasColor",
                column: "ColorID");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialHasColor_MaterialID_ColorID",
                table: "MaterialHasColor",
                columns: new[] { "MaterialID", "ColorID" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialHasSupplier_MaterialID_SupplierID",
                table: "MaterialHasSupplier",
                columns: new[] { "MaterialID", "SupplierID" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialHasSurfaceFinish_MaterialID_SurfaceFinishID",
                table: "MaterialHasSurfaceFinish",
                columns: new[] { "MaterialID", "SurfaceFinishID" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialHasSurfaceFinish_SurfaceFinishID",
                table: "MaterialHasSurfaceFinish",
                column: "SurfaceFinishID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Country");

            migrationBuilder.DropTable(
                name: "Currency");

            migrationBuilder.DropTable(
                name: "MaterialHasColor");

            migrationBuilder.DropTable(
                name: "MaterialHasSupplier");

            migrationBuilder.DropTable(
                name: "MaterialHasSurfaceFinish");

            migrationBuilder.DropTable(
                name: "Color");

            migrationBuilder.DropTable(
                name: "Material");

            migrationBuilder.DropTable(
                name: "SurfaceFinish");

            migrationBuilder.DropTable(
                name: "MaterialGroup");
        }
    }
}
