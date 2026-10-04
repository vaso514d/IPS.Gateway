using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class Pacs008PreparationStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageId",
                table: "Transactions",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtocolTransactionId",
                table: "Transactions",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedXml",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnsignedXml",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_MessageId",
                table: "Transactions",
                column: "MessageId",
                unique: true,
                filter: "[MessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProtocolTransactionId",
                table: "Transactions",
                column: "ProtocolTransactionId",
                unique: true,
                filter: "[ProtocolTransactionId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions",
                sql: "([MessageId] IS NULL AND [ProtocolTransactionId] IS NULL AND [UnsignedXml] IS NULL AND [SignedXml] IS NULL) OR ([MessageType] = 'pacs.008' AND [MessageId] IS NOT NULL AND [ProtocolTransactionId] IS NOT NULL AND ([SignedXml] IS NULL OR [UnsignedXml] IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_MessageId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ProtocolTransactionId",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Preparation",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ProtocolTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SignedXml",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UnsignedXml",
                table: "Transactions");
        }
    }
}
