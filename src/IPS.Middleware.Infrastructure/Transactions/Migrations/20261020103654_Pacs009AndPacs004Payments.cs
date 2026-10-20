using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class Pacs009AndPacs004Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pacs004Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClientReference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    CoreReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReturnId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    ReturnReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InstructedAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    DebtorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    DebtorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreditorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    CreditorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    OriginalMessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalMessageNameId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalInstructionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalEndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalTransactionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalUetr = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdditionalInformation = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacs004Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pacs004Payments_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pacs009Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClientReference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    CoreReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InstructionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    EndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    TransactionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Uetr = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DebtorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    DebtorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreditorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CreditorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CategoryPurpose = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacs009Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pacs009Payments_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs004Payments_ClientReference",
                table: "Pacs004Payments",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs004Payments_Direction_CreatedAtUtc",
                table: "Pacs004Payments",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs004Payments_OriginalEndToEndId",
                table: "Pacs004Payments",
                column: "OriginalEndToEndId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs004Payments_Status_StatusAtUtc",
                table: "Pacs004Payments",
                columns: new[] { "Status", "StatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs009Payments_ClientReference",
                table: "Pacs009Payments",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs009Payments_Direction_CreatedAtUtc",
                table: "Pacs009Payments",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs009Payments_EndToEndId",
                table: "Pacs009Payments",
                column: "EndToEndId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs009Payments_Status_StatusAtUtc",
                table: "Pacs009Payments",
                columns: new[] { "Status", "StatusAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pacs004Payments");

            migrationBuilder.DropTable(
                name: "Pacs009Payments");
        }
    }
}
