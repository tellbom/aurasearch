# DualNewsSearch

基于 ASP.NET Core 6.0 的新闻/公告双引擎检索 API。Elasticsearch 7.x 与 Vespa 8.x 分别执行 BM25 检索，API 支持 `EsOnly`、`VespaOnly`、`Rrf` 和 `Shadow` 模式。项目不包含向量、Embedding、RAG、LLM 查询改写或语义重排。

## 运行边界

- 目标框架固定为 `net6.0`，SDK 由 `global.json` 固定为 `6.0.402`，禁止升级到 .NET 7/8。
- Elasticsearch 与 Vespa 使用 Linux Docker 服务；API、SQLite 和 MVP 测试工具在开发机运行。
- API 启动时自动、幂等地初始化搜索引擎：
  - Elasticsearch：验证主版本为 7，创建缺失的 `IndexName` 和 `IndexAlias`；已存在且指向正确时跳过。
  - Vespa：将程序集内嵌的 Application Package 提交到 Config API 执行 `prepareandactivate`。重复部署不会清空已有文档，修改 package 后重启 API 即可生效。
  - SQLite：执行 EF Core migration。

## 启动

服务器执行：

```sh
sh deploy/linux-mvp/dependencies-up.sh
```

Docker 脚本只负责启动依赖，不创建 ES index，也不部署 Vespa Application Package。开发机需要能够访问以下 VM 映射端口：

| 服务 | VM 端口 | 用途 |
|---|---:|---|
| Elasticsearch | `29200` | 查询、写入与 index/alias 初始化 |
| Vespa Query/Document API | `28080` | 查询、文档写入与健康检查 |
| Vespa Config API | `29071` | API 启动时部署 Application Package |

`29071` 是管理端口，应通过 VM/宿主机防火墙仅允许开发机 IP 访问，不应暴露到公网。

开发机设置环境变量后启动 API：

```powershell
$env:Elasticsearch__Endpoint = 'http://192.168.124.2:29200'
$env:Elasticsearch__IndexName = 'news-v1'
$env:Elasticsearch__IndexAlias = 'news-read'
$env:Vespa__Endpoint = 'http://192.168.124.2:28080'
$env:Vespa__ConfigEndpoint = 'http://192.168.124.2:29071'
$env:Indexing__SqlitePath = 'data/search.db'
dotnet run --project src/DualNewsSearch.Api
```

若由外部平台预先管理 schema，可分别设置 `Elasticsearch__ProvisioningEnabled=false` 或 `Vespa__ProvisioningEnabled=false`。默认保持自动初始化。

Vespa package 的兼容修改会在下一次 API 启动时自动激活。Elasticsearch 已存在 index 的 analyzer/settings 不会被静默改写；需要修改不兼容 mapping 时，必须走独立的新 index、回填、一致性检查和 alias 原子切换流程。若配置的 `IndexName` 不存在但 `IndexAlias` 已指向其他索引，API 会拒绝启动，避免误建多目标 alias。

## API 约定

- 基础路径：`/api/v1`
- 请求与响应：`application/json`
- 枚举使用字符串，例如 `News`、`Announcement`、`Portal`、`Rrf`。
- 时间使用包含时区的 ISO 8601 格式，服务端统一转换为 UTC。
- 写入 API 返回 `202 Accepted`，表示 SQLite 期望状态已接收；使用 indexing snapshot 判断 ES/Vespa 是否追平。
- 搜索响应 body 和 `X-Search-Trace-ID` header 都包含本次查询的 trace ID。
- Swagger 只在 `Development` 环境启用：`/swagger`。
- Vue/Vite 开发环境建议将 `/api`、`/health` 代理到本 API，避免额外开放跨域来源。

## 搜索 API

### `POST /api/v1/search`

请求：

```json
{
  "query": "人工智能",
  "sourceTypes": ["News", "Announcement"],
  "publishTimeFrom": "2026-01-01T00:00:00+08:00",
  "publishTimeTo": "2026-12-31T23:59:59+08:00",
  "publisher": "示例发布方",
  "author": "示例作者",
  "page": 1,
  "pageSize": 20
}
```

只有 `query` 必填。`page >= 1`，`pageSize` 范围为 1–100。响应：

```json
{
  "searchTraceId": "88cee989-0ad9-43b8-a1d4-651113488b31",
  "searchMode": "Rrf",
  "degraded": false,
  "degradationMode": null,
  "maxDepthReached": false,
  "page": 1,
  "pageSize": 20,
  "results": [
    {
      "newsId": "news:10001",
      "title": "示例标题",
      "highlight": "命中的正文片段",
      "publisher": "示例发布方",
      "author": "示例作者",
      "sourceType": "News",
      "publishTime": "2026-08-11T00:00:00+00:00"
    }
  ]
}
```

