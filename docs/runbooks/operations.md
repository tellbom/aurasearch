# 运维操作索引

- 禁用 Vespa：mode API → `EsOnly`；需要时关闭 Vespa sink 并滚动重启。
- 启用 RRF：先查看 `/api/v1/search-health`，所有 check 通过后提交 operator/reason。
- 单篇/范围重建：`POST /api/v1/operations/reindex`。
- 全量重建：body 中 `full=true, confirm=REINDEX_ALL`；期间保持 EsOnly。
- Dead 补偿：`POST /api/v1/operations/retry-dead?newsId=...`。
- 诊断：health 响应包含 mode、引擎 reachability、readiness、积压、同步率和最近模式审计。
- 回滚：见 `deployment.md`，自动降级后只允许人工恢复。
- SQLite retention：每小时按 `Telemetry:CleanupBatchSize` 清理过期 trace；低峰期由 Operator 评估 `VACUUM`，不要在高峰自动执行。

