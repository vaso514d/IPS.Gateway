using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class Pacs008SubmissionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "CK_Transactions_Submission",
                table: "Transactions",
                sql: "([SubmissionJson] IS NULL AND [SubmissionResponseJson] IS NULL) OR ([SubmissionJson] IS NOT NULL AND ISJSON([SubmissionJson]) = 1 AND [MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL AND [UnsignedXml] IS NOT NULL AND ([SubmissionResponseJson] IS NULL OR ISJSON([SubmissionResponseJson]) = 1))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Submission",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SubmissionJson",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SubmissionResponseJson",
                table: "Transactions");
        }
    }
}
