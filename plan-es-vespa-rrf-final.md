# plan.md — ASP.NET Core 6.0 新闻公告双引擎检索、RRF 融合与效果评估

| 项目 | 裁决 |
|---|---|
| 文档状态 | **READY FOR CODEX** |
| 项目形态 | 全新仓库、全新 ASP.NET Core 6.0 Web API |
| 开发环境 | Windows 7 可编辑、编译和运行单元测试；Linux 服务器运行 Docker、集成测试和正式部署 |
| 搜索引擎 | 现有 Elasticsearch 7.x + 新增 Vespa 8.x |
| 最终搜索结果 | ES TopK 与 Vespa TopK 通过 RRF 融合后返回 |
| 语义能力 | 不使用向量、Embedding、ANN、Cross Encoder、LLM Reranker 或 RAG |
| 联想搜索 | 保持 Elasticsearch 单引擎，不进入 RRF |
| 上线策略 | 通过离线质量 Gate、数据完整性 Gate 和故障降级 Gate 后，可以直接启用 `Rrf`；`Shadow` 仅作为可选诊断模式 |

---

## 0. Codex 执行规则

本项目当前没有既有仓库。Codex 应在当前工作区创建完整代码仓库，不需要执行“读取旧源码并适配”的前置步骤。

必须遵守：

1. 创建 ASP.NET Core 6.0 Web API，不升级到 .NET 7/8，也不降级到 .NET Framework。
2. 生产服务运行在 Linux Docker；Windows 7 不要求运行 Vespa 或 Docker 集成测试。
3. 采用模块化单体，不拆分新的微服务。
4. Elasticsearch 与 Vespa 都由同一个 C# API 编排。
5. 现有 ES 7.x 和 IK 分词保持不变；新项目通过 HTTP/NEST 连接现有 ES。
6. 所有外部地址、索引名、超时、RRF 参数和排名权重必须配置化。
7. 不硬编码内网 IP、密码或固定索引名称。
8. 每完成一个 Task，必须运行对应测试并记录结果。
9. 遇到需要人工相关性判断的 Gate 时，Codex 负责生成数据、工具和报告模板，不得伪造人工判断结果。
10. 任一技术 Gate 不通过时必须停止后续依赖任务，输出阻塞报告，不得绕过 Gate。

---

## 1. 目标与问题定义

现有新闻、公告及门户检索使用 Elasticsearch 7.x、IK 中文分词和 BM25。当前主要问题不是完全召回不到，而是：

- 更接近关键词的文章有时排名较后；
- 相似查询的 TopK 顺序不够稳定；
- 单一路线难以判断是召回不足还是排序不足；
- 缺少可持续评估 ES、Vespa 和融合结果的埋点。

本项目验证以下假设：

> Vespa 使用与 ES 不同的中文匹配和排名策略，可以提供部分互补候选；将两套关键词搜索结果按名次进行 RRF 融合，可以提高最终 TopK 的稳定性、召回覆盖和人工相关性。

该假设必须被测量，而不是预设成功。若 RRF 不优于 ES，则通过配置恢复 `EsOnly`，项目仍视为完成了有效技术验证。

---

## 2. 范围边界

### 2.1 本期包含

- 新建 ASP.NET Core 6.0 Web API；
- 统一新闻检索数据模型；
- HTML 正文清洗；
- Elasticsearch 7.x 查询、写入和联想接口适配；
- Vespa Docker、Application Package、Document API 和 Query API；
- ES/Vespa 双引擎写入；
- ES/Vespa 并行查询；
- `news_id` 去重；
- RRF 融合；
- 固定窗口分页；
- 查询、曝光、点击埋点；
- 离线 NDCG/MRR/Recall 评估工具；
- 故障降级、熔断、模式切换和回滚；
- 内网离线部署包和运行手册。

### 2.2 本期禁止

