using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeEventsAndNec4SlaPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0011 — NEC4 SLA policy columns on the existing
            // ProjectCommercialConfigurations. Existing rows backfill to the
            // NEC4 ECC standard-form defaults (1 / 3 / 2 / 1 weeks).
            migrationBuilder.AddColumn<int>(
                name: "Nec4PmAcknowledgementDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "Nec4ContractorQuotationDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 21);

            migrationBuilder.AddColumn<int>(
                name: "Nec4PmAssessmentDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<int>(
                name: "Nec4EarlyWarningResponseDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 7);

            // ADR-0011 — change events.
            migrationBuilder.CreateTable(
                name: "ChangeEvents",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialsProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EstimatedNetEffectAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    EstimatedNetEffectCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    NotifiedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    NotifiedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QuotationSubmittedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    QuotationSubmittedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AssessedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    AssessedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ImplementedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ImplementedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RejectedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EarlyWarningReducedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    EarlyWarningReducedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EarlyWarningClosedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    EarlyWarningClosedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceCimsRfiId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeEvents_FinancialsProjects_FinancialsProjectId",
                        column: x => x.FinancialsProjectId,
                        principalSchema: "fin",
                        principalTable: "FinancialsProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_SourceCimsRfi",
                schema: "fin",
                table: "ChangeEvents",
                column: "SourceCimsRfiId");

            migrationBuilder.CreateIndex(
                name: "UX_ChangeEvents_Project_Type_Reference",
                schema: "fin",
                table: "ChangeEvents",
                columns: new[] { "FinancialsProjectId", "Type", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeEvents",
                schema: "fin");

            migrationBuilder.DropColumn(
                name: "Nec4EarlyWarningResponseDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations");

            migrationBuilder.DropColumn(
                name: "Nec4PmAssessmentDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations");

            migrationBuilder.DropColumn(
                name: "Nec4ContractorQuotationDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations");

            migrationBuilder.DropColumn(
                name: "Nec4PmAcknowledgementDays",
                schema: "fin",
                table: "ProjectCommercialConfigurations");
        }
    }
}
