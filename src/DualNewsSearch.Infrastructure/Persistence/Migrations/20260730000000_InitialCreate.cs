using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace DualNewsSearch.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SearchDbContext))]
[Migration("20260730000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "desired_documents",
            columns: table => new
            {
                news_id = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                SourceId = table.Column<string>(type: "TEXT", nullable: false),
                SourceType = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                content_html = table.Column<string>(type: "TEXT", nullable: false),
                content_text = table.Column<string>(type: "TEXT", nullable: false),
                Publisher = table.Column<string>(type: "TEXT", nullable: false),
                Author = table.Column<string>(type: "TEXT", nullable: false),
                PublishTime = table.Column<long>(type: "INTEGER", nullable: false),
                index_version = table.Column<long>(type: "INTEGER", nullable: false),
                content_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DesiredOperation = table.Column<string>(type: "TEXT", nullable: false),
                EsAppliedVersion = table.Column<long>(type: "INTEGER", nullable: true),
                VespaAppliedVersion = table.Column<long>(type: "INTEGER", nullable: true),
                EsStatus = table.Column<string>(type: "TEXT", nullable: false),
                VespaStatus = table.Column<string>(type: "TEXT", nullable: false),
                EsRetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                VespaRetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                EsNextRetryAt = table.Column<long>(type: "INTEGER", nullable: true),
                VespaNextRetryAt = table.Column<long>(type: "INTEGER", nullable: true),
                EsLastError = table.Column<string>(type: "TEXT", nullable: true),
                VespaLastError = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_desired_documents", x => x.news_id));

        migrationBuilder.CreateTable(
            name: "index_outbox",
            columns: table => new
            {
                news_id = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                AvailableAt = table.Column<long>(type: "INTEGER", nullable: false),
                ClaimedUntil = table.Column<long>(type: "INTEGER", nullable: true),
                ClaimToken = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_index_outbox", x => x.news_id));

        migrationBuilder.CreateTable(
            name: "search_queries",
            columns: table => new
            {
                search_trace_id = table.Column<Guid>(type: "TEXT", nullable: false),
                QueryText = table.Column<string>(type: "TEXT", nullable: true),
                NormalizedQuery = table.Column<string>(type: "TEXT", nullable: false),
                FiltersJson = table.Column<string>(type: "TEXT", nullable: false),
                SearchTime = table.Column<long>(type: "INTEGER", nullable: false),
                SearchMode = table.Column<string>(type: "TEXT", nullable: false),
                ResultVersion = table.Column<string>(type: "TEXT", nullable: false),
                EsLatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                VespaLatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                FusionLatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                TotalLatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                EsHitCount = table.Column<int>(type: "INTEGER", nullable: false),
                VespaHitCount = table.Column<int>(type: "INTEGER", nullable: false),
                MergedUniqueCount = table.Column<int>(type: "INTEGER", nullable: false),
                EsTimeout = table.Column<bool>(type: "INTEGER", nullable: false),
                VespaTimeout = table.Column<bool>(type: "INTEGER", nullable: false),
                DegradationMode = table.Column<string>(type: "TEXT", nullable: true),
                ParametersJson = table.Column<string>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_search_queries", x => x.search_trace_id));

        migrationBuilder.CreateTable(
            name: "search_results",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SearchTraceId = table.Column<Guid>(type: "TEXT", nullable: false),
                NewsId = table.Column<string>(type: "TEXT", nullable: false),
                EsRank = table.Column<int>(type: "INTEGER", nullable: true),
                EsScore = table.Column<double>(type: "REAL", nullable: true),
                VespaRank = table.Column<int>(type: "INTEGER", nullable: true),
                VespaRelevance = table.Column<double>(type: "REAL", nullable: true),
                RrfRank = table.Column<int>(type: "INTEGER", nullable: true),
                RrfScore = table.Column<double>(type: "REAL", nullable: true),
                PresentInEs = table.Column<bool>(type: "INTEGER", nullable: false),
                PresentInVespa = table.Column<bool>(type: "INTEGER", nullable: false),
                Exposed = table.Column<bool>(type: "INTEGER", nullable: false),
                ExposedAt = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_search_results", x => x.Id));

        migrationBuilder.CreateTable(
            name: "search_clicks",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SearchTraceId = table.Column<Guid>(type: "TEXT", nullable: false),
                NewsId = table.Column<string>(type: "TEXT", nullable: false),
                ClickPosition = table.Column<int>(type: "INTEGER", nullable: false),
                DwellTimeMs = table.Column<long>(type: "INTEGER", nullable: true),
                ClickedAt = table.Column<long>(type: "INTEGER", nullable: false),
                ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_search_clicks", x => x.Id));

        migrationBuilder.CreateIndex("IX_desired_documents_SourceType_PublishTime", "desired_documents", new[] { "SourceType", "PublishTime" });
        migrationBuilder.CreateIndex("IX_desired_documents_UpdatedAt", "desired_documents", "UpdatedAt");
        migrationBuilder.CreateIndex("IX_index_outbox_AvailableAt_ClaimedUntil", "index_outbox", new[] { "AvailableAt", "ClaimedUntil" });
        migrationBuilder.CreateIndex("IX_search_clicks_ExpiresAt", "search_clicks", "ExpiresAt");
        migrationBuilder.CreateIndex("IX_search_clicks_SearchTraceId_NewsId", "search_clicks", new[] { "SearchTraceId", "NewsId" });
        migrationBuilder.CreateIndex("IX_search_queries_ExpiresAt", "search_queries", "ExpiresAt");
        migrationBuilder.CreateIndex("IX_search_queries_ResultVersion_SearchTime", "search_queries", new[] { "ResultVersion", "SearchTime" });
        migrationBuilder.CreateIndex("IX_search_results_SearchTraceId_NewsId", "search_results", new[] { "SearchTraceId", "NewsId" }, unique: true);
        migrationBuilder.CreateIndex("IX_search_results_SearchTraceId_RrfRank", "search_results", new[] { "SearchTraceId", "RrfRank" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("desired_documents");
        migrationBuilder.DropTable("index_outbox");
        migrationBuilder.DropTable("search_clicks");
        migrationBuilder.DropTable("search_queries");
        migrationBuilder.DropTable("search_results");
    }
}
