using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Commitments",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialsProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CounterpartyCimsOrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RetentionOverridePercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RetentionOverrideReleaseAtPCPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RetentionOverrideReleaseAtDLPEndPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    PaymentOverrideNetDays = table.Column<int>(type: "int", nullable: true),
                    PaymentOverrideCycleDays = table.Column<int>(type: "int", nullable: true),
                    PaymentOverrideDueDayOfMonth = table.Column<int>(type: "int", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ActivatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commitments_FinancialsProjects_FinancialsProjectId",
                        column: x => x.FinancialsProjectId,
                        principalSchema: "fin",
                        principalTable: "FinancialsProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommitmentLines",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommitmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    CimsCostCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitRateAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitRateCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ValueAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ValueCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitmentLines_Commitments_CommitmentId",
                        column: x => x.CommitmentId,
                        principalSchema: "fin",
                        principalTable: "Commitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentLines_CimsCostCodeId",
                schema: "fin",
                table: "CommitmentLines",
                column: "CimsCostCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_CommitmentLines_Commitment_LineNumber",
                schema: "fin",
                table: "CommitmentLines",
                columns: new[] { "CommitmentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_Counterparty",
                schema: "fin",
                table: "Commitments",
                column: "CounterpartyCimsOrganisationId");

            migrationBuilder.CreateIndex(
                name: "UX_Commitments_Project_Type_Reference",
                schema: "fin",
                table: "Commitments",
                columns: new[] { "FinancialsProjectId", "Type", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitmentLines",
                schema: "fin");

            migrationBuilder.DropTable(
                name: "Commitments",
                schema: "fin");
        }
    }
}
