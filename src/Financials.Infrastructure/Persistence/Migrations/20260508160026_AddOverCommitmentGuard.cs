using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverCommitmentGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OverCommitmentGuardMode",
                schema: "fin",
                table: "ProjectCommercialConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverCommitmentGuardMode",
                schema: "fin",
                table: "ProjectCommercialConfigurations");
        }
    }
}
