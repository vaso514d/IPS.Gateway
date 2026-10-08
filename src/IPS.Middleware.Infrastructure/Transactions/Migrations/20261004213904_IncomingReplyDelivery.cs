using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class IncomingReplyDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplyCheckpoint",
                table: "InboundMessageJournal",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "IncomingReplies",
                columns: table => new
                {
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvelopeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnsignedXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageKind = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingReplies", x => x.JournalId);
                    table.CheckConstraint("CK_IncomingReplies_Envelope", "ISJSON([EnvelopeJson]) = 1");
                    table.CheckConstraint("CK_IncomingReplies_Message", "([MessageXml] IS NULL AND [MessageKind] IS NULL AND [Status] = 0) OR ([UnsignedXml] IS NOT NULL AND [MessageXml] IS NOT NULL AND [MessageKind] IN (0,1) AND [Status] IN (1,2,3))");
                    table.CheckConstraint("CK_IncomingReplies_Review", "([Status] = 3 AND [ReviewReason] IS NOT NULL) OR ([Status] <> 3 AND [ReviewReason] IS NULL)");
                    table.CheckConstraint("CK_IncomingReplies_Status", "[Status] IN (0,1,2,3)");
                    table.ForeignKey(
                        name: "FK_IncomingReplies_InboundMessageJournal_JournalId",
                        column: x => x.JournalId,
                        principalTable: "InboundMessageJournal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncomingReplyAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    OwnerToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Consumed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingReplyAttempts", x => x.Id);
                    table.CheckConstraint("CK_IncomingReplyAttempts_Completion", "([CompletionJson] IS NULL AND [Consumed] = 0) OR ([CompletionJson] IS NOT NULL AND ISJSON([CompletionJson]) = 1)");
                    table.CheckConstraint("CK_IncomingReplyAttempts_Number", "[Number] > 0");
                    table.ForeignKey(
                        name: "FK_IncomingReplyAttempts_IncomingReplies_JournalId",
                        column: x => x.JournalId,
                        principalTable: "IncomingReplies",
                        principalColumn: "JournalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingReplyAttempts_JournalId_Number",
                table: "IncomingReplyAttempts",
                columns: new[] { "JournalId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncomingReplyAttempts");

            migrationBuilder.DropTable(
                name: "IncomingReplies");

            migrationBuilder.DropColumn(
                name: "ReplyCheckpoint",
                table: "InboundMessageJournal");
        }
    }
}
