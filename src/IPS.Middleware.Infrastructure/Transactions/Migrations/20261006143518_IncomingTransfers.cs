using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class IncomingTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionEvents_Kind",
                table: "TransactionEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AggregateIdentities_Kind",
                table: "AggregateIdentities");

            migrationBuilder.CreateTable(
                name: "IncomingTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantBic = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CoreStatus = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CoreProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CoreReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CoreReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    CoreDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ManualReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AggregateKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, computedColumnSql: "CONVERT(varchar(32), 'incoming-transfer')", stored: true),
                    EndToEndIdBytes = table.Column<int>(type: "int", nullable: false, computedColumnSql: "DATALENGTH([EndToEndId])", stored: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeadlineUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingTransfers", x => x.Id);
                    table.CheckConstraint("CK_IncomingTransfers_Claim", "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_IncomingTransfers_Request", "ISJSON([RequestJson]) = 1");
                    table.ForeignKey(
                        name: "FK_IncomingTransfers_AggregateIdentities_Id_AggregateKind",
                        columns: x => new { x.Id, x.AggregateKind },
                        principalTable: "AggregateIdentities",
                        principalColumns: new[] { "Id", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionEvents_Kind",
                table: "TransactionEvents",
                sql: "([AggregateKind] = 'outgoing-payment' AND [Name] LIKE 'payment.%') OR ([AggregateKind] = 'incoming-payment' AND [Name] LIKE 'incoming-payment.%') OR ([AggregateKind] = 'incoming-transfer' AND [Name] LIKE 'incoming-transfer.%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AggregateIdentities_Kind",
                table: "AggregateIdentities",
                sql: "[Kind] IN ('outgoing-payment', 'incoming-payment', 'incoming-transfer')");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_Id_AggregateKind",
                table: "IncomingTransfers",
                columns: new[] { "Id", "AggregateKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_NextActionAtUtc_Id",
                table: "IncomingTransfers",
                columns: new[] { "NextActionAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_ParticipantBic_EndToEndId_EndToEndIdBytes",
                table: "IncomingTransfers",
                columns: new[] { "ParticipantBic", "EndToEndId", "EndToEndIdBytes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncomingTransfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionEvents_Kind",
                table: "TransactionEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AggregateIdentities_Kind",
                table: "AggregateIdentities");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionEvents_Kind",
                table: "TransactionEvents",
                sql: "([AggregateKind] = 'outgoing-payment' AND [Name] LIKE 'payment.%') OR ([AggregateKind] = 'incoming-payment' AND [Name] LIKE 'incoming-payment.%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AggregateIdentities_Kind",
                table: "AggregateIdentities",
                sql: "[Kind] IN ('outgoing-payment', 'incoming-payment')");
        }
    }
}