| 编号 | 禁止内容 |
|---|---|
| B1 | 向量索引、Embedding、ANN、稠密/稀疏向量召回 |
| B2 | Cross Encoder、LLM Reranker、LLM 查询改写 |
| B3 | RAGFlow、Haystack、LlamaIndex 等 RAG 编排框架 |
| B4 | 将 ES `_score` 与 Vespa `relevance` 直接相加 |
| B5 | 用标题替代 `news_id` 去重 |
| B6 | 把 Vespa 放进 ASP.NET 应用容器 |
| B7 | 让 Windows 7 本机承担 Vespa、Docker 或 Linux 集成测试 |
| B8 | 运行时访问公网 |
| B9 | 未通过数据完整性检查就启用 RRF |
| B10 | 通过“读取后比较再写入”的非原子方式处理乱序更新 |

---

## 3. 固定技术栈

| 层 | 技术 |
|---|---|
| Web API | ASP.NET Core 6.0 |
| C# | C# 10 |
| Elasticsearch 客户端 | NEST 7.x，版本必须与 ES 7.x 兼容；若现场只允许原始 HTTP，可实现等价适配器 |
| Vespa 客户端 | `HttpClient` 调用 Document API 和 Query API |
| HTML 解析 | HtmlAgilityPack，禁止正则删除 HTML |
| 持久化 | EF Core 6 + SQLite，保存检索文档期望状态、索引任务状态和埋点；SQLite 不是业务新闻主库 |
| 稳定性 | `IHttpClientFactory` + Polly 兼容 .NET 6 的版本 |
| 测试 | xUnit + FluentAssertions；Linux 集成测试使用真实 Docker 容器 |
| 部署 | Docker Compose，应用、Vespa、SQLite 数据卷分别持久化 |
| 日志 | `ILogger` 结构化日志；允许接入现有内网日志平台 |

### 3.1 .NET 6 门禁说明

本项目明确要求 `.NET 6`，因此：

- `global.json` 固定实际安装的 .NET 6 SDK 补丁版本；
- 所有 NuGet 包固定版本并导出离线依赖清单；
- 不引入只支持 .NET 7/8 的 API；
- Windows 7 只要求完成源码编辑、`dotnet build` 和不依赖容器的单元测试；
- 容器集成测试统一在 Linux 开发服务器或 CI 上执行；
- 文档必须记录 .NET 6 和 Windows 7 均属于存量兼容边界，不能在本项目中擅自改变。

---

## 4. 目标架构

```text
上游新闻/公告系统
        |
        | Upsert/Delete API，携带稳定 news_id 与单调 index_version
        v
ASP.NET Core 6 Search API
        |
        +--> HTML 清洗、时间标准化、content_hash
        |
        +--> SQLite Desired Document / Outbox
                    |
                    v
              Index Worker
                    |
          +---------+---------+
          |                   |
          v                   v
 Elasticsearch 7.x         Vespa 8.x
 IK + 现有 BM25          CJK 候选方案 + Rank Profile
          |                   |
          | Search TopK       | Search TopK
          +---------+---------+
                    |
                    v
             Search Orchestrator
                    |
              news_id 去重
                    |
                 RRF 融合
                    |
             固定窗口分页 TopN
                    |
                    v
                  前端
                    |
           impression / click API
                    |
                    v
                SQLite 埋点
```

### 4.1 为什么采用模块化单体

- 搜索、过滤、融合和埋点共享同一个请求模型；
- 避免独立微服务重复实现过滤规则；
- C# 开发、Docker 部署和内网交付更简单；
- Vespa 与 ES 仍保持独立故障域；
- 后续可以按接口拆分，但 V1 不提前增加复杂度。

---

## 5. 仓库与解决方案结构

Codex 应创建：

