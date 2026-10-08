using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingCamt029 : Migration
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions",
                sql: "[AcceptedJson] IS NULL OR ([MessageType] IN ('pacs.008', 'pacs.009', 'pacs.004', 'camt.056', 'camt.029') AND ISJSON([AcceptedJson]) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR ([MessageType] IN ('pacs.008', 'pacs.009', 'pacs.004', 'camt.056', 'camt.029') AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages",
                sql: "([InvestigationId] IS NULL OR [ResendId] IS NULL) AND (([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [ResendId] IS NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11', 'pacs.004.001.13', 'camt.056.001.11', 'camt.029.001.13') AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL) OR ([ResendId] IS NOT NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11', 'pacs.004.001.13', 'camt.056.001.11', 'camt.029.001.13') AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL))))");
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions",
                sql: "[AcceptedJson] IS NULL OR ([MessageType] IN ('pacs.008', 'pacs.009', 'pacs.004', 'camt.056') AND ISJSON([AcceptedJson]) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL) OR ([MessageType] IN ('pacs.008', 'pacs.009', 'pacs.004', 'camt.056') AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Lifecycle",
                table: "OutgoingMessages",
                sql: "([InvestigationId] IS NULL OR [ResendId] IS NULL) AND (([Direction] = 0 AND [MessageDefinition] IS NOT NULL AND (([InvestigationId] IS NULL AND [ResendId] IS NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11', 'pacs.004.001.13', 'camt.056.001.11') AND [OriginatingMessageId] IS NULL) OR ([InvestigationId] IS NOT NULL AND [MessageDefinition] = 'pacs.028.001.06' AND [OriginatingMessageId] IS NOT NULL) OR ([ResendId] IS NOT NULL AND [MessageDefinition] IN ('pacs.008.001.12', 'pacs.009.001.11', 'pacs.004.001.13', 'camt.056.001.11') AND [OriginatingMessageId] IS NOT NULL)) AND [Disposition] IS NOT NULL AND [Disposition] IN (0,1) AND [HttpStatusCode] IS NULL AND [HeadersJson] IS NULL AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL AND (([Status] = 0 AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [SubmissionOwner] IS NOT NULL))) OR ([Direction] = 1 AND [OriginatingMessageId] IS NOT NULL AND [Disposition] IS NULL AND [StartedAtUtc] IS NULL AND [SubmissionOwner] IS NULL AND [HttpStatusCode] IS NOT NULL AND [HttpStatusCode] BETWEEN 100 AND 599 AND [HeadersJson] IS NOT NULL AND ISJSON([HeadersJson]) = 1 AND (([Status] = 2 AND [ProcessedAtUtc] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 4 AND [ProcessedAtUtc] IS NOT NULL AND [Failure] IS NOT NULL))))");
        }
    }
}
