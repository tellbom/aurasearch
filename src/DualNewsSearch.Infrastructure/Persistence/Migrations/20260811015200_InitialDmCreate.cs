using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DualNewsSearch.Infrastructure.Persistence.Migrations
{
    public partial class InitialDmCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aurasearch_desired_documents",
                columns: table => new
                {
                    news_id = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    SourceId = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    SourceType = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: false),
                    content_html = table.Column<string>(type: "CLOB", nullable: false),
                    content_text = table.Column<string>(type: "CLOB", nullable: false),
                    Publisher = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "NVARCHAR2(500)", maxLength: 500, nullable: false),
                    PublishTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    index_version = table.Column<long>(type: "BIGINT", nullable: false),
                    content_hash = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: false),
                    DesiredOperation = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    EsAppliedVersion = table.Column<long>(type: "BIGINT", nullable: true),
                    VespaAppliedVersion = table.Column<long>(type: "BIGINT", nullable: true),
                    EsStatus = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    VespaStatus = table.Column<string>(type: "NVARCHAR2(16)", maxLength: 16, nullable: false),
                    EsRetryCount = table.Column<int>(type: "INT", nullable: false),
                    VespaRetryCount = table.Column<int>(type: "INT", nullable: false),
                    EsNextRetryAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    VespaNextRetryAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    EsLastError = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    VespaLastError = table.Column<string>(type: "NVARCHAR2(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aurasearch_desired_documents", x => x.news_id);
                });

            migrationBuilder.CreateTable(
                name: "aurasearch_index_outbox",
                columns: table => new
                {
                    news_id = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ClaimedUntil = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    ClaimToken = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aurasearch_index_outbox", x => x.news_id);
                });

            migrationBuilder.CreateTable(
                name: "aurasearch_search_clicks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "BIGINT", nullable: false)
                        .Annotation("Dm:Identity", "1, 1"),
                    SearchTraceId = table.Column<Guid>(type: "CHAR(36)", nullable: false),
                    NewsId = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    ClickPosition = table.Column<int>(type: "INT", nullable: false),
                    DwellTimeMs = table.Column<long>(type: "BIGINT", nullable: true),
                    ClickedAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aurasearch_search_clicks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "aurasearch_search_queries",
                columns: table => new
                {
                    search_trace_id = table.Column<Guid>(type: "CHAR(36)", nullable: false),
                    QueryText = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: true),
                    NormalizedQuery = table.Column<string>(type: "NVARCHAR2(2000)", maxLength: 2000, nullable: false),
                    FiltersJson = table.Column<string>(type: "CLOB", nullable: false),
                    SearchTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    SearchMode = table.Column<string>(type: "NVARCHAR2(32)", maxLength: 32, nullable: false),
                    ResultVersion = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    EsLatencyMs = table.Column<long>(type: "BIGINT", nullable: false),
                    VespaLatencyMs = table.Column<long>(type: "BIGINT", nullable: false),
                    FusionLatencyMs = table.Column<long>(type: "BIGINT", nullable: false),
                    TotalLatencyMs = table.Column<long>(type: "BIGINT", nullable: false),
                    EsHitCount = table.Column<int>(type: "INT", nullable: false),
                    VespaHitCount = table.Column<int>(type: "INT", nullable: false),
                    MergedUniqueCount = table.Column<int>(type: "INT", nullable: false),
                    EsTimeout = table.Column<bool>(type: "BIT", nullable: false),
                    VespaTimeout = table.Column<bool>(type: "BIT", nullable: false),
                    DegradationMode = table.Column<string>(type: "NVARCHAR2(64)", maxLength: 64, nullable: true),
                    ParametersJson = table.Column<string>(type: "CLOB", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aurasearch_search_queries", x => x.search_trace_id);
                });

            migrationBuilder.CreateTable(
                name: "aurasearch_search_results",
                columns: table => new
                {
                    Id = table.Column<long>(type: "BIGINT", nullable: false)
                        .Annotation("Dm:Identity", "1, 1"),
                    SearchTraceId = table.Column<Guid>(type: "CHAR(36)", nullable: false),
                    NewsId = table.Column<string>(type: "NVARCHAR2(256)", maxLength: 256, nullable: false),
                    EsRank = table.Column<int>(type: "INT", nullable: true),
                    EsScore = table.Column<double>(type: "FLOAT", nullable: true),
                    VespaRank = table.Column<int>(type: "INT", nullable: true),
                    VespaRelevance = table.Column<double>(type: "FLOAT", nullable: true),
                    RrfRank = table.Column<int>(type: "INT", nullable: true),
                    RrfScore = table.Column<double>(type: "FLOAT", nullable: true),
                    PresentInEs = table.Column<bool>(type: "BIT", nullable: false),
                    PresentInVespa = table.Column<bool>(type: "BIT", nullable: false),
                    Exposed = table.Column<bool>(type: "BIT", nullable: false),
                    ExposedAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aurasearch_search_results", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aura_doc_type_time",
                table: "aurasearch_desired_documents",
                columns: new[] { "SourceType", "PublishTime" });

            migrationBuilder.CreateIndex(
                name: "ix_aura_doc_updated",
                table: "aurasearch_desired_documents",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_aura_outbox_claim",
                table: "aurasearch_index_outbox",
                columns: new[] { "AvailableAt", "ClaimedUntil" });

            migrationBuilder.CreateIndex(
                name: "ix_aura_click_expiry",
                table: "aurasearch_search_clicks",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_aura_click_trace_news",
                table: "aurasearch_search_clicks",
                columns: new[] { "SearchTraceId", "NewsId" });

            migrationBuilder.CreateIndex(
                name: "ix_aura_query_expiry",
                table: "aurasearch_search_queries",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_aura_query_version_time",
                table: "aurasearch_search_queries",
                columns: new[] { "ResultVersion", "SearchTime" });

            migrationBuilder.CreateIndex(
                name: "ix_aura_result_trace_rank",
                table: "aurasearch_search_results",
                columns: new[] { "SearchTraceId", "RrfRank" });

            migrationBuilder.CreateIndex(
                name: "ux_aura_result_trace_news",
                table: "aurasearch_search_results",
                columns: new[] { "SearchTraceId", "NewsId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aurasearch_desired_documents");

            migrationBuilder.DropTable(
                name: "aurasearch_index_outbox");

            migrationBuilder.DropTable(
                name: "aurasearch_search_clicks");

            migrationBuilder.DropTable(
                name: "aurasearch_search_queries");

            migrationBuilder.DropTable(
                name: "aurasearch_search_results");
        }
    }
}
