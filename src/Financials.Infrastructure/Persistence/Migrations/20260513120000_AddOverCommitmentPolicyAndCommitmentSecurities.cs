using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverCommitmentPolicyAndCommitmentSecurities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0009 — over-commitment guard policy columns on the existing
            // ProjectCommercialConfigurations. Existing rows backfill to the
            // Sprint 6 default (Warn, 0 GBP).
            migrationBuilder.AddColumn<int>(
                name: "OverCommitmentMode",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "OverCommitmentToleranceAmount",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OverCommitmentToleranceCurrency",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "nchar(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "GBP");

            // ADR-0010 — commitment securities (bonds, warranties, insurances).
            migrationBuilder.CreateTable(
                name: "CommitmentSecurities",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommitmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IssuerCimsOrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValueAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ValueCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SupersededBySecurityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitmentSecurities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitmentSecurities_Commitments_CommitmentId",
                        column: x => x.CommitmentId,
                        principalSchema: "fin",
                        principalTable: "Commitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentSecurities_Issuer",
                schema: "fin",
                table: "CommitmentSecurities",
                column: "IssuerCimsOrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentSecurities_SupersededBy",
                schema: "fin",
                table: "CommitmentSecurities",
                column: "SupersededBySecurityId");

            migrationBuilder.CreateIndex(
                name: "UX_CommitmentSecurities_Commitment_Type_Reference",
                schema: "fin",
                table: "CommitmentSecurities",
                columns: new[] { "CommitmentId", "Type", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitmentSecurities",
                schema: "fin");

            migrationBuilder.DropColumn(
                name: "OverCommitmentToleranceCurrency",
                schema: "fin",
                table: "ProjectCommercialConfigurations");

            migrationBuilder.DropColumn(
                name: "OverCommitmentToleranceAmount",
                schema: "fin",
                table: "ProjectCommercialConfigurations");

            migrationBuilder.DropColumn(
                name: "OverCommitmentMode",
                schema: "fin",
                table: "ProjectCommercialConfigurations");
        }
    }
}
