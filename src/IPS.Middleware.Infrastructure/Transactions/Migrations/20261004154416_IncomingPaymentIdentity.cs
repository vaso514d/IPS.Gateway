using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class IncomingPaymentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionEvents_Transactions_TransactionId",
                table: "TransactionEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InboundJournal_Sequence",
                table: "InboundMessageJournal");

            migrationBuilder.AddColumn<string>(
                name: "AggregateKind",
                table: "TransactionEvents",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "IncomingPaymentId",
                table: "InboundMessageJournal",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalJson",
                table: "InboundMessageJournal",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AggregateKind",
                table: "Transactions",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                computedColumnSql: "CONVERT(varchar(32), 'outgoing-payment')",
                stored: true);

            migrationBuilder.CreateTable(
                name: "AggregateIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AggregateIdentities", x => x.Id);
                    table.UniqueConstraint("AK_AggregateIdentities_Id_Kind", x => new { x.Id, x.Kind });
                    table.CheckConstraint("CK_AggregateIdentities_Kind", "[Kind] IN ('outgoing-payment', 'incoming-payment')");
                });

            migrationBuilder.CreateTable(
                name: "IncomingPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantBic = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AggregateKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, computedColumnSql: "CONVERT(varchar(32), 'incoming-payment')", stored: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndToEndIdBytes = table.Column<int>(type: "int", nullable: false, computedColumnSql: "DATALENGTH([EndToEndId])", stored: true),
                    NextActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingPayments", x => x.Id);
                    table.CheckConstraint("CK_IncomingPayments_Claim", "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_IncomingPayments_Request", "ISJSON([RequestJson]) = 1");
                    table.ForeignKey(
                        name: "FK_IncomingPayments_AggregateIdentities_Id_AggregateKind",
                        columns: x => new { x.Id, x.AggregateKind },
                        principalTable: "AggregateIdentities",
                        principalColumns: new[] { "Id", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Id_AggregateKind",
                table: "Transactions",
                columns: new[] { "Id", "AggregateKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEvents_TransactionId_AggregateKind",
                table: "TransactionEvents",
                columns: new[] { "TransactionId", "AggregateKind" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransactionEvents_Kind",
                table: "TransactionEvents",
                sql: "([AggregateKind] = 'outgoing-payment' AND [Name] LIKE 'payment.%') OR ([AggregateKind] = 'incoming-payment' AND [Name] LIKE 'incoming-payment.%')");

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageJournal_IncomingPaymentId",
                table: "InboundMessageJournal",
                column: "IncomingPaymentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboundJournal_Attachment",
                table: "InboundMessageJournal",
                sql: "([IncomingPaymentId] IS NULL AND [OriginalJson] IS NULL) OR ([IncomingPaymentId] IS NOT NULL AND ISJSON([OriginalJson]) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboundJournal_Sequence",
                table: "InboundMessageJournal",
                sql: "([Status] = 2 AND [HoldReason] IS NOT NULL) OR ([Status] IN (0, 1) AND [Sequence] IS NOT NULL AND [Sequence] > 0 AND [HoldReason] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_Id_AggregateKind",
                table: "IncomingPayments",
                columns: new[] { "Id", "AggregateKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_NextActionAtUtc_RegisteredAtUtc_Id",
                table: "IncomingPayments",
                columns: new[] { "NextActionAtUtc", "RegisteredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingPayments_ParticipantBic_EndToEndId_EndToEndIdBytes",
                table: "IncomingPayments",
                columns: new[] { "ParticipantBic", "EndToEndId", "EndToEndIdBytes" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InboundMessageJournal_IncomingPayments_IncomingPaymentId",
                table: "InboundMessageJournal",
                column: "IncomingPaymentId",
                principalTable: "IncomingPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionEvents_AggregateIdentities_TransactionId_AggregateKind",
                table: "TransactionEvents",
                columns: new[] { "TransactionId", "AggregateKind" },
                principalTable: "AggregateIdentities",
                principalColumns: new[] { "Id", "Kind" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_AggregateIdentities_Id_AggregateKind",
                table: "Transactions",
                columns: new[] { "Id", "AggregateKind" },
                principalTable: "AggregateIdentities",
                principalColumns: new[] { "Id", "Kind" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InboundMessageJournal_IncomingPayments_IncomingPaymentId",
                table: "InboundMessageJournal");

            migrationBuilder.DropForeignKey(
                name: "FK_TransactionEvents_AggregateIdentities_TransactionId_AggregateKind",
                table: "TransactionEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_AggregateIdentities_Id_AggregateKind",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "IncomingPayments");

            migrationBuilder.DropTable(
                name: "AggregateIdentities");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Id_AggregateKind",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_TransactionEvents_TransactionId_AggregateKind",
                table: "TransactionEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransactionEvents_Kind",
                table: "TransactionEvents");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessageJournal_IncomingPaymentId",
                table: "InboundMessageJournal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InboundJournal_Attachment",
                table: "InboundMessageJournal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InboundJournal_Sequence",
                table: "InboundMessageJournal");

            migrationBuilder.DropColumn(
                name: "AggregateKind",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AggregateKind",
                table: "TransactionEvents");

            migrationBuilder.DropColumn(
                name: "IncomingPaymentId",
                table: "InboundMessageJournal");

            migrationBuilder.DropColumn(
                name: "OriginalJson",
                table: "InboundMessageJournal");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboundJournal_Sequence",
                table: "InboundMessageJournal",
                sql: "([Status] = 2 AND [HoldReason] IS NOT NULL AND ([Sequence] IS NULL OR [Sequence] <= 0)) OR ([Status] IN (0, 1) AND [Sequence] IS NOT NULL AND [Sequence] > 0 AND [HoldReason] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionEvents_Transactions_TransactionId",
                table: "TransactionEvents",
                column: "TransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
