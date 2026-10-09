using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ClaimExpiresAtUtc",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence",
                table: "OutgoingStatusDeliveries");

            migrationBuilder.AddColumn<int>(
                name: "DispatchPriority",
                table: "Transactions",
                type: "int",
                nullable: false,
                computedColumnSql: "CASE WHEN [MessageType] = N'pacs.008' THEN 0 ELSE 1 END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ClaimExpiresAtUtc",
                table: "Transactions",
                column: "ClaimExpiresAtUtc")
                .Annotation("SqlServer:Include", new[] { "ClaimToken", "DispatchPriority", "CurrentStatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CurrentStatus_DispatchPriority_CurrentStatusAtUtc_Id",
                table: "Transactions",
                columns: new[] { "CurrentStatus", "DispatchPriority", "CurrentStatusAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "ClaimToken", "NextActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_NextActionAtUtc",
                table: "Transactions",
                column: "NextActionAtUtc",
                filter: "[NextActionAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence",
                table: "OutgoingStatusDeliveries",
                columns: new[] { "State", "NextAtUtc", "PaymentId", "Sequence" })
                .Annotation("SqlServer:Include", new[] { "ClaimToken", "ClaimExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ClaimExpiresAtUtc",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CurrentStatus_DispatchPriority_CurrentStatusAtUtc_Id",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_NextActionAtUtc",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence",
                table: "OutgoingStatusDeliveries");

            migrationBuilder.DropColumn(
                name: "DispatchPriority",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ClaimExpiresAtUtc",
                table: "Transactions",
                column: "ClaimExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id",
                table: "Transactions",
                columns: new[] { "CurrentStatus", "MessageType", "CurrentStatusAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence",
                table: "OutgoingStatusDeliveries",
                columns: new[] { "State", "NextAtUtc", "PaymentId", "Sequence" });
        }
    }
}