```text
DualNewsSearch/
├── DualNewsSearch.sln
├── global.json
├── Directory.Build.props
├── README.md
├── src/
│   ├── DualNewsSearch.Api/
│   ├── DualNewsSearch.Application/
│   ├── DualNewsSearch.Domain/
│   ├── DualNewsSearch.Infrastructure/
│   └── DualNewsSearch.Worker/
├── tests/
│   ├── DualNewsSearch.UnitTests/
│   └── DualNewsSearch.IntegrationTests/
├── deploy/
│   ├── app/
│   └── vespa/
│       ├── docker-compose.yml
│       ├── application/
│       │   ├── services.xml
│       │   └── schemas/news.sd
│       ├── scripts/
│       └── samples/
├── docs/
│   ├── architecture/
│   ├── baseline/
│   ├── eval/
│   ├── runbooks/
│   └── acceptance/
└── tools/
    ├── backfill/
    ├── evaluation/
    └── offline-package/
```

### 5.1 项目职责

| 项目 | 职责 |
|---|---|
| Domain | 数据模型、枚举、纯 RRF 算法、业务不变量 |
| Application | 用例、编排接口、模式决策、配置模型 |
| Infrastructure | ES、Vespa、SQLite、HTML 清洗、日志和 HTTP 实现 |
| Worker | 索引 Outbox 消费、重试、补偿、一致性检查 |
| Api | Index/Search/Suggest/Telemetry/Health 接口 |

依赖方向必须保持：

```text
Api/Worker -> Application -> Domain
Infrastructure -> Application/Domain
Domain 不依赖任何基础设施包
```

---

## 6. API 契约

### 6.1 新闻写入

```http
PUT /api/index/documents/{newsId}
```

请求：

```json
{
  "sourceId": "12345",
  "sourceType": "news",
  "title": "新闻标题",
  "contentHtml": "<p>正文</p>",
  "publisher": "发布者",
  "author": "编写者",
  "publishTime": "2026-07-30T08:00:00+08:00",
  "indexVersion": 10025
}
```

规则：

- URL 中 `newsId` 是跨 ES、Vespa、SQLite 的唯一稳定 ID；
- 推荐格式为 `{sourceType}:{sourceId}`；
- `indexVersion` 必须是同一 `newsId` 下严格单调递增的 `long`；
- 相同或更低版本返回幂等/过期状态，不覆盖更高版本；
- API 只落 SQLite 期望状态与 Outbox，不同步等待 ES/Vespa 完成。

### 6.2 删除

```http
DELETE /api/index/documents/{newsId}?indexVersion=10026
```

删除也必须携带更高版本。SQLite 保留 tombstone 和最高版本，避免延迟旧消息把已删除新闻重新写回搜索引擎。

### 6.3 批量写入

```http
POST /api/index/documents/batch
```

- 有单批大小上限；
- 每条独立返回结果；
- 不允许一条失败导致整批回滚；
- 支持历史回填工具通过此接口提交。

### 6.4 正式搜索

```http
POST /api/search
```

请求模型：

```json
{
  "query": "关键词",
  "sourceTypes": ["news", "announcement"],
  "publishTimeFrom": null,
  "publishTimeTo": null,
  "publisher": null,
  "author": null,
  "page": 1,
  "pageSize": 20
}
```

响应必须包含：

- `searchTraceId`；
- `searchMode`；
- `degraded`、`degradationMode`；
- `maxDepthReached`；
- 结果列表；
- 每条结果不向普通前端暴露内部原始分数，但调试接口可以返回诊断信息。

### 6.5 联想搜索

```http
GET /api/suggest?q=...
```

联想搜索只请求 Elasticsearch，禁止进入 Vespa 和 RRF。

### 6.6 曝光与点击

```http
POST /api/telemetry/impressions
POST /api/telemetry/clicks
```

必须验证 `searchTraceId + newsId` 确实存在于服务端记录的返回结果中，防止伪造埋点污染指标。

---

## 7. 统一数据模型

```csharp
public sealed record NewsSearchDocument(
    string NewsId,
    string SourceId,
    SourceType SourceType,
    string Title,
    string ContentText,
    string Publisher,
    string Author,
    DateTimeOffset PublishTime,
    long IndexVersion,
    string ContentHash,
    DateTimeOffset UpdatedAt);
```

原始 `content_html` 保存在 SQLite 期望状态表中用于重建，但不写入 ES/Vespa 正文索引，也不在日志中输出。

