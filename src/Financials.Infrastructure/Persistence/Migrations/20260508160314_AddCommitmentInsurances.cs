using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitmentInsurances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommitmentInsurances",
                schema: "fin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommitmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    SubType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PolicyNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ValueAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ValueCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitmentInsurances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitmentInsurances_Commitments_CommitmentId",
                        column: x => x.CommitmentId,
                        principalSchema: "fin",
                        principalTable: "Commitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentInsurances_Commitment",
                schema: "fin",
                table: "CommitmentInsurances",
                column: "CommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommitmentInsurances_ExpiresAt",
                schema: "fin",
                table: "CommitmentInsurances",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitmentInsurances",
                schema: "fin");
        }
    }
}
