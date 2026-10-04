using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class IncomingReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingCoreCalls_Kind",
                table: "IncomingCoreCalls");

            migrationBuilder.AddColumn<string>(
                name: "ManualReviewReason",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Reversal",
                table: "IncomingPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversalObservedAtUtc",
                table: "IncomingPayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "IncomingCoreCalls",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_FollowUpAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments",
                columns: new[] { "FollowUpAtUtc", "RegisteredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingCoreCalls_PaymentId_Kind",
                table: "IncomingCoreCalls",
                columns: new[] { "PaymentId", "Kind" },
                unique: true,
                filter: "[Kind] = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingCoreCalls_Kind",
                table: "IncomingCoreCalls",
                sql: "[Kind] IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingCoreCalls_Request",
                table: "IncomingCoreCalls",
                sql: "([Kind] = 3 AND [RequestJson] IS NOT NULL AND ISJSON([RequestJson]) = 1) OR ([Kind] <> 3 AND [RequestJson] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingPayments_FollowUpAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments");

            migrationBuilder.DropIndex(
                name: "IX_IncomingCoreCalls_PaymentId_Kind",
                table: "IncomingCoreCalls");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingCoreCalls_Kind",
                table: "IncomingCoreCalls");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingCoreCalls_Request",
                table: "IncomingCoreCalls");

            migrationBuilder.DropColumn(
                name: "ManualReviewReason",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "Reversal",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "ReversalObservedAtUtc",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "RequestJson",
                table: "IncomingCoreCalls");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingCoreCalls_Kind",
                table: "IncomingCoreCalls",
                sql: "[Kind] IN (0, 1)");
        }
    }
}
