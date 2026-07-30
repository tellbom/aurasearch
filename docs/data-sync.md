# 数据同步说明

Index API 在一个 SQLite 写事务内比较版本、保存最新 desired document/tombstone 并 upsert 单一 Outbox 触发器。Worker claim 后重新读取最新期望状态，因此历史 payload 不堆积。

每个 sink 独立维护 applied version、状态、重试次数、next retry 和脱敏错误：

- Applied/NoOp/Stale：终态成功；
- 429/5xx/timeout：指数退避，达到上限转 Dead；
- 其他 4xx：Permanent/Dead；
- `/operations/retry-dead`：人工补偿；
- `/operations/reindex`：按 newsId、发布时间范围或显式确认的全量重建。

ES 依靠 external version；Vespa 依靠 `news.index_version < incomingVersion` condition 和 create。删除保留 SQLite tombstone 高水位，旧 upsert 无法复活。引擎成功但 SQLite 状态提交前崩溃时，同版本操作安全重放。

`/operations/indexing-snapshot` 输出全量/按 sourceType 的本地 applied 计数、1h/24h 同步率、积压与最长滞后。生产 RRF 前还必须在真实引擎运行全量 count 与至少 2,000 个 hash 抽样，不能只以本地 applied 状态替代引擎数据。

