using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingStatusDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutgoingStatusDeliveries",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PayloadVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailure = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingStatusDeliveries", x => new { x.PaymentId, x.Sequence });
                    table.CheckConstraint("CK_OutgoingStatusDeliveries_Claim", "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL AND [Attempts] > 0 AND [State] = 0)");
                    table.CheckConstraint("CK_OutgoingStatusDeliveries_Payload", "[Sequence] > 0 AND [PayloadVersion] = 1 AND ISJSON([PayloadJson]) = 1 AND [Attempts] >= 0");
                    table.CheckConstraint("CK_OutgoingStatusDeliveries_State", "([State] = 0 AND [NextAtUtc] IS NOT NULL AND [DeliveredAtUtc] IS NULL) OR ([State] = 1 AND [NextAtUtc] IS NULL AND [DeliveredAtUtc] IS NOT NULL AND [ClaimToken] IS NULL) OR ([State] = 2 AND [NextAtUtc] IS NULL AND [DeliveredAtUtc] IS NULL AND [ClaimToken] IS NULL)");
                    table.ForeignKey(
                        name: "FK_OutgoingStatusDeliveries_Transactions_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence",
                table: "OutgoingStatusDeliveries",
                columns: new[] { "State", "NextAtUtc", "PaymentId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutgoingStatusDeliveries");
        }
    }
}
