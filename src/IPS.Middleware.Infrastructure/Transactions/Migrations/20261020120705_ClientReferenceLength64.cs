using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class ClientReferenceLength64 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pain002StatusReports_ClientReference",
                table: "Pain002StatusReports");

            migrationBuilder.DropIndex(
                name: "IX_Pain001Initiations_ClientReference",
                table: "Pain001Initiations");

            migrationBuilder.DropIndex(
                name: "IX_Pacs009Payments_ClientReference",
                table: "Pacs009Payments");

            migrationBuilder.DropIndex(
                name: "IX_Pacs008Payments_ClientReference",
                table: "Pacs008Payments");

            migrationBuilder.DropIndex(
                name: "IX_Pacs004Payments_ClientReference",
                table: "Pacs004Payments");

            migrationBuilder.DropIndex(
                name: "IX_Pacs002StatusReports_ClientReference",
                table: "Pacs002StatusReports");

            migrationBuilder.DropIndex(
                name: "IX_Camt056Recalls_ClientReference",
                table: "Camt056Recalls");

            migrationBuilder.DropIndex(
                name: "IX_Camt055Cancellations_ClientReference",
                table: "Camt055Cancellations");

            migrationBuilder.DropIndex(
                name: "IX_Camt029Resolutions_ClientReference",
                table: "Camt029Resolutions");

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Transactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pain002StatusReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pain001Initiations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs009Payments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs008Payments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs004Payments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs002StatusReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Camt056Recalls",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Camt055Cancellations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Camt029Resolutions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(35)",
                oldMaxLength: 35,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pain002StatusReports_ClientReference",
                table: "Pain002StatusReports",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pain001Initiations_ClientReference",
                table: "Pain001Initiations",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs009Payments_ClientReference",
                table: "Pacs009Payments",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs008Payments_ClientReference",
                table: "Pacs008Payments",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs004Payments_ClientReference",
                table: "Pacs004Payments",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs002StatusReports_ClientReference",
                table: "Pacs002StatusReports",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt056Recalls_ClientReference",
                table: "Camt056Recalls",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt055Cancellations_ClientReference",
                table: "Camt055Cancellations",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt029Resolutions_ClientReference",
                table: "Camt029Resolutions",
                column: "ClientReference",
                unique: true,
                filter: "[ClientReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pain002StatusReports_ClientReference",
                table: "Pain002StatusReports");

            migrationBuilder.DropIndex(
                name: "IX_Pain001Initiations_ClientReference",
                table: "Pain001Initiations");

            migrationBuilder.DropIndex(
                name: "IX_Pacs009Payments_ClientReference",
                table: "Pacs009Payments");

            migrationBuilder.DropIndex(
                name: "IX_Pacs008Payments_ClientReference",
                table: "Pacs008Payments");

            migrationBuilder.DropIndex(
                name: "IX_Pacs004Payments_ClientReference",
                table: "Pacs004Payments");

            migrationBuilder.DropIndex(
                name: "IX_Pacs002StatusReports_ClientReference",
                table: "Pacs002StatusReports");

            migrationBuilder.DropIndex(
                name: "IX_Camt056Recalls_ClientReference",
                table: "Camt056Recalls");

            migrationBuilder.DropIndex(
                name: "IX_Camt055Cancellations_ClientReference",
                table: "Camt055Cancellations");

            migrationBuilder.DropIndex(
                name: "IX_Camt029Resolutions_ClientReference",
                table: "Camt029Resolutions");

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Transactions",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pain002StatusReports",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pain001Initiations",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs009Payments",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs008Payments",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs004Payments",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Pacs002StatusReports",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Camt056Recalls",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Camt055Cancellations",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientReference",
                table: "Camt029Resolutions",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pain002StatusReports_ClientReference",
                table: "Pain002StatusReports",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pain001Initiations_ClientReference",
                table: "Pain001Initiations",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs009Payments_ClientReference",
                table: "Pacs009Payments",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs008Payments_ClientReference",
                table: "Pacs008Payments",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs004Payments_ClientReference",
                table: "Pacs004Payments",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs002StatusReports_ClientReference",
                table: "Pacs002StatusReports",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt056Recalls_ClientReference",
                table: "Camt056Recalls",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt055Cancellations_ClientReference",
                table: "Camt055Cancellations",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt029Resolutions_ClientReference",
                table: "Camt029Resolutions",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");
        }
    }
}
