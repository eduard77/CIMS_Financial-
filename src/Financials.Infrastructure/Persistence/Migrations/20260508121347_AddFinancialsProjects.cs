using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialsProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fin");

            migrationBuilder.CreateTable(
                name: "FinancialsProjects",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CimsProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialsProjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_FinancialsProjects_CimsProjectId",
                schema: "fin",
                table: "FinancialsProjects",
                column: "CimsProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialsProjects",
                schema: "fin");
        }
    }
}
