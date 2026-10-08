using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingPacs009 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingResends_InvestigationId",
                table: "OutgoingResends");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingResends_State",
                table: "OutgoingResends");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvestigationId",
                table: "OutgoingResends",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadlineUtc",
                table: "OutgoingResends",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions",
                sql: "[AcceptedJson] IS NULL OR ([MessageType] IN ('pacs.008', 'pacs.009') AND ISJSON([AcceptedJson]) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR ([MessageType] IN ('pacs.008', 'pacs.009') AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingResends_InvestigationId",
                table: "OutgoingResends",
                column: "InvestigationId",
                unique: true,
                filter: "[InvestigationId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingResends_State",
                table: "OutgoingResends",
                sql: "[Number] > 0 AND (([InvestigationId] IS NOT NULL AND [DeadlineUtc] IS NULL) OR ([InvestigationId] IS NULL AND [DeadlineUtc] IS NOT NULL)) AND (([Outcome] IS NULL AND [CompletedAtUtc] IS NULL AND [DetailsJson] IS NULL AND [TransportFailure] IS NULL) OR ([Outcome] IS NOT NULL AND [Outcome] BETWEEN 0 AND 2 AND [CompletedAtUtc] IS NOT NULL AND [DetailsJson] IS NOT NULL AND ISJSON([DetailsJson]) = 1))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages",
                sql: "([InvestigationId] IS NULL OR [ResendId] IS NULL) AND (([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [ResendId] IS NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11') AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL) OR ([ResendId] IS NOT NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11') AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingResends_InvestigationId",
                table: "OutgoingResends");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingResends_State",
                table: "OutgoingResends");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "DeadlineUtc",
                table: "OutgoingResends");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvestigationId",
                table: "OutgoingResends",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions",
                sql: "[AcceptedJson] IS NULL OR ([MessageType] = 'pacs.008' AND ISJSON([AcceptedJson]) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR ([MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL )");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingResends_InvestigationId",
                table: "OutgoingResends",
                column: "InvestigationId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingResends_State",
                table: "OutgoingResends",
                sql: "[Number] > 0 AND (([Outcome] IS NULL AND [CompletedAtUtc] IS NULL AND [DetailsJson] IS NULL AND [TransportFailure] IS NULL) OR ([Outcome] IS NOT NULL AND [Outcome] BETWEEN 0 AND 2 AND [CompletedAtUtc] IS NOT NULL AND [DetailsJson] IS NOT NULL AND ISJSON([DetailsJson]) = 1))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages",
                sql: "([InvestigationId] IS NULL OR [ResendId] IS NULL) AND (([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [ResendId] IS NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL) OR ([ResendId] IS NOT NULL AND [MessageDefinition] = 'pacs.008.001.12' AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL))))");
        }
    }
}