### 7.1 必需字段

| 字段 | 用途 |
|---|---|
| `news_id` | 去重、更新、删除、RRF、埋点关联 |
| `source_id` | 与上游业务记录关联 |
| `source_type` | news / announcement / portal |
| `title` | 高权重全文字段 |
| `content_text` | 统一清洗后的正文 |
| `publisher` | 显式筛选或发布者搜索 |
| `author` | 显式筛选或作者搜索 |
| `publish_time` | 时间筛选与有限新鲜度提升 |
| `index_version` | 原子防乱序与 tombstone 高水位 |
| `content_hash` | 跳过无变化写入与一致性检查 |

### 7.2 Content Hash

在 HTML 清洗和 UTC 标准化后计算：

```text
SHA256(
  title + UnitSeparator +
  content_text + UnitSeparator +
  publisher + UnitSeparator +
  author + UnitSeparator +
  publish_time_utc + UnitSeparator +
  source_type
)
```

`index_version` 不进入正文 hash；它负责事件顺序，不代表内容本身。

---

## 8. SQLite 持久化与索引状态机

SQLite 仅保存搜索服务自身状态，必须挂载独立数据卷。

### 8.1 表结构

#### `desired_documents`

- `news_id` PK；
- 原始索引请求字段；
- `content_html`；
- `content_text`；
- `content_hash`；
- `index_version`；
- `desired_operation`：Upsert/Delete；
- `es_applied_version`；
- `vespa_applied_version`；
- `es_status`、`vespa_status`；
- 重试次数、下次重试时间、最后错误；
- 创建/更新时间。

#### `index_outbox`

- 一条 `news_id` 最多保留一个待处理触发器；
- 新版本到达时只更新 `desired_documents`，不堆积所有历史 payload；
- Worker 每次加载当前最新期望状态；
- 旧版本自动被新版本合并或标记 superseded。

#### 埋点表

- `search_queries`；
- `search_results`；
- `search_clicks`，或在结果表上保存幂等点击状态；
- 所有表配置保留期限与清理任务。

### 8.2 状态更新原子性

索引 API 在一个 SQLite 事务中：

1. 读取 `desired_documents.index_version`；
2. 仅接受严格更高版本；
3. 更新期望状态；
4. Upsert Outbox 触发器；
5. 提交事务。

该事务是服务端第一层乱序防护。

### 8.3 引擎端第二层乱序防护

#### Elasticsearch

- Upsert/Index 使用 `version={index_version}&version_type=external`；
- Delete 使用对应的外部版本控制；
- 更低版本冲突视为 stale/superseded，不进入重试死循环。

#### Vespa

- Schema 增加 `index_version` attribute；
- Put/Update/Remove 使用 Document API test-and-set condition；
- 不存在文档时允许 create；
- 条件不满足时区分为 stale condition，不作为服务故障；
- 删除后的最高版本仍由 SQLite tombstone 保存，避免旧 Upsert 复活。

禁止：

```text
GET 当前文档 -> C# 比较 updated_at -> 再写
```

因为该方式存在并发检查与写入之间的竞态窗口。

---

## 9. HTML 清洗

只实现一个 HTML→Text 模块，ES 与 Vespa 共用其结果。

### 9.1 规则

- 删除 `script/style/noscript/iframe/object/embed`；
- 删除注释和明显不可见节点；
- HTML Entity 解码，包括双重编码的常见情况；
- `p/div/br/h1-h6/li/tr` 转换为换行；
- `td/th` 之间保留分隔符；
- 保持中文标点；
- 不在中文字符之间主动插入空格；
- 合并连续空白和异常换行；
- 损坏 HTML 进入恢复解析，不向调用方抛出未处理异常；
- 最大长度默认 200,000 字符，可配置；
- 截断不得切断 surrogate pair；
- 同一输入输出必须确定性一致。

### 9.2 测试矩阵

普通段落、嵌套标签、表格公告、HTML Entity、双重编码、脚本样式、空正文、损坏 HTML、中文标点、多余换行、超长正文、Emoji 边界、幂等性。

