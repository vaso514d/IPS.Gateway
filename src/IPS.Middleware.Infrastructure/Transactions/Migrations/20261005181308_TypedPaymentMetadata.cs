using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class TypedPaymentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Direction_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_IncomingPayments_FollowUpAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments");

            migrationBuilder.DropIndex(
                name: "IX_IncomingPayments_NextActionAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions",
                columns: new[] { "CurrentStatus", "MessageType", "CurrentStatusAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_FollowUpAtUtc_Id",
                table: "IncomingPayments",
                columns: new[] { "FollowUpAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_NextActionAtUtc_Id",
                table: "IncomingPayments",
                columns: new[] { "NextActionAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_IncomingPayments_FollowUpAtUtc_Id",
                table: "IncomingPayments");

            migrationBuilder.DropIndex(
                name: "IX_IncomingPayments_NextActionAtUtc_Id",
                table: "IncomingPayments");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Direction_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions",
                columns: new[] { "Direction", "CurrentStatus", "MessageType", "CurrentStatusAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_FollowUpAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments",
                columns: new[] { "FollowUpAtUtc", "RegisteredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_NextActionAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments",
                columns: new[] { "NextActionAtUtc", "RegisteredAtUtc", "Id" });
        }
    }
}
