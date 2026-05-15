using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCommercialConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectCommercialConfigurations",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialsProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetentionPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RetentionReleaseAtPCPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RetentionReleaseAtDLPEndPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PaymentNetDays = table.Column<int>(type: "int", nullable: false),
                    PaymentCycleDays = table.Column<int>(type: "int", nullable: false),
                    PaymentDueDayOfMonth = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommercialConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommercialConfigurations_FinancialsProjects_FinancialsProjectId",
                        column: x => x.FinancialsProjectId,
                        principalSchema: "fin",
                        principalTable: "FinancialsProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectCommercialConfigurations_FinancialsProjectId",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                column: "FinancialsProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectCommercialConfigurations",
                schema: "fin");
        }
    }
}
