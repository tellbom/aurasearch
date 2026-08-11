-- AuraSearch DM8 initial schema.
-- Run once in an empty target schema before starting the API.
-- Business documents are intentionally not seeded; import them through the Index API.

CREATE TABLE "aurasearch_ef_migrations_history" (
    "MigrationId" NVARCHAR2(150) NOT NULL,
    "ProductVersion" NVARCHAR2(32) NOT NULL,
    CONSTRAINT "PK_aurasearch_ef_migrations_history" PRIMARY KEY ("MigrationId")
);

CREATE TABLE "aurasearch_desired_documents" (
    "news_id" NVARCHAR2(256) NOT NULL,
    "SourceId" NVARCHAR2(256) NOT NULL,
    "SourceType" NVARCHAR2(32) NOT NULL,
    "Title" NVARCHAR2(1000) NOT NULL,
    "content_html" CLOB NOT NULL,
    "content_text" CLOB NOT NULL,
    "Publisher" NVARCHAR2(500) NOT NULL,
    "Author" NVARCHAR2(500) NOT NULL,
    "PublishTime" TIMESTAMP NOT NULL,
    "index_version" BIGINT NOT NULL,
    "content_hash" NVARCHAR2(64) NOT NULL,
    "DesiredOperation" NVARCHAR2(16) NOT NULL,
    "EsAppliedVersion" BIGINT NULL,
    "VespaAppliedVersion" BIGINT NULL,
    "EsStatus" NVARCHAR2(16) NOT NULL,
    "VespaStatus" NVARCHAR2(16) NOT NULL,
    "EsRetryCount" INT NOT NULL,
    "VespaRetryCount" INT NOT NULL,
    "EsNextRetryAt" TIMESTAMP NULL,
    "VespaNextRetryAt" TIMESTAMP NULL,
    "EsLastError" NVARCHAR2(1000) NULL,
    "VespaLastError" NVARCHAR2(1000) NULL,
    "CreatedAt" TIMESTAMP NOT NULL,
    "UpdatedAt" TIMESTAMP NOT NULL,
    CONSTRAINT "PK_aurasearch_desired_documents" PRIMARY KEY ("news_id")
);

CREATE TABLE "aurasearch_index_outbox" (
    "news_id" NVARCHAR2(256) NOT NULL,
    "AvailableAt" TIMESTAMP NOT NULL,
    "ClaimedUntil" TIMESTAMP NULL,
    "ClaimToken" NVARCHAR2(64) NULL,
    "CreatedAt" TIMESTAMP NOT NULL,
    "UpdatedAt" TIMESTAMP NOT NULL,
    CONSTRAINT "PK_aurasearch_index_outbox" PRIMARY KEY ("news_id")
);

CREATE TABLE "aurasearch_search_clicks" (
    "Id" BIGINT IDENTITY NOT NULL,
    "SearchTraceId" CHAR(36) NOT NULL,
    "NewsId" NVARCHAR2(256) NOT NULL,
    "ClickPosition" INT NOT NULL,
    "DwellTimeMs" BIGINT NULL,
    "ClickedAt" TIMESTAMP NOT NULL,
    "ExpiresAt" TIMESTAMP NOT NULL,
    CONSTRAINT "PK_aurasearch_search_clicks" PRIMARY KEY ("Id")
);

CREATE TABLE "aurasearch_search_queries" (
    "search_trace_id" CHAR(36) NOT NULL,
    "QueryText" NVARCHAR2(2000) NULL,
    "NormalizedQuery" NVARCHAR2(2000) NOT NULL,
    "FiltersJson" CLOB NOT NULL,
    "SearchTime" TIMESTAMP NOT NULL,
    "SearchMode" NVARCHAR2(32) NOT NULL,
    "ResultVersion" NVARCHAR2(256) NOT NULL,
    "EsLatencyMs" BIGINT NOT NULL,
    "VespaLatencyMs" BIGINT NOT NULL,
    "FusionLatencyMs" BIGINT NOT NULL,
    "TotalLatencyMs" BIGINT NOT NULL,
    "EsHitCount" INT NOT NULL,
    "VespaHitCount" INT NOT NULL,
    "MergedUniqueCount" INT NOT NULL,
    "EsTimeout" BIT NOT NULL,
    "VespaTimeout" BIT NOT NULL,
    "DegradationMode" NVARCHAR2(64) NULL,
    "ParametersJson" CLOB NOT NULL,
    "ExpiresAt" TIMESTAMP NOT NULL,
    CONSTRAINT "PK_aurasearch_search_queries" PRIMARY KEY ("search_trace_id")
);

CREATE TABLE "aurasearch_search_results" (
    "Id" BIGINT IDENTITY NOT NULL,
    "SearchTraceId" CHAR(36) NOT NULL,
    "NewsId" NVARCHAR2(256) NOT NULL,
    "EsRank" INT NULL,
    "EsScore" FLOAT NULL,
    "VespaRank" INT NULL,
    "VespaRelevance" FLOAT NULL,
    "RrfRank" INT NULL,
    "RrfScore" FLOAT NULL,
    "PresentInEs" BIT NOT NULL,
    "PresentInVespa" BIT NOT NULL,
    "Exposed" BIT NOT NULL,
    "ExposedAt" TIMESTAMP NULL,
    CONSTRAINT "PK_aurasearch_search_results" PRIMARY KEY ("Id")
);

CREATE INDEX "ix_aura_doc_type_time"
    ON "aurasearch_desired_documents" ("SourceType", "PublishTime");
CREATE INDEX "ix_aura_doc_updated"
    ON "aurasearch_desired_documents" ("UpdatedAt");
CREATE INDEX "ix_aura_outbox_claim"
    ON "aurasearch_index_outbox" ("AvailableAt", "ClaimedUntil");
CREATE INDEX "ix_aura_click_expiry"
    ON "aurasearch_search_clicks" ("ExpiresAt");
CREATE INDEX "ix_aura_click_trace_news"
    ON "aurasearch_search_clicks" ("SearchTraceId", "NewsId");
CREATE INDEX "ix_aura_query_expiry"
    ON "aurasearch_search_queries" ("ExpiresAt");
CREATE INDEX "ix_aura_query_version_time"
    ON "aurasearch_search_queries" ("ResultVersion", "SearchTime");
CREATE INDEX "ix_aura_result_trace_rank"
    ON "aurasearch_search_results" ("SearchTraceId", "RrfRank");
CREATE UNIQUE INDEX "ux_aura_result_trace_news"
    ON "aurasearch_search_results" ("SearchTraceId", "NewsId");

INSERT INTO "aurasearch_ef_migrations_history" ("MigrationId", "ProductVersion")
VALUES ('20260811015200_InitialDmCreate', '6.0.25');

COMMIT;