---

## 10. Elasticsearch 路线

### 10.1 目标

ES 是现有稳定基线和联想搜索引擎。本项目不主动改变现场 IK 字典与既有查询效果。

### 10.2 必须支持

- 连接现有 ES 7.x；
- 启动时校验版本和索引可用性；
- 建立项目所需 Mapping/alias 的脚本，但不得覆盖现场已有索引；
- Upsert/Delete 使用外部版本；
- 搜索返回 TopK、原始名次、`_score`、highlight；
- Suggest 使用现有 completion/edge-ngram/既有实现；
- 提供诊断接口导出实际 ES 请求 JSON；
- 记录 ES P50/P95/P99、超时和零结果。

### 10.3 ES 基线冻结

在进行 RRF 效果对比期间，ES 查询配置必须由 `result_version` 标识。若 ES 查询权重或 IK 配置改变，必须生成新的基线版本，禁止把不同版本的指标混在一起。

---

## 11. Vespa Docker 与 Application Package

### 11.1 部署

使用官方 `vespaengine/vespa` 镜像，但不在文档中写模糊的 `8.x` 作为最终版本。Codex 在 S3：

1. 选择一个实际可拉取和验证的完整版本标签；
2. 锁定镜像 digest；
3. 导出 tar；
4. 记录 SHA-256；
5. 在断网机器上完成导入演练。

目录：

```text
deploy/vespa/
├── docker-compose.yml
├── application/
│   ├── services.xml
│   └── schemas/news.sd
├── scripts/
│   ├── up.sh
│   ├── down.sh
│   ├── logs.sh
│   ├── health-check.sh
│   ├── deploy-app.sh
│   ├── reset-dev-data.sh
│   ├── export-image.sh
│   └── import-image.sh
└── samples/
```

容器要求：

- 独立容器；
- `8080`、`19071` 只在内部网络可访问；
- `/opt/vespa/var`、`/opt/vespa/logs` 持久化；
- 配置文件版本化；
- 健康检查覆盖 config server 与 query/document container；
- 资源限制、文件描述符和宿主机前置条件写入 README；
- 不要求开发人员进入容器手工编辑文件。

### 11.2 Schema 基础字段

```text
news_id
source_id
source_type
index_version
content_hash
title
content
publisher
author
publish_time
```

过滤字段使用 attribute + filter rank；全文字段启用对应匹配与排名能力。

### 11.3 中文检索候选方案

不能在未经 POC 的情况下把“bigram + BM25”直接视为最终答案。S3 必须至少比较：

| 方案 | 匹配 | 排名 |
|---|---|---|
| A | CJK gram-size 2 + all | BM25 |
| B | CJK gram-size 2 + weakAnd | BM25 |
| C | CJK gram-size 2 + all | native/proximity 类排序 |
| D | CJK gram-size 2 + weakAnd | native/proximity 类排序 |
| E | 预分词字段 | BM25 |
| Baseline | ES IK | 现有 ES 排名 |

要求：

- 通过 Vespa trace 检查真实 query tree；
- `publisher`、`author` 的精确过滤字段与全文匹配字段分离；
- 普通关键词默认只计算 `title + content`；
- 用户显式选择发布者/作者过滤时才使用其过滤字段；
- 发布时间只对文本相关度相近的结果进行有限提升；
- 时间衰减必须将负年龄钳制为 0，避免未来错误时间获得异常加权；
- POC 通过后再将最终匹配方式写入 `news.sd` 和配置默认值。

### 11.4 中文技术 Gate

准备至少：

- 2,000 篇真实、匿名化新闻/公告；
- 30 个真实中文查询，覆盖热门、中频、长尾、公司名、公告类型和多词查询；
- 每个查询汇总 ES 与各 Vespa 候选方案 Top10；
- 至少两名人员按 0–3 相关性标注；
- 生成 NDCG@10、MRR@10、Recall@20、Overlap@10 和延迟。

