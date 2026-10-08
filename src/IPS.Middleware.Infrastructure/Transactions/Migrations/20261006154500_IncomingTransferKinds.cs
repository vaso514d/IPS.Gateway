using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class IncomingTransferKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingTransfers_ParticipantBic_EndToEndId_EndToEndIdBytes",
                table: "IncomingTransfers");

            migrationBuilder.DropColumn(
                name: "EndToEndIdBytes",
                table: "IncomingTransfers");

            migrationBuilder.RenameColumn(
                name: "EndToEndId",
                table: "IncomingTransfers",
                newName: "Key");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "IncomingTransfers",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<int>(
                name: "KeyBytes",
                table: "IncomingTransfers",
                type: "int",
                nullable: false,
                computedColumnSql: "DATALENGTH([Key])",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_ParticipantBic_Kind_Key_KeyBytes",
                table: "IncomingTransfers",
                columns: new[] { "ParticipantBic", "Kind", "Key", "KeyBytes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingTransfers_ParticipantBic_Kind_Key_KeyBytes",
                table: "IncomingTransfers");

            migrationBuilder.DropColumn(
                name: "KeyBytes",
                table: "IncomingTransfers");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "IncomingTransfers");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "IncomingTransfers",
                newName: "EndToEndId");

            migrationBuilder.AddColumn<int>(
                name: "EndToEndIdBytes",
                table: "IncomingTransfers",
                type: "int",
                nullable: false,
                computedColumnSql: "DATALENGTH([EndToEndId])",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_ParticipantBic_EndToEndId_EndToEndIdBytes",
                table: "IncomingTransfers",
                columns: new[] { "ParticipantBic", "EndToEndId", "EndToEndIdBytes" },
                unique: true);
        }
    }
}
