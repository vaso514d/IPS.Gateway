using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class InboundMessageJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundMessageJournal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantBic = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: true),
                    MessageType = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    RawXml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PossibleDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HoldReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NextActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DuplicateCount = table.Column<long>(type: "bigint", nullable: false),
                    LastDuplicateAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundMessageJournal", x => x.Id);
                    table.CheckConstraint("CK_InboundJournal_Claim", "([ClaimToken] IS NULL AND [ClaimExpiresAtUtc] IS NULL) OR ([ClaimToken] IS NOT NULL AND [ClaimExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_InboundJournal_Duplicates", "[DuplicateCount] >= 0");
                    table.CheckConstraint("CK_InboundJournal_Scheduling", "([Status] = 0 AND [NextActionAtUtc] IS NOT NULL) OR ([Status] <> 0 AND [NextActionAtUtc] IS NULL AND [ClaimToken] IS NULL)");
                    table.CheckConstraint("CK_InboundJournal_Sequence", "([Status] = 2 AND [HoldReason] IS NOT NULL AND ([Sequence] IS NULL OR [Sequence] <= 0)) OR ([Status] IN (0, 1) AND [Sequence] IS NOT NULL AND [Sequence] > 0 AND [HoldReason] IS NULL)");
                    table.CheckConstraint("CK_InboundJournal_Status", "[Status] IN (0, 1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageJournal_ParticipantBic_Sequence",
                table: "InboundMessageJournal",
                columns: new[] { "ParticipantBic", "Sequence" },
                unique: true,
                filter: "[Sequence] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageJournal_Status_NextActionAtUtc_ReceivedAtUtc_Id",
                table: "InboundMessageJournal",
                columns: new[] { "Status", "NextActionAtUtc", "ReceivedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundMessageJournal");
        }
    }
}
