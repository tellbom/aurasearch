# API 文档

所有业务接口使用 `/api/v1` 前缀。开发环境 Swagger 位于 `/swagger`。

## 索引

- `PUT /api/v1/index/documents/{newsId}`：异步 upsert；body 包含 `sourceId/sourceType/title/contentHtml/cover/publisher/author/publishTime/indexVersion`，其中新闻 `cover` 为可选封面 URL。
- `DELETE /api/v1/index/documents/{newsId}?indexVersion=...`：写入 tombstone。
- `POST /api/v1/index/documents/batch`：逐条返回 Accepted/NoOp/Stale/Invalid；一条失败不回滚整批。

索引接口只提交 DM 期望状态与 Outbox，返回 `202 Accepted`，不等待搜索引擎。

## 搜索

- `POST /api/v1/search`：支持 query、sourceTypes、publishTimeFrom/To、publisher、author、page、pageSize。
- `POST /api/v1/search/day-groups`：面向新闻公告组件的按日聚合搜索；`pageSize` 表示每页自然日数，默认 5。
- `GET /api/v1/suggest?q=...&size=10`：只访问 Elasticsearch。

搜索响应包含 `searchTraceId/searchMode/degraded/degradationMode/maxDepthReached/page/pageSize/results`。普通响应不暴露原始引擎分数；DM 诊断记录保留 esRank/esScore/vespaRank/vespaRelevance/rrfRank/rrfScore。

### 按日聚合搜索

`POST /api/v1/search/day-groups` 接受与普通搜索相同的筛选字段，但允许 `query` 为空。分页单位是自然日，不是结果条数：

```json
{
  "query": "人工智能",
  "sourceTypes": ["News", "Announcement"],
  "publishTimeFrom": "2026-08-01T00:00:00+08:00",
  "publishTimeTo": "2026-08-31T23:59:59.999+08:00",
  "page": 1,
  "pageSize": 5
}
```

服务端按 `Asia/Shanghai` 自然日完整分组后再分页；同一天命中的新闻和公告始终放在同一个 `days[].items` 中，不会跨页拆分。响应示例：

```json
{
  "searchTraceId": "88cee989-0ad9-43b8-a1d4-651113488b31",
  "searchMode": "Rrf",
  "degraded": false,
  "degradationMode": null,
  "maxDepthReached": false,
  "page": 1,
  "pageSize": 5,
  "totalDays": 8,
  "totalPages": 2,
  "totalItems": 23,
  "newsItems": 18,
  "announcementItems": 5,
  "days": [
    {
      "date": "2026-08-11",
      "items": [
        {
          "newsId": "news:10001",
          "title": "示例标题",
          "highlight": "命中的正文片段",
          "publisher": "示例发布方",
          "author": "示例作者",
          "sourceType": "News",
          "publishTime": "2026-08-11T01:30:00+00:00",
          "summary": "清洗 HTML 后生成、最多 180 个 UTF-16 字符的新闻摘要…",
          "contentHtml": null,
          "cover": "https://cdn.example.com/news/10001.jpg"
        }
      ]
    }
  ]
}
```

`totalItems` 是本次融合窗口内的新闻公告总数，`totalPages = ceil(totalDays / pageSize)`。新闻返回清洗后的截断 `summary` 与 `cover`；公告的 `contentHtml` 返回经安全清洗的完整 HTML，查询命中位于文本节点时用 `<mark class="search-hit">` 标记，不会通过字符串截断破坏标签结构。`maxDepthReached=true` 表示命中数触及 `Fusion:MaxFusionDepth`，调用方应提示当前结果可能被配置的检索深度截断。

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