进入后续全量开发的最低 Gate：

```text
最佳 Vespa 方案 NDCG@10 >= ES NDCG@10 × 0.75
并且 Vespa 存在非零的独有相关结果
并且无系统性中文拆分错误
并且 P95 延迟符合双引擎总超时预算
```

0.75 只代表“值得继续开发”，不是生产 RRF 的上线标准。

---

## 12. 双引擎查询与 RRF

### 12.1 统一 SearchQuery

ES 与 Vespa 适配器必须接收同一个不可变 `SearchQuery`：

- query；
- sourceTypes；
- publishTimeFrom/To；
- publisher；
- author；
- page/pageSize；
- 后续新增过滤字段。

不得使用任意字典绕过字段对齐。新增过滤字段时，两个适配器都必须实现，并由 parity test 验证。

### 12.2 并行执行

- 两个引擎并发发起；
- 每个引擎独立 timeout 和 cancellation token；
- 一个引擎超时不得取消另一个引擎；
- 存在全局超时；
- 熔断器按引擎独立；
- Vespa 请求设置自身软超时；
- 单引擎失败转换为降级，不向外抛出导致整体 500；
- 两个引擎同时失败才返回明确 5xx。

### 12.3 RRF

```text
RRF(doc) = es_weight / (rank_constant + es_rank)
         + vespa_weight / (rank_constant + vespa_rank)
```

缺失于某引擎的文档不贡献该项，不使用虚拟大名次。

V1 参数：

```yaml
es_top_k: 50
vespa_top_k: 50
final_top_k: 20
rank_constant: 60
es_weight: 1.0
vespa_weight: 1.0
max_fusion_depth: 50
```

全部可配置并进入 `result_version`。

### 12.4 稳定排序

RRF 分数相同时：

1. 两个引擎都召回优先；
2. 最佳单引擎名次更高优先；
3. 发布时间较新优先；
4. `news_id` Ordinal 升序兜底。

### 12.5 分页

V1 使用固定融合窗口：

- 每次查询分别取得 ES/Vespa Top50；
- 全局融合后再切页；
- 最大只返回前 50 条融合结果；
- 超过深度返回 `maxDepthReached=true`；
- 禁止逐页独立融合；
- 导出/审计深分页另设 ES-only 接口，不混入用户搜索接口。

---

## 13. 搜索模式与上线策略

```text
EsOnly
VespaOnly
Rrf
Shadow
```

### 13.1 模式语义

| 模式 | 行为 |
|---|---|
| EsOnly | 只查 ES，零 Vespa 查询 |
| VespaOnly | 只查 Vespa，用于诊断 |
| Rrf | 双引擎并行并返回融合结果 |
| Shadow | 双引擎并行、记录 Vespa 与融合结果，但响应保持 ES |

### 13.2 启用 RRF 的硬 Gate

不强制运行两周 Shadow。满足以下条件后可以直接将生产模式设为 `Rrf`：

1. Vespa 全量回填完成；
2. 最近 1 小时和 24 小时同步率达到阈值；
3. 全量计数和按 `source_type` 计数偏差在阈值内；
4. 抽样 `content_hash` 一致；
5. 过滤 parity 测试通过；
6. 中文技术 Gate 通过；
7. 离线共同标注池中：
   - `NDCG@10(RRF) >= NDCG@10(ES)`；
   - `MRR@10(RRF) >= MRR@10(ES)`；
   - `Recall@20(RRF) > Recall@20(ES)`；
8. 故障降级和回滚演练通过；
9. 产品明确接受最大融合深度 50。

建议目标是 RRF NDCG@10 相对 ES 至少提升 3%；未达到时仍可由产品裁决是否上线，但报告必须清楚标出无显著提升。

### 13.3 自动降级

触发任一条件自动转 `EsOnly` 并告警：

- Vespa 容器不可用或熔断；
- 最近 1 小时新增同步率低于配置阈值；
- 最近 24 小时新增同步率低于配置阈值；
- 全量或按 source_type 计数偏差超过阈值；
- hash 抽样不一致超过阈值；
- Outbox 积压超过阈值；
- 最长未同步时间超过阈值。

