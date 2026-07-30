# 架构说明

```text
Index API -> HTML cleaner -> SQLite desired_documents + one-row outbox/news_id
                                      |
                                  Outbox Worker
                              /                       \
          Elasticsearch adapter                 Vespa adapter
          external version                      test-and-set

Search API -> Search Gateway -> ES + Vespa concurrent TopK
                                  -> news_id dedupe
                                  -> weighted rank-only RRF
                                  -> fixed depth 50 -> page slice
                                  -> async telemetry queue -> SQLite
```

依赖方向为 `Api -> Application -> Domain`、`Infrastructure -> Application/Domain`，Worker 只依赖 Application/Domain 的端口。Domain 不引用 EF、HTTP 或搜索引擎包。

关键不变量：

1. 上游 `news_id` 是跨 SQLite、ES、Vespa 和埋点的稳定主键。
2. `index_version` 对同一 ID 严格递增；SQLite 期望状态、ES external version 和 Vespa condition 共同防乱序。
3. `content_text` 只由一个 HtmlAgilityPack 清洗器产生，两套 sink 共享同一个不可变对象。
4. RRF 只计算 `weight / (rankConstant + 1-based rank)`；ES `_score` 与 Vespa `relevance` 不参与跨引擎相加。
5. 固定 Top50 窗口先融合后切页，超过深度显式返回 `maxDepthReached`。
6. 自动降级只会切到 `EsOnly`，绝不自动恢复 RRF。

