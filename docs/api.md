# API 文档

所有业务接口使用 `/api/v1` 前缀。开发环境 Swagger 位于 `/swagger`。

## 索引

- `PUT /api/v1/index/documents/{newsId}`：异步 upsert；body 包含 `sourceId/sourceType/title/contentHtml/publisher/author/publishTime/indexVersion`。
- `DELETE /api/v1/index/documents/{newsId}?indexVersion=...`：写入 tombstone。
- `POST /api/v1/index/documents/batch`：逐条返回 Accepted/NoOp/Stale/Invalid；一条失败不回滚整批。

索引接口只提交 SQLite 期望状态与 Outbox，返回 `202 Accepted`，不等待搜索引擎。

## 搜索

- `POST /api/v1/search`：支持 query、sourceTypes、publishTimeFrom/To、publisher、author、page、pageSize。
- `GET /api/v1/suggest?q=...&size=10`：只访问 Elasticsearch。

搜索响应包含 `searchTraceId/searchMode/degraded/degradationMode/maxDepthReached/page/pageSize/results`。普通响应不暴露原始引擎分数；SQLite 诊断记录保留 esRank/esScore/vespaRank/vespaRelevance/rrfRank/rrfScore。

## 埋点

- `POST /api/v1/telemetry/impressions`
- `POST /api/v1/telemetry/clicks`

两者都验证 trace/result 对；伪造或过期记录返回 400。

## 运维

- `GET /health/live`：只表示进程存活；`GET /health/ready`：检查当前搜索模式所需引擎连通性，不执行 count/hash 一致性检查。
- `GET /api/v1/search-health`
- `POST /api/v1/search-health/mode`：body 必须包含 mode/operator/reason；Rrf/VespaOnly 先过 readiness。
- `POST /api/v1/operations/retry-dead`
- `POST /api/v1/operations/reindex`
- `GET /api/v1/operations/indexing-snapshot`
- `GET /api/v1/operations/consistency?hashSampleSize=100`
- `POST /api/v1/diagnostics/{elasticsearch|vespa}/query`
- `GET /api/v1/telemetry/metrics?resultVersion=...&days=7`
