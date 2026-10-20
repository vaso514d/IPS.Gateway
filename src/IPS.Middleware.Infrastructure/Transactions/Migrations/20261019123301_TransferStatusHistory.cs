using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class TransferStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentTransactionStatuses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    MessageType = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    IpsInternalCode = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactionStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactionStatuses_AggregateIdentities_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactionStatuses_Direction_AtUtc",
                table: "PaymentTransactionStatuses",
                columns: new[] { "Direction", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactionStatuses_PaymentTransactionId_Id",
                table: "PaymentTransactionStatuses",
                columns: new[] { "PaymentTransactionId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentTransactionStatuses");
        }
    }
}
