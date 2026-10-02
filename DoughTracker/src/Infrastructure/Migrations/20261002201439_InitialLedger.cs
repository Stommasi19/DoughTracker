using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "financial_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderItemId = table.Column<string>(type: "text", nullable: false),
                    InstitutionId = table.Column<string>(type: "text", nullable: false),
                    InstitutionName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_connections", x => x.Id);
                    table.UniqueConstraint("AK_financial_connections_Id_OwnerId", x => new { x.Id, x.OwnerId });
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Mask = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Subtype = table.Column<string>(type: "text", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    AvailableBalance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                    table.UniqueConstraint("AK_accounts_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_accounts_financial_connections_ConnectionId_OwnerId",
                        columns: x => new { x.ConnectionId, x.OwnerId },
                        principalTable: "financial_connections",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AuthorizedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    MerchantName = table.Column<string>(type: "text", nullable: true),
                    ProviderCategoryId = table.Column<string>(type: "text", nullable: false),
                    ProviderCategoryKey = table.Column<string>(type: "text", nullable: true),
                    CategoryConfidence = table.Column<string>(type: "text", nullable: true),
                    Classification = table.Column<string>(type: "text", nullable: false),
                    Pending = table.Column<bool>(type: "boolean", nullable: false),
                    PendingTransactionId = table.Column<string>(type: "text", nullable: true),
                    PaymentChannel = table.Column<string>(type: "text", nullable: false),
                    ManualCategoryId = table.Column<string>(type: "text", nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transactions_accounts_AccountId_OwnerId",
                        columns: x => new { x.AccountId, x.OwnerId },
                        principalTable: "accounts",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_categories_ManualCategoryId",
                        column: x => x.ManualCategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_categories_ProviderCategoryId",
                        column: x => x.ProviderCategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { "dining", "Food & drink" },
                    { "entertainment", "Entertainment" },
                    { "groceries", "Groceries" },
                    { "health", "Health" },
                    { "housing", "Housing" },
                    { "shopping", "Shopping" },
                    { "transport", "Transport" },
                    { "uncategorized", "Uncategorized" },
                    { "utilities", "Utilities" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ConnectionId_OwnerId",
                table: "accounts",
                columns: new[] { "ConnectionId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_ConnectionId_ProviderAccountId",
                table: "accounts",
                columns: new[] { "ConnectionId", "ProviderAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_OwnerId",
                table: "accounts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_financial_connections_Provider_ProviderItemId",
                table: "financial_connections",
                columns: new[] { "Provider", "ProviderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_AccountId_OwnerId",
                table: "transactions",
                columns: new[] { "AccountId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_AccountId_ProviderTransactionId",
                table: "transactions",
                columns: new[] { "AccountId", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ManualCategoryId",
                table: "transactions",
                column: "ManualCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_OwnerId_Date_Id",
                table: "transactions",
                columns: new[] { "OwnerId", "Date", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ProviderCategoryId",
                table: "transactions",
                column: "ProviderCategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "financial_connections");
        }
    }
}
