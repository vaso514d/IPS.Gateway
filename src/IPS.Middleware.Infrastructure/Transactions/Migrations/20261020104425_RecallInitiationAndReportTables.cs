using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.Middleware.Infrastructure.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class RecallInitiationAndReportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Camt029Resolutions",
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
                    CancellationStatusId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalMessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalEndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalTransactionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    ResolutionReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    AdditionalInformation = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camt029Resolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Camt029Resolutions_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Camt055Cancellations",
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
                    CancellationId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    PaymentCancellationId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    AssignerBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    AssigneeBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    OriginalMessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalPaymentInformationId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalInstructionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalEndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    RequestedExecutionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CancellationReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginatorBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CreditorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    CreditorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camt055Cancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Camt055Cancellations_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Camt056Recalls",
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
                    RecallId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalMessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalEndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalTransactionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalAmount = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    OriginalCurrency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    OriginalSettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RecallReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    DebtorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    DebtorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    DebtorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CreditorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    CreditorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreditorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camt056Recalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Camt056Recalls_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pacs002StatusReports",
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
                    OriginalMessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalEndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalTransactionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    TransactionStatusCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    ReportReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    AdditionalInformation = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacs002StatusReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pain001Initiations",
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
                    PaymentInformationId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    InstructionId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    EndToEndId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    RequestedExecutionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InitiatingPartyBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    DebtorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    DebtorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    DebtorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CreditorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    CreditorAccount = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreditorAgentBic = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CategoryPurpose = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    RemittanceInformation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pain001Initiations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pain001Initiations_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pain002StatusReports",
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
                    OriginalMessageId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    OriginalPaymentInformationId = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    ReportReasonCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    AdditionalInformation = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OriginatorName = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pain002StatusReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pain002StatusReports_AggregateIdentities_Id",
                        column: x => x.Id,
                        principalTable: "AggregateIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Camt029Resolutions_ClientReference",
                table: "Camt029Resolutions",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt029Resolutions_Direction_CreatedAtUtc",
                table: "Camt029Resolutions",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Camt029Resolutions_OriginalEndToEndId",
                table: "Camt029Resolutions",
                column: "OriginalEndToEndId");

            migrationBuilder.CreateIndex(
                name: "IX_Camt029Resolutions_Status_StatusAtUtc",
                table: "Camt029Resolutions",
                columns: new[] { "Status", "StatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Camt055Cancellations_ClientReference",
                table: "Camt055Cancellations",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt055Cancellations_Direction_CreatedAtUtc",
                table: "Camt055Cancellations",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Camt055Cancellations_OriginalPaymentInformationId",
                table: "Camt055Cancellations",
                column: "OriginalPaymentInformationId");

            migrationBuilder.CreateIndex(
                name: "IX_Camt055Cancellations_Status_StatusAtUtc",
                table: "Camt055Cancellations",
                columns: new[] { "Status", "StatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Camt056Recalls_ClientReference",
                table: "Camt056Recalls",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Camt056Recalls_Direction_CreatedAtUtc",
                table: "Camt056Recalls",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Camt056Recalls_OriginalEndToEndId",
                table: "Camt056Recalls",
                column: "OriginalEndToEndId");

            migrationBuilder.CreateIndex(
                name: "IX_Camt056Recalls_Status_StatusAtUtc",
                table: "Camt056Recalls",
                columns: new[] { "Status", "StatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs002StatusReports_ClientReference",
                table: "Pacs002StatusReports",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs002StatusReports_Direction_CreatedAtUtc",
                table: "Pacs002StatusReports",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacs002StatusReports_OriginalEndToEndId",
                table: "Pacs002StatusReports",
                column: "OriginalEndToEndId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacs002StatusReports_Status_StatusAtUtc",
                table: "Pacs002StatusReports",
                columns: new[] { "Status", "StatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pain001Initiations_ClientReference",
                table: "Pain001Initiations",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pain001Initiations_Direction_CreatedAtUtc",
                table: "Pain001Initiations",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pain001Initiations_PaymentInformationId",
                table: "Pain001Initiations",
                column: "PaymentInformationId");

            migrationBuilder.CreateIndex(
                name: "IX_Pain001Initiations_Status_StatusAtUtc",
                table: "Pain001Initiations",
                columns: new[] { "Status", "StatusAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pain002StatusReports_ClientReference",
                table: "Pain002StatusReports",
                column: "ClientReference",
                filter: "[ClientReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pain002StatusReports_Direction_CreatedAtUtc",
                table: "Pain002StatusReports",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pain002StatusReports_OriginalPaymentInformationId",
                table: "Pain002StatusReports",
                column: "OriginalPaymentInformationId");

            migrationBuilder.CreateIndex(
                name: "IX_Pain002StatusReports_Status_StatusAtUtc",
                table: "Pain002StatusReports",
                columns: new[] { "Status", "StatusAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Camt029Resolutions");

            migrationBuilder.DropTable(
                name: "Camt055Cancellations");

            migrationBuilder.DropTable(
                name: "Camt056Recalls");

            migrationBuilder.DropTable(
                name: "Pacs002StatusReports");

            migrationBuilder.DropTable(
                name: "Pain001Initiations");

            migrationBuilder.DropTable(
                name: "Pain002StatusReports");
        }
    }
}
