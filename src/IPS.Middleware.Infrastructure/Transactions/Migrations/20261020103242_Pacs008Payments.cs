using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class Pacs008Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pacs008Payments",
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
                    AcceptanceDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DebtorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    DebtorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    DebtorIdentification = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DebtorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CreditorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    CreditorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreditorIdentification = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreditorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CategoryPurpose = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    RemittanceInformation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacs008Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pacs008Payments_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs008Payments_ClientReference",
                table: "Pacs008Payments",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs008Payments_Direction_CreatedAtUtc",
                table: "Pacs008Payments",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs008Payments_EndToEndId",
                table: "Pacs008Payments",
                column: "EndToEndId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs008Payments_Status_StatusAtUtc",
                table: "Pacs008Payments",
                columns: new[] { "Status", "StatusAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pacs008Payments");
        }
    }
}
