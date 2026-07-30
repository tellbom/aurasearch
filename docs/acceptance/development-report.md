# 分阶段开发报告

交付共 126 个文件（含原始 plan/task）。本报告中的“修改”给出每阶段文件范围；完整目录入口为 `README.md`，源码位于 `src/`，测试位于 `tests/`，部署物位于 `deploy/`，工具位于 `tools/`，文档位于 `docs/`。

## S0

- 修改：解决方案、7 个项目、固定 SDK/NuGet lock、配置校验、SQLite migration、ProblemDetails、health/Swagger、应用 Docker。
- 测试：restore/build 通过；首次 offline source 与高版本 API 失败已修正。
- 风险：.NET 6/Windows 7 已停止支持，属于明确存量边界。
- 下一阶段：统一契约、清洗和期望状态。

## S1

- 修改：SourceType/DTO、不可变文档、canonical SHA-256、HtmlAgilityPack 清洗、desired/outbox 状态机、Index API。
- 测试：20 个 fixture、3 culture、Emoji、乱序/tombstone/并发。
- 风险：SQLite 单实例写吞吐需在 staging 压测。
- 下一阶段：ES/Vespa adapters。

## S2

- 修改：ES 7 版本探测、external version sink、BM25 query/filter/highlight、ES-only suggest、mapping dry-run。
- 测试：请求构造与 raw score 隔离。
- 风险：真实 ES/IK 未连接。
- 下一阶段：Vespa package/POC。

## S3

- 修改：Vespa 8.721.11 digest、Compose、services/news schema、Document/Query client、A–E profiles、POC 工具模板。
- 测试：XML/禁用语义字段静态审计、参数注入测试。
- 风险：Docker 不可用；中文 Gate NOT_RUN，未选择 profile 胜者。
- 下一阶段：按用户“不等待”要求继续代码开发，生产仍锁 EsOnly。

## S4

- 修改：独立 sinks、claim lease、retry/Dead/补偿、安全重放、回填、单篇/范围/全量重建、snapshot。
- 测试：SQLite 状态机与无引擎 Index API。
- 风险：真实双 Worker/崩溃注入、引擎 count/hash 仍需 Linux。
- 下一阶段：RRF。

## S5

- 修改：闭合 SearchQuery、并发 Gateway、四模式、独立 timeout/retry/breaker、rank-only RRF、稳定 tie-break、固定窗口分页、降级。
- 测试：手算、100 次乱序、并发、单/双故障、分页。
- 风险：真实 filter parity 未执行。
- 下一阶段：埋点/评估。

## S6

- 修改：trace 全链路、异步查询/结果埋点、曝光/点击防伪、retention、NDCG/MRR/Recall/Overlap/percentile、盲标工具。
- 测试：textbook 手算与 trace pair integration。
- 风险：点击存在位置偏差，只能辅助，生产质量依赖人工共同池。
- 下一阶段：readiness/运维。

## S7

- 修改：readiness 检查、自动 EsOnly/人工恢复、诊断、Chaos/离线包脚本、运行手册。
- 测试：配置/静态/无容器路径。
- 风险：纯内网、Chaos 和第二位 Operator 演练 NOT_RUN。
- 下一阶段：验收。

## S8

- 修改：验收矩阵、性能原始采样、回滚步骤、最终交付索引。
- 测试：Windows 可执行集合完成；Linux staging 条目保留 NOT_RUN。
- 风险：G1–G6 外部/人工 Gate 未通过，禁止生产 RRF。
- 下一阶段：Operator/Human 按 e2e-report 执行并书面裁决。
