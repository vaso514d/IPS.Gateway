using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class FrozenReconciliationDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciliationDeadlineUtc",
                table: "IncomingPayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingPayments_FollowUpDeadline",
                table: "IncomingPayments",
                sql: "[FollowUpAtUtc] IS NULL OR ([ReconciliationDeadlineUtc] IS NOT NULL AND [FollowUpAtUtc] <= [ReconciliationDeadlineUtc])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingPayments_FollowUpDeadline",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "ReconciliationDeadlineUtc",
                table: "IncomingPayments");
        }
    }
}
