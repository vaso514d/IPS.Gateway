using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class TransactionWorkClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimExpiresAtUtc",
                table: "Transactions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClaimToken",
                table: "Transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextActionAtUtc",
                table: "Transactions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ClaimExpiresAtUtc",
                table: "Transactions",
                column: "ClaimExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Direction_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions",
                columns: new[] { "Direction", "CurrentStatus", "MessageType", "CurrentStatusAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Claim",
                table: "Transactions",
                sql: "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ClaimExpiresAtUtc",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Direction_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Claim",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClaimExpiresAtUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClaimToken",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "NextActionAtUtc",
                table: "Transactions");
        }
    }
}