自动降级后不得自动恢复到 RRF，必须人工确认后重新启用，避免模式抖动。

---

## 14. 埋点与评估

### 14.1 查询级

```text
search_trace_id
query
normalized_query
filters
search_time
search_mode
result_version
es_latency_ms
vespa_latency_ms
fusion_latency_ms
total_latency_ms
es_hit_count
vespa_hit_count
merged_unique_count
es_timeout
vespa_timeout
degradation_mode
参数快照
```

### 14.2 结果级

```text
search_trace_id
news_id
es_rank
es_score
vespa_rank
vespa_relevance
rrf_rank
rrf_score
present_in_es
present_in_vespa
exposed
clicked
click_position
dwell_time
```

### 14.3 指标

- ES/Vespa/RRF Clicked Recall@K；
- ES/Vespa/RRF Click MRR；
- `es_unique_click_rate`；
- `vespa_unique_click_rate`；
- Overlap@K；
- 零结果率；
- P50/P95/P99 延迟；
- 降级率；
- 同步成功率；
- Outbox 积压；
- 离线 NDCG@10、MRR@10、Recall@20。

点击不是某个引擎的直接胜利。归因只能基于候选是否存在及其原始名次。

### 14.4 人工评估

Codex 负责：

- 抽样脚本；
- pooled result 导出；
- 标注 TSV/CSV；
- 评分说明；
- NDCG/MRR 计算工具；
- 报告生成。

人工负责：

- 真实相关性标注；
- 产品是否接受 RRF 上线的最终裁决。

Codex 禁止自行假装人工评审者填写相关性等级。

---

## 15. 测试策略

### 15.1 Windows 7 可执行

- Domain/Application 单元测试；
- HTML 清洗测试；
- Hash、版本状态机和 RRF 测试；
- HTTP 请求构造测试；
- 使用 fake adapter 的并发/超时/降级测试。

### 15.2 Linux 集成测试

- Vespa Docker 启动和 Application Package 部署；
- Vespa CRUD 和条件写入；
- ES 外部版本写入；
- SQLite Outbox；
- 双引擎并发查询；
- RRF 端到端；
- 停止任一引擎后的降级；
- 全量回填与断点恢复；
- 数据一致性和自动关闭；
- 性能测试。

### 15.3 必测竞态

- v10、v12、v11 顺序到达，最终必须为 v12；
- 删除 v20 后到达 Upsert v19，不得复活；
- 同一版本重复提交必须幂等；
- 两个 Worker 同时处理同一 news_id；
- 引擎写成功、SQLite 状态更新前进程崩溃；
- SQLite 状态提交后引擎请求超时但实际已写入；
- Vespa test-and-set 失败应分类为 stale，而不是持续重试。

---

## 16. 开发阶段

| 阶段 | 名称 | 退出标准 |
|---|---|---|
| S0 | 仓库和基础框架 | .NET 6 解决方案、配置、Docker 基础、SQLite migration、CI 脚本可用 |
| S1 | 统一契约与 HTML 清洗 | 数据模型、版本规则、清洗、hash 和 SQLite 期望状态完成 |
| S2 | Elasticsearch 基线 | ES 连接、Mapping/alias 工具、外部版本写入、搜索和 Suggest 完成 |
| S3 | Vespa 与中文 POC | Docker、Application Package、CRUD、条件写入、中文方案评测工具完成并通过技术 Gate |
| S4 | 持久化双写与回填 | Outbox、重试、tombstone、回填、补偿和一致性检查完成 |
| S5 | 双引擎查询与 RRF | 并发、过滤对齐、RRF、分页、降级、模式切换完成 |
| S6 | 埋点与离线评估 | 查询/结果/曝光/点击链路和评估工具完成 |
| S7 | 运维、故障和内网交付 | 自动降级、健康检查、运行手册、离线镜像与 NuGet 包验证完成 |
| S8 | 验收和上线裁决 | 性能、一致性、质量、回滚证据齐全，可明确选择 Rrf 或 EsOnly |

