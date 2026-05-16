using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxEventNextAttemptAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                schema: "fin",
                table: "OutboxEvents",
                type: "datetime2(7)",
                nullable: true);

            // Seed NextAttemptAt for any existing Pending rows so they remain
            // immediately claimable on the next poll. Without this, Pending
            // rows enqueued before the column existed would have NULL — which
            // the dispatcher's claim query treats as "claim immediately" — so
            // strictly speaking the seed is belt-and-braces. Dispatched (=1)
            // and Failed (=2) rows are terminal and stay NULL.
            migrationBuilder.Sql(
                "UPDATE fin.OutboxEvents SET NextAttemptAt = OccurredAt WHERE Status = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "fin",
                table: "OutboxEvents");
        }
    }
}
