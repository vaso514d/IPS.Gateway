using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class IncomingCbsProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CheckpointVersion",
                table: "IncomingPayments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ContextJson",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CoreDescription",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoreInternalErrorCode",
                table: "IncomingPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CoreProcessedAtUtc",
                table: "IncomingPayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoreReasonCode",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoreReference",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoreStatus",
                table: "IncomingPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FollowUp",
                table: "IncomingPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FollowUpAtUtc",
                table: "IncomingPayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IpsAccepted",
                table: "IncomingPayments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IpsDecidedAtUtc",
                table: "IncomingPayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpsDescription",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpsReasonCode",
                table: "IncomingPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncomingCoreCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OwnerToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Consumed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingCoreCalls", x => x.Id);
                    table.CheckConstraint("CK_IncomingCoreCalls_Kind", "[Kind] IN (0, 1)");
                    table.CheckConstraint("CK_IncomingCoreCalls_Result", "([CompletionJson] IS NULL AND [Consumed] = 0) OR ([CompletionJson] IS NOT NULL AND ISJSON([CompletionJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_IncomingCoreCalls_IncomingPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "IncomingPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingPayments_Context",
                table: "IncomingPayments",
                sql: "ISJSON([ContextJson]) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingCoreCalls_PaymentId",
                table: "IncomingCoreCalls",
                column: "PaymentId",
                unique: true,
                filter: "[Kind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingCoreCalls_PaymentId_Number",
                table: "IncomingCoreCalls",
                columns: new[] { "PaymentId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncomingCoreCalls");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingPayments_Context",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CheckpointVersion",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "ContextJson",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CoreDescription",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CoreInternalErrorCode",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CoreProcessedAtUtc",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CoreReasonCode",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CoreReference",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "CoreStatus",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "FollowUp",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "FollowUpAtUtc",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "IpsAccepted",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "IpsDecidedAtUtc",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "IpsDescription",
                table: "IncomingPayments");

            migrationBuilder.DropColumn(
                name: "IpsReasonCode",
                table: "IncomingPayments");
        }
    }
}