---

## 17. 关键架构裁决

| ADR | 裁决 |
|---|---|
| ADR-01 | 新建 ASP.NET Core 6.0 模块化单体 |
| ADR-02 | Vespa 独立 Docker Compose，通过内部网络接入应用 |
| ADR-03 | SQLite 保存期望状态、Outbox 和埋点，使用持久化卷 |
| ADR-04 | 新闻写 API 异步索引，不把 ES/Vespa 故障带入上游事务 |
| ADR-05 | `news_id` 由上游提供，推荐 `{sourceType}:{sourceId}` |
| ADR-06 | `index_version` 为强制、单调 `long`，删除也必须递增 |
| ADR-07 | SQLite 最新期望状态 + ES external version + Vespa test-and-set 三层防乱序 |
| ADR-08 | HTML 清洗使用 HtmlAgilityPack，共享一份 `content_text` |
| ADR-09 | Vespa 中文策略先 POC，不预设 bigram BM25 必胜 |
| ADR-10 | RRF 初始 TopK=50/50，k=60，权重 1:1，全部配置化 |
| ADR-11 | 正式搜索返回 RRF；Suggest 保持 ES-only |
| ADR-12 | V1 固定融合深度 50，不做逐页融合 |
| ADR-13 | 无强制两周 Shadow，Gate 通过后可直接 Rrf |
| ADR-14 | Shadow 保留为诊断模式 |
| ADR-15 | 自动降级后人工恢复，不自动回切 RRF |
| ADR-16 | 人工标注不可由 Codex 伪造 |
| ADR-17 | 内网离线部署必须做断网演练 |

---

## 18. 回滚

| 触发 | 动作 |
|---|---|
| RRF 质量不佳 | 配置切换 `EsOnly` |
| Vespa 不稳定 | 熔断并自动 `EsOnly` |
| Vespa 数据漂移 | 自动 `EsOnly`，停止 Vespa 参与查询 |
| Vespa 写入拖慢 Worker | 关闭 Vespa sink，ES 继续 |
| 完全撤回 | 移除 Vespa adapter/sink 注册，保留 ES 路径和埋点 |
| 需要重建 Vespa | `EsOnly` → 清 readiness → 重建/回填 → 一致性检查 → 人工切 `Rrf` |

所有模式变化必须记录审计日志和操作者。

---

## 19. 最终验收标准

1. 新仓库可使用固定 .NET 6 SDK 构建。
2. Windows 7 可以运行非容器单元测试。
3. Linux Docker 可以启动 ASP.NET 应用和独立 Vespa。
4. Vespa Application Package 可脚本化部署。
5. 新闻可通过 HTTP API 新增、更新和删除。
6. HTML 清洗结果同时写入 ES 与 Vespa。
7. `news_id`、`index_version` 和 `content_hash` 在两引擎保持一致。
8. 乱序更新、重复更新和删除后旧消息复活测试全部通过。
9. ES 与 Vespa 可以并行查询。
10. 两边结果按 `news_id` 去重并按 RRF 融合。
11. 不直接相加原始分数。
12. 分页无跨页重复和遗漏，并明确最大深度。
13. 任一引擎故障可以降级。
14. 两个引擎失败返回明确错误，不伪装成零结果。
15. RRF 上线前中文 Gate、过滤 Gate、数据 Gate 和离线质量 Gate 全部通过。
16. 查询、曝光和点击可通过 `search_trace_id` 关联。
17. ES、Vespa 和 RRF 的质量与延迟可以分别计算。
18. 可以一键切换 `EsOnly`、`VespaOnly`、`Rrf`、`Shadow`。
19. 所有 Docker 镜像和 NuGet 依赖可在纯内网恢复。
20. 代码、配置、部署脚本、测试、运行手册和验收报告齐全。
21. 依赖和 Schema 审计确认未引入任何向量、Embedding 或语义 Reranker。
