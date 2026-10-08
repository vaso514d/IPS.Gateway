using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingResends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_PaymentId_Direction",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages");

            migrationBuilder.AddColumn<Guid>(
                name: "ResendId",
                table: "OutgoingMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutgoingResends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransportFailure = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingResends", x => x.Id);
                    table.UniqueConstraint("AK_OutgoingResends_Id_PaymentId", x => new { x.Id, x.PaymentId });
                    table.CheckConstraint("CK_OutgoingResends_State", "[Number] > 0 AND (([Outcome] IS NULL AND [CompletedAtUtc] IS NULL AND [DetailsJson] IS NULL AND [TransportFailure] IS NULL) OR ([Outcome] IS NOT NULL AND [Outcome] BETWEEN 0 AND 2 AND [CompletedAtUtc] IS NOT NULL AND [DetailsJson] IS NOT NULL AND ISJSON([DetailsJson]) = 1))");
                    table.ForeignKey(
                        name: "FK_OutgoingResends_OutgoingInvestigations_InvestigationId_PaymentId",
                        columns: x => new { x.InvestigationId, x.PaymentId },
                        principalTable: "OutgoingInvestigations",
                        principalColumns: new[] { "Id", "PaymentId" });
                    table.ForeignKey(
                        name: "FK_OutgoingResends_Transactions_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_PaymentId_Direction",
                table: "OutgoingMessages",
                columns: new[] { "PaymentId", "Direction" },
                unique: true,
                filter: "[InvestigationId] IS NULL AND [ResendId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_ResendId_Direction",
                table: "OutgoingMessages",
                columns: new[] { "ResendId", "Direction" },
                unique: true,
                filter: "[ResendId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_ResendId_PaymentId",
                table: "OutgoingMessages",
                columns: new[] { "ResendId", "PaymentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages",
                sql: "([InvestigationId] IS NULL OR [ResendId] IS NULL) AND (([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [ResendId] IS NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL) OR ([ResendId] IS NOT NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL))))");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingResends_InvestigationId",
                table: "OutgoingResends",
                column: "InvestigationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingResends_InvestigationId_PaymentId",
                table: "OutgoingResends",
                columns: new[] { "InvestigationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingResends_PaymentId_Number",
                table: "OutgoingResends",
                columns: new[] { "PaymentId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OutgoingMessages_OutgoingResends_ResendId_PaymentId",
                table: "OutgoingMessages",
                columns: new[] { "ResendId", "PaymentId" },
                principalTable: "OutgoingResends",
                principalColumns: new[] { "Id", "PaymentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OutgoingMessages_OutgoingResends_ResendId_PaymentId",
                table: "OutgoingMessages");

            migrationBuilder.DropTable(
                name: "OutgoingResends");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_PaymentId_Direction",
                table: "OutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_ResendId_Direction",
                table: "OutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_ResendId_PaymentId",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "ResendId",
                table: "OutgoingMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_PaymentId_Direction",
                table: "OutgoingMessages",
                columns: new[] { "PaymentId", "Direction" },
                unique: true,
                filter: "[InvestigationId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages",
                sql: "([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL)))");
        }
    }
}
