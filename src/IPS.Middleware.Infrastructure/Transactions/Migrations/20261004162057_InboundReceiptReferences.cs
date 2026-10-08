using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class InboundReceiptReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InboundJournal_Attachment",
                table: "InboundMessageJournal");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboundJournal_Attachment",
                table: "InboundMessageJournal",
                sql: "([IncomingPaymentId] IS NULL AND [OriginalJson] IS NULL) OR ([OriginalJson] IS NOT NULL AND ISJSON([OriginalJson]) = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InboundJournal_Attachment",
                table: "InboundMessageJournal");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboundJournal_Attachment",
                table: "InboundMessageJournal",
                sql: "([IncomingPaymentId] IS NULL AND [OriginalJson] IS NULL) OR ([IncomingPaymentId] IS NOT NULL AND ISJSON([OriginalJson]) = 1)");
        }
    }
}
