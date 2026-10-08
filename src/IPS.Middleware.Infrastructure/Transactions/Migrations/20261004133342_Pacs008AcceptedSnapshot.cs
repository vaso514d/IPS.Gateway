using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class Pacs008AcceptedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptedJson",
                table: "Transactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions",
                sql: "[AcceptedJson] IS NULL OR ([MessageType] = 'pacs.008' AND ISJSON([AcceptedJson]) = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Accepted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AcceptedJson",
                table: "Transactions");
        }
    }
}
