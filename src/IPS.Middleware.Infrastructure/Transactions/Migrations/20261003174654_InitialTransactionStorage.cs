using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransactionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    ClientReference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CurrentStatusAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CurrentSequence = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionHistory",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    IpsInternalCode = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionHistory", x => new { x.TransactionId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_TransactionHistory_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ClientReference",
                table: "Transactions",
                column: "ClientReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionHistory");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