两套引擎均不可用时返回 `503`；单引擎故障时响应保持相同结构，并通过 `degraded/degradationMode` 标明降级。

### `GET /api/v1/suggest?q={keyword}&size=10`

仅使用 Elasticsearch。`size` 范围为 1–50，返回去重后的标题字符串数组。

## 文档写入 API

### `PUT /api/v1/index/documents/{newsId}`

```json
{
  "sourceId": "source-10001",
  "sourceType": "News",
  "title": "示例标题",
  "contentHtml": "<article><p>示例正文</p></article>",
  "publisher": "示例发布方",
  "author": "示例作者",
  "publishTime": "2026-08-11T08:00:00+08:00",
  "indexVersion": 1
}
```

`newsId/sourceId` 最长 256，`title` 最长 1,000，`indexVersion` 必须为正数且对同一 `newsId` 单调递增。响应状态为 `Accepted`、`NoOp` 或 `Stale`。

### `DELETE /api/v1/index/documents/{newsId}?indexVersion={version}`

删除同样遵守单调版本规则。低于当前版本的删除返回 `Stale`。

### `POST /api/v1/index/documents/batch`

```json
{
  "documents": [
    {
      "newsId": "news:10001",
      "document": {
        "sourceId": "source-10001",
        "sourceType": "News",
        "title": "示例标题",
        "contentHtml": "<p>示例正文</p>",
        "publisher": "示例发布方",
        "author": "示例作者",
        "publishTime": "2026-08-11T08:00:00+08:00",
        "indexVersion": 1
      }
    }
  ]
}
```

默认每批最多 200 条。单条失败不会回滚整批，每项返回独立的 `status/errors`。

## 健康、模式与运维 API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/health/live` | 仅表示 API 进程存活 |
| GET | `/health/ready` | 检查当前模式依赖的引擎是否可用 |
| GET | `/api/v1/system/version` | 返回服务、`net6.0` 与实际运行时版本 |
| GET | `/api/v1/search-health` | 返回当前模式、引擎状态、同步率、积压、一致性与模式审计 |
| POST | `/api/v1/search-health/mode` | 人工切换 `EsOnly/VespaOnly/Rrf/Shadow` |
| GET | `/api/v1/operations/indexing-snapshot` | 返回 desired、双引擎 applied、backlog 和 sourceType 计数 |
| GET | `/api/v1/operations/consistency?hashSampleSize=100` | 比较 SQLite、ES、Vespa 的 count/sourceType/hash |
| POST | `/api/v1/operations/retry-dead?newsId=` | 重试 Dead outbox；`newsId` 为空时重试全部 |
| POST | `/api/v1/operations/reindex` | 按 newsId、发布时间范围或全量重新入队 |
| POST | `/api/v1/diagnostics/{elasticsearch|vespa}/query?topK=50` | 渲染引擎请求，不实际执行搜索 |

切换模式请求：

```json
{
  "mode": "Rrf",
  "operator": "operator-name",
  "reason": "MVP acceptance passed"
}
```

启用 `Rrf` 或 `VespaOnly` 前默认执行 readiness Gate；失败返回 `409` 且不改变模式。

全量 reindex 必须显式确认：

```json
{
  "newsId": null,
  "publishTimeFrom": null,
  "publishTimeTo": null,
  "full": true,
  "confirm": "REINDEX_ALL"
}
```

## 埋点 API

| 方法 | 路径 | 请求/说明 |
|---|---|---|
| POST | `/api/v1/telemetry/impressions` | `{"searchTraceId":"...","newsIds":["news:10001"]}` |
| POST | `/api/v1/telemetry/clicks` | `{"searchTraceId":"...","newsId":"news:10001","clickPosition":1,"dwellTimeMs":3200}` |
| GET | `/api/v1/telemetry/metrics?resultVersion=&days=7` | 查询 1–365 天的点击、零结果、降级、延迟与重叠指标 |

曝光和点击只接受当前 trace 实际返回的 `newsId`；伪造、过期或不匹配的 trace/result pair 返回 `400`。

## 构建与回归

```powershell
dotnet restore DualNewsSearch.sln --locked-mode
dotnet build DualNewsSearch.sln -c Release --no-restore
dotnet test DualNewsSearch.sln -c Release --no-build --no-restore
```

Linux/Mac 客户端 MVP：

```sh
API_URL=http://127.0.0.1:8080 sh deploy/linux-mvp/mvp-test.sh
```

架构说明见 `docs/architecture/overview.md`，配置字段见 `docs/configuration.md`，运维流程见 `docs/runbooks/operations.md`。
