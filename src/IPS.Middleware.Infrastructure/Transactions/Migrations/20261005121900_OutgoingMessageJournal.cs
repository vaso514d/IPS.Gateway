using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingMessageJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Submission",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SignedXml",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SubmissionJson",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SubmissionResponseJson",
                table: "Transactions");

            migrationBuilder.CreateTable(
                name: "OutgoingMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    MessageDefinition = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OriginatingMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmissionOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    HeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Failure = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingMessages", x => x.Id);
                    table.UniqueConstraint("AK_OutgoingMessages_Id_PaymentId", x => new { x.Id, x.PaymentId });
                    table.CheckConstraint("CK_OutgoingMessages_Lifecycle", "([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NULL AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL)))");
                    table.ForeignKey(
                        name: "FK_OutgoingMessages_OutgoingMessages_OriginatingMessageId_PaymentId",
                        columns: x => new { x.OriginatingMessageId, x.PaymentId },
                        principalTable: "OutgoingMessages",
                        principalColumns: new[] { "Id", "PaymentId" });
                    table.ForeignKey(
                        name: "FK_OutgoingMessages_Transactions_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR ([MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL )");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_OriginatingMessageId_PaymentId",
                table: "OutgoingMessages",
                columns: new[] { "OriginatingMessageId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_PaymentId_Direction",
                table: "OutgoingMessages",
                columns: new[] { "PaymentId", "Direction" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions");

            migrationBuilder.AddColumn<string>(
                name: "SignedXml",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionJson",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionResponseJson",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL AND [SignedXml] IS NULL) OR ([MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL AND ([SignedXml] IS NULL OR [UnsignedXml] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Submission",
                table: "Transactions",
                sql: "([SubmissionJson] IS NULL AND [SubmissionResponseJson] IS NULL) OR ([SubmissionJson] IS NOT NULL AND ISJSON([SubmissionJson]) = 1 AND [MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL AND [UnsignedXml] IS NOT NULL AND ([SubmissionResponseJson] IS NULL OR ISJSON([SubmissionResponseJson]) = 1))");
        }
    }
}
