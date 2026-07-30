# task.md — ASP.NET Core 6.0 ES + Vespa 双引擎检索与 RRF 开发任务

本文件是 `plan-es-vespa-rrf-final.md` 的执行清单。

## 阅读规则

- 项目是全新仓库，不需要适配旧源码。
- 所有代码固定为 ASP.NET Core / .NET 6。
- `Owner`：
  - **Codex**：可由 Codex 完成代码、配置、脚本和自动化测试；
  - **Human**：需要真实人工判断或产品裁决；
  - **Operator**：需要在 Linux/内网环境执行部署或演练；
  - **Codex + Human**：Codex生成工具与报告，人工提供结论。
- 每个代码任务必须：编译成功、测试通过、无硬编码端点、更新文档。
- Windows 7 只跑单元测试；带 Docker 的任务在 Linux 执行。
- Gate 失败后停止所有依赖任务，输出 `docs/acceptance/blocker-report.md`。

---

# S0 — 仓库与基础框架

## Task 0.1 — 创建仓库和解决方案

- **Owner:** Codex
- **目标:** 建立可构建的 .NET 6 模块化单体。
- **前置:** 无。
- **实现:** 创建 `DualNewsSearch.sln`、`global.json`、`Directory.Build.props`，以及 Api/Application/Domain/Infrastructure/Worker、UnitTests、IntegrationTests 项目；配置项目引用方向。
- **测试:** `dotnet restore`、`dotnet build -c Release`、空测试套件运行。
- **验收:** 所有项目为 `net6.0`；Domain 无基础设施依赖；没有 .NET 7/8 包。
- **提交:** Yes。

## Task 0.2 — 固定 NuGet、代码规范和构建门禁

- **Owner:** Codex
- **目标:** 保证 Windows 7 与内网可重复构建。
- **前置:** 0.1。
- **实现:** 固定包版本；开启 nullable、warnings as errors；增加 `.editorconfig`；生成 `packages.lock.json`；增加离线 NuGet 源配置模板；记录实际 .NET 6 SDK 补丁版本。
- **测试:** 清理 NuGet 缓存后恢复；验证 lock file 生效。
- **验收:** 无浮动包版本；文档列出所有直接和传递依赖。
- **提交:** Yes。

## Task 0.3 — 配置模型与启动校验

- **Owner:** Codex
- **目标:** 所有端点和参数配置化并启动即校验。
- **前置:** 0.1。
- **实现:** 建立 ElasticsearchOptions、VespaOptions、FusionOptions、IndexingOptions、TelemetryOptions、SearchModeOptions；使用 Options validation；增加示例配置和环境变量映射。
- **测试:** 缺失端点、非法 TopK、finalTopK 大于窗口、非法 timeout 时启动失败。
- **验收:** 代码中无内网 IP、账号、索引名和 RRF 数值散落。
- **提交:** Yes。

## Task 0.4 — SQLite 与初始 Migration

- **Owner:** Codex
- **目标:** 建立搜索服务持久化底座。
- **前置:** 0.1。
- **实现:** EF Core 6 + SQLite；定义 DbContext；创建 desired_documents、index_outbox、search_queries、search_results、search_clicks/点击状态表；建立必要索引和保留期限字段。
- **测试:** Migration 创建、升级、重新启动持久化测试。
- **验收:** SQLite 文件路径配置化；Docker 卷可持久化；没有把正文写入日志。
- **提交:** Yes。

## Task 0.5 — 基础 API、异常模型和健康接口

- **Owner:** Codex
- **目标:** 建立统一响应和健康框架。
- **前置:** 0.3、0.4。
- **实现:** ProblemDetails；request correlation；`/health/live`、`/health/ready`；Swagger；结构化日志；API 版本前缀。
- **测试:** 健康端点、校验错误、未处理异常转换测试。
- **验收:** 错误不泄露堆栈和凭据；日志带 correlation id。
- **提交:** Yes。

## Task 0.6 — Linux Docker 与开发脚本基础

- **Owner:** Codex + Operator
- **目标:** 应用可以在 Linux Docker 启动。
- **前置:** 0.5。
- **实现:** 多阶段 Dockerfile；应用 Compose；SQLite 卷；内部网络；build/up/down/logs/health 脚本。
- **测试:** Operator 在 Linux 执行干净启动、重启、卷持久化。
- **验收:** 不访问公网即可使用预加载镜像启动。
- **提交:** Yes。

---

# S1 — 数据契约、HTML 清洗与期望状态

## Task 1.1 — SourceType 与新闻写入 DTO

- **Owner:** Codex
- **目标:** 建立严格输入契约。
- **前置:** S0。
- **实现:** SourceType 枚举；Upsert/Delete/Batch DTO；FluentValidation 或内置校验；NewsId、SourceId、IndexVersion 规则。
- **测试:** 空标题、空 ID、非法时间、非法类型、非正版本、超长字段。
- **验收:** `indexVersion` 为必填 long；删除也必须携带版本。
- **提交:** Yes。

## Task 1.2 — NewsSearchDocument 与 ContentHash

- **Owner:** Codex
- **目标:** 一个不可变文档供两引擎共用。
- **前置:** 1.1。
- **实现:** immutable record；UTC 标准化；SHA-256 canonical hash；Unit Separator；null/empty 规则。
- **测试:** 跨文化稳定性、字段边界碰撞、单字段变化、重启稳定性。
- **验收:** ES/Vespa writer 不能拥有独立业务转换模型。
- **提交:** Yes。

## Task 1.3 — HTML 清洗实现

- **Owner:** Codex
- **目标:** 从 HTML 得到确定性的中文正文。
- **前置:** 1.1。
- **实现:** HtmlAgilityPack；按 plan §9 处理节点、换行、实体、损坏 HTML、长度和 surrogate pair；输出 contentTruncated。
- **测试:** 由 1.4 完成。
- **验收:** 禁止 regex strip-tags；任何输入不导致未处理异常。
- **提交:** Yes。

## Task 1.4 — HTML 清洗测试矩阵

- **Owner:** Codex
- **目标:** 覆盖真实新闻/公告 HTML。
- **前置:** 1.3。
- **实现:** 至少 20 个匿名化 fixture，覆盖 13 类矩阵；测试 zh-CN/en-US/tr-TR culture；验证不在 CJK 字符间插入空格。
- **测试:** `dotnet test`；清洗器分支覆盖率目标 ≥90%。
- **验收:** 幂等性和 Emoji 截断边界通过。
- **提交:** Yes。

## Task 1.5 — Desired Document 状态机

- **Owner:** Codex
- **目标:** 在 SQLite 原子保存最新期望版本和 tombstone。
- **前置:** 0.4、1.2、1.3。
- **实现:** UpsertDesired、DeleteDesired；事务内比较严格更高版本；同版本幂等；更低版本 stale；Upsert Outbox 触发器；保存原始 HTML 供重建。
- **测试:** v10/v12/v11、重复版本、删除 v20 后 upsert v19、并发提交。
- **验收:** SQLite 中永远只保留同一 newsId 的最新期望状态和最高版本。
- **提交:** Yes。

## Task 1.6 — Index API

- **Owner:** Codex
- **目标:** 暴露单条、删除和批量写入接口。
- **前置:** 1.5。
- **实现:** PUT/DELETE/Batch；每条返回 Accepted/NoOp/Stale/Invalid；批量有大小上限；接口只落 SQLite，不等待搜索引擎。
- **测试:** API 集成测试、部分失败、重复请求、并发请求。
- **验收:** Vespa/ES 停止时 Index API 仍可接收并持久化期望状态。
- **提交:** Yes。

---

# S2 — Elasticsearch 7.x 基线

## Task 2.1 — Elasticsearch 连接与版本探测

- **Owner:** Codex + Operator
- **目标:** 连接现有内网 ES 7.x。
- **前置:** S0。
- **实现:** NEST 7.x typed client；连接池；HTTP/1.1；timeout；启动探测集群版本、索引和 IK analyzer；敏感配置从环境变量读取。
- **测试:** Linux 环境连通测试；错误证书/端点/超时分类。
- **验收:** 明确拒绝非兼容主版本；无每请求创建 client。
- **提交:** Yes。

## Task 2.2 — ES Mapping 与 Alias 管理工具

- **Owner:** Codex
- **目标:** 提供可审计的索引初始化能力，不覆盖现场索引。
- **前置:** 2.1。
- **实现:** Mapping 模板、alias、IK 字段；dry-run；检测已存在索引；导出现有 mapping/settings 到 docs/baseline。
- **测试:** 临时 ES 索引创建与重复执行幂等测试。
- **验收:** 工具默认不删除或覆盖索引；破坏性命令要求显式确认。
- **提交:** Yes。

## Task 2.3 — ES 外部版本 Upsert/Delete

- **Owner:** Codex
- **目标:** 防止 ES 乱序覆盖和旧删除。
- **前置:** 1.2、2.1。
- **实现:** Index/Upsert 携带 external version；Delete 携带版本；版本冲突分类 stale；contentHash 无变化跳过。
- **测试:** v10→v12→v11；删除后旧 upsert；重复 v12；网络超时后重试。
- **验收:** 最终 ES 文档版本严格为最大 indexVersion。
- **提交:** Yes。

## Task 2.4 — ES Search Adapter

- **Owner:** Codex
- **目标:** 实现关键词 TopK 基线。
- **前置:** 2.1、统一 SearchQuery 草案。
- **实现:** title/content 查询；sourceType/time/publisher/author filter；highlight；返回 1-based rank 和 `_score`；导出实际 Query DSL 诊断信息。
- **测试:** 查询构造、特殊字符、空查询、过滤组合、取消和超时。
- **验收:** 原始分数只用于诊断，不进入跨引擎直接加法。
- **提交:** Yes。

## Task 2.5 — ES Suggest Adapter

- **Owner:** Codex
- **目标:** 正式保留 ES-only 联想搜索。
- **前置:** 2.1。
- **实现:** 接入现场 completion/edge-ngram/既有 suggest 方案；若现场无独立 suggest 索引，则提供可选初始化模板。
- **测试:** 前缀、中文、空输入、最大条数、P95 基线脚本。
- **验收:** Suggest 路径产生零 Vespa 请求。
- **提交:** Yes。

## Task 2.6 — ES 基线报告工具

- **Owner:** Codex + Human
- **目标:** 为 Vespa/RRF 对照冻结 ES 结果版本。
- **前置:** 2.4。
- **实现:** 批量执行查询 TSV；导出 TopK、请求 JSON、耗时和零结果；生成 `result_version=es-v1` 报告模板。
- **测试:** 相同输入重复运行结果可复现。
- **验收:** Human 可以使用工具标记 ES 基线，不要求 Codex伪造相关性。
- **提交:** Yes。

---

# S3 — Vespa Docker、Application Package 与中文 Gate

## Task 3.1 — Vespa 镜像锁定与 Compose

- **Owner:** Codex + Operator
- **目标:** 单节点 Vespa 在 Linux 稳定运行。
- **前置:** S0。
- **实现:** 选择具体完整 tag，记录 digest；独立 Compose；内部网络；var/log 持久化；文件描述符和资源配置；宿主机前置检查。
- **测试:** up/down/restart、数据持久化、外部端口不可访问。
- **验收:** 镜像不是模糊 `8.x`；README 记录 tag、digest 和资源要求。
- **提交:** Yes。

## Task 3.2 — services.xml 与基础 news.sd

- **Owner:** Codex
- **目标:** 部署可查询、可写入、可条件更新的基础 Schema。
- **前置:** 3.1。
- **实现:** document/search API；content cluster；字段包含 newsId/sourceId/sourceType/indexVersion/contentHash/title/content/publisher/author/publishTime；过滤字段 attribute 化；准备多个候选 rank profile。
- **测试:** Application Package validate/deploy 无错误。
- **验收:** indexVersion 可用于条件表达式；Schema 不含 tensor/vector 字段。
- **提交:** Yes。

## Task 3.3 — 部署和运维脚本

- **Owner:** Codex + Operator
- **目标:** 不进入容器即可部署和诊断。
- **前置:** 3.2。
- **实现:** deploy-app、health-check、up/down/logs/reset-dev-data；reset 有生产保护；脚本失败返回非零。
- **测试:** 正常和故意损坏 Schema 两条路径。
- **验收:** 第二位人员按 README 可以部署。
- **提交:** Yes。

## Task 3.4 — Vespa Document API CRUD 与条件写入

- **Owner:** Codex
- **目标:** 证明原子版本控制可用。
- **前置:** 3.3。
- **实现:** Upsert/Get/Update/Delete；test-and-set condition；create-if-absent；区分 condition failed、4xx、5xx、timeout。
- **测试:** v10→v12→v11；条件不满足；不存在文档 create；删除；中文往返。
- **验收:** 旧版本不能覆盖新版本；条件失败不被分类为瞬时故障。
- **提交:** Yes。

## Task 3.5 — Vespa C# Document Client

- **Owner:** Codex
- **目标:** 提供 typed HttpClient。
- **前置:** 3.4。
- **实现:** IVespaDocumentClient；named HttpClient；URI 编码；错误分类；HTTP/1.1；条件参数；取消和 timeout。
- **测试:** mocked handler + live container CRUD。
- **验收:** 无 socket exhaustion 模式；日志不包含正文。
- **提交:** Yes。

## Task 3.6 — 中文候选 Rank Profiles 与查询工具

- **Owner:** Codex
- **目标:** 同时支持 plan §11.3 的 A–E 对比。
- **前置:** 3.2、3.5。
- **实现:** gram all/weakAnd、BM25/native-proximity 候选；可选预分词字段接口；publisher/author text/filter 分离；trace 导出；查询参数化。
- **测试:** 每个 profile 可部署和运行；YQL 注入测试；query tree 报告。
- **验收:** 不在代码中提前宣布某 profile 胜出。
- **提交:** Yes。

## Task 3.7 — 中文 POC 数据与 pooled 导出

- **Owner:** Codex + Human
- **目标:** 形成真实中文质量 Gate。
- **前置:** 3.6、2.6。
- **实现:** 导入 ≥2,000 匿名化文档；执行 ≥30 查询；汇总 ES 和 Vespa 各 profile Top10；生成盲标注 TSV 与 rubric；计算工具支持 NDCG/MRR/Recall/Overlap。
- **测试:** 指标工具使用手算样例验证。
- **验收:** Human 完成标注并产生 `docs/baseline/vespa-cjk-poc.md`；Codex 不填写主观标签。
- **提交:** Yes（工具和模板）；Human（标签）。

## Task 3.8 — 中文技术 Gate 裁决

- **Owner:** Human
- **目标:** 决定是否继续 S4+。
- **前置:** 3.7。
- **实现:** 选择最佳 Vespa profile；检查 NDCG≥ES×0.75、独有相关结果、拆词错误和 P95；记录通过/失败。
- **测试:** 双人标注一致性报告。
- **验收:** 通过则将 profile 写入配置默认值；失败则停止并输出 blocker，或要求实现预分词 fallback 后重跑。
- **提交:** Yes（报告）。

---

# S4 — 持久化双写、重试与回填

## Task 4.1 — ES/Vespa Index Sink 抽象

- **Owner:** Codex
- **目标:** 两个引擎共享同一 NewsSearchDocument。
- **前置:** S1、2.3、3.5、3.8 passed。
- **实现:** IIndexSink；ElasticIndexSink；VespaIndexSink；独立 appliedVersion/status；配置开关。
- **测试:** 一个 sink 失败不影响另一个；同一对象实例/相同 hash。
- **验收:** 禁止两个 sink 独立清洗 HTML。
- **提交:** Yes。

## Task 4.2 — Outbox Worker 与同 newsId 串行

- **Owner:** Codex
- **目标:** 可靠消费最新期望状态。
- **前置:** 1.5、4.1。
- **实现:** BackgroundService；原子 claim；同一 newsId 不并行；每次加载最新 Desired 状态；新版本合并旧触发器；优雅停机。
- **测试:** 多 Worker、进程中断、并发新版本、重复 trigger。
- **验收:** 不堆积过期 payload；最终一致到最高版本。
- **提交:** Yes。

## Task 4.3 — 重试、Dead 状态与补偿

- **Owner:** Codex
- **目标:** 不静默丢失索引更新。
- **前置:** 4.2。
- **实现:** 指数退避；瞬时/永久/stale 分类；最大次数；dead 状态；手工 retry API/CLI；错误摘要脱敏。
- **测试:** 5xx、429、timeout、400、版本冲突、条件失败。
- **验收:** stale 不重试；Vespa 停止 10 分钟后恢复可补齐。
- **提交:** Yes。

## Task 4.4 — 崩溃与不确定写入恢复

- **Owner:** Codex
- **目标:** 处理“引擎已成功但进程未记状态”。
- **前置:** 4.3。
- **实现:** 重试同版本操作；依赖 ES 外部版本/Vespa 条件写幂等；写后记录 appliedVersion；对 timeout 后未知结果安全重放。
- **测试:** 在引擎响应后、SQLite commit 前强制崩溃；恢复后最终一致。
- **验收:** 不需要读取后比较；安全重放无副作用。
- **提交:** Yes。

## Task 4.5 — 历史回填工具

- **Owner:** Codex + Operator
- **目标:** 从 JSONL/CSV 或批量 API 将已有新闻送入服务。
- **前置:** 4.3。
- **实现:** 流式读取；按 newsId checkpoint；批量大小/并发配置；断点续传；进度、吞吐和 ETA；不使用 offset 分页假设。
- **测试:** 中途 kill 后恢复；坏记录隔离；重复导入。
- **验收:** 不丢、不重、可恢复；回填完成标志只有一致性通过后设置。
- **提交:** Yes。

## Task 4.6 — 单篇、范围和全量重建

- **Owner:** Codex
- **目标:** 运维可修复索引。
- **前置:** 4.5。
- **实现:** reindex by newsId、publishTime range、full；重置引擎 applied state；危险操作确认。
- **测试:** 三种模式和删除 tombstone。
- **验收:** 不需要重新部署代码即可修复。
- **提交:** Yes。

## Task 4.7 — 一致性检查

- **Owner:** Codex
- **目标:** 持续检测 ES/Vespa/SQLite 漂移。
- **前置:** 4.6。
- **实现:** 全量及 sourceType 计数；最近 1h/24h 同步率；随机 hash 抽样；最大滞后；积压；输出修复 ID。
- **测试:** 注入计数、hash、近期新增和积压异常。
- **验收:** 检查结果供 readiness 和自动降级使用。
- **提交:** Yes。

---

# S5 — 双引擎查询、RRF 与分页

## Task 5.1 — SearchQuery、SearchCandidate 与适配器接口

- **Owner:** Codex
- **目标:** 两引擎过滤模型闭合集合。
- **前置:** S2/S3。
- **实现:** immutable SearchQuery；SearchCandidate；ISearchEngineAdapter；禁止 extra dictionary。
- **测试:** 反射测试保证新增查询字段必须被两个 adapter 显式处理。
- **验收:** 所有 plan §6.4 过滤字段均存在。
- **提交:** Yes。

## Task 5.2 — Vespa Search Adapter

- **Owner:** Codex
- **目标:** 使用 S3 胜出 profile 查询 TopK。
- **前置:** 3.8、5.1。
- **实现:** 参数化 YQL；filter parity；rank inputs；soft timeout；trace debug；动态摘要/highlight fallback；1-based rank。
- **测试:** 每种 filter、引号/元字符注入、取消、timeout、空结果。
- **验收:** 用户关键词不通过字符串拼接改变 YQL 结构。
- **提交:** Yes。

## Task 5.3 — Filter Parity Gate

- **Owner:** Codex
- **目标:** 两引擎在无关键词过滤查询下返回相同可见集合。
- **前置:** 2.4、5.2。
- **实现:** Fixture 覆盖 sourceType、时间、publisher、author 及未来扩展字段；比较 newsId sets；CI required test。
- **测试:** 每项单独与组合。
- **验收:** 全部集合一致；新增 SearchQuery 字段时测试先失败。
- **提交:** Yes。

## Task 5.4 — 并发 Orchestrator

- **Owner:** Codex
- **目标:** 安全并行请求两个引擎。
- **前置:** 5.1、5.2。
- **实现:** 独立 linked CTS；per-engine/global timeout；异常隔离；并发发起；延迟记录；配置校验。
- **测试:** 一快一慢、一抛错、都抛错、取消、global timeout。
- **验收:** 慢 Vespa 不取消或拖死 ES，反之亦然。
- **提交:** Yes。

## Task 5.5 — 熔断与 SearchMode

- **Owner:** Codex
- **目标:** 支持 EsOnly/VespaOnly/Rrf/Shadow。
- **前置:** 5.4。
- **实现:** 独立 circuit breaker；half-open；模式配置；审计日志；EsOnly 零 Vespa 调用；Shadow 响应与 ES 一致。
- **测试:** 状态转换和模式切换。
- **验收:** 模式错误不会绕过 readiness Gate。
- **提交:** Yes。

## Task 5.6 — RRF 纯函数

- **Owner:** Codex
- **目标:** 实现加权、可配置 RRF。
- **前置:** 5.1。
- **实现:** 1-based rank；缺失项不贡献；newsId 去重；保留来源数据；无 I/O 依赖。
- **测试:** 手算分数、相同/不同/部分重叠、单引擎、空、重复、权重 0、非默认 k。
- **验收:** 原始 ES/Vespa 分数绝不直接相加。
- **提交:** Yes。

## Task 5.7 — 确定性 Tie-break

- **Owner:** Codex
- **目标:** 跨机器和输入顺序稳定。
- **前置:** 5.6。
- **实现:** 双引擎→最佳单引擎名次→新时间→newsId Ordinal。
- **测试:** 每层单独测试；打乱输入 100 次结果一致。
- **验收:** 任意两个不同 newsId 不 compare equal。
- **提交:** Yes。

## Task 5.8 — 固定窗口分页

- **Owner:** Codex
- **目标:** 防止跨页重复、遗漏和重排。
- **前置:** 5.6、5.7。
- **实现:** Top50 一次融合；再切 page；超过深度返回 maxDepthReached；Swagger 说明；深分页 ES-only 边界。
- **测试:** page1/2/3/超限；跨页集合无重复无遗漏。
- **验收:** 禁止逐页重新查询并融合。
- **提交:** Yes。

## Task 5.9 — 降级响应 wiring

- **Owner:** Codex
- **目标:** 正常和降级保持相同 DTO。
- **前置:** 5.4、5.8。
- **实现:** ES+Vespa→RRF；ES-only fallback；Vespa-only fallback；两者失败→5xx；degradationMode。
- **测试:** 四条路径。
- **验收:** 总失败不返回空 200。
- **提交:** Yes。

---

# S6 — 埋点与离线评估

## Task 6.1 — searchTraceId 全链路

- **Owner:** Codex
- **目标:** 关联查询、结果、曝光和点击。
- **前置:** S5。
- **实现:** 每次搜索新 UUID；响应 header/body；传入日志、adapter、fusion 和 DB。
- **测试:** 一个请求所有记录 ID 一致，不同请求不复用。
- **验收:** 不直接复用可能跨重试的上游 correlation id。
- **提交:** Yes。

## Task 6.2 — 查询级埋点

- **Owner:** Codex
- **目标:** 保存参数、耗时、模式和降级。
- **前置:** 6.1。
- **实现:** plan §14.1 字段；resultVersion；异步持久化；查询保留/脱敏配置；不保存正文。
- **测试:** 字段完整、异常路径、埋点失败不阻塞搜索。
- **验收:** 可按 resultVersion 分区计算。
- **提交:** Yes。

## Task 6.3 — 结果与曝光埋点

- **Owner:** Codex
- **目标:** 保存每条融合结果的引擎来源。
- **前置:** 6.2。
- **实现:** TopN 结果表；impression API；验证 trace/result 对；幂等曝光；数据库索引。
- **测试:** 只允许当前 trace 返回结果被曝光。
- **验收:** presentInEs/presentInVespa 与候选一致。
- **提交:** Yes。

## Task 6.4 — 点击和停留时间

- **Owner:** Codex
- **目标:** 建立防污染点击链路。
- **前置:** 6.3。
- **实现:** click API；验证 pair；幂等；clickPosition；dwellTime 后补；重复点击策略配置。
- **测试:** 正常、重复、伪造 ID、过期 trace。
- **验收:** 伪造记录被拒绝且可审计。
- **提交:** Yes。

## Task 6.5 — 指标计算器

- **Owner:** Codex
- **目标:** 计算 ES/Vespa/RRF 可解释指标。
- **前置:** 6.4。
- **实现:** Clicked Recall、Click MRR、unique click rate、Overlap、latency percentile、degradation、zero result、sync metrics；按 resultVersion 分组。
- **测试:** 手构数据与手算结果。
- **验收:** 不使用预聚合平均值计算百分位数。
- **提交:** Yes。

## Task 6.6 — Offline Evaluation Harness

- **Owner:** Codex + Human
- **目标:** 消除点击位置偏差，形成共同标注池。
- **前置:** 6.5。
- **实现:** 抽样查询；汇总 ES/Vespa/RRF Top10 去重；盲化排序来源；0–3 rubric；NDCG/MRR/Recall；可重复报告。
- **测试:** textbook 指标样例；相同 judgement 重跑一致。
- **验收:** Human 标注；Codex 不得自己填写主观相关性。
- **提交:** Yes（工具/模板/计算）；Human（标签）。

## Task 6.7 — RRF 上线质量 Gate 报告

- **Owner:** Human
- **目标:** 确认 RRF 不低于 ES。
- **前置:** 6.6。
- **实现:** 比较 NDCG@10、MRR@10、Recall@20；标记是否满足 plan §13.2；记录相对提升和无显著提升风险。
- **测试:** 双人复核。
- **验收:** 明确输出 `PASS_RRF`、`KEEP_ES_ONLY` 或 `NEED_TUNING`。
- **提交:** Yes（报告）。

---

# S7 — Readiness、自动降级、内网交付与运行手册

## Task 7.1 — Vespa Readiness Gate

- **Owner:** Codex
- **目标:** 未回填或数据漂移时禁止 Rrf/VespaOnly。
- **前置:** 4.7、5.5。
- **实现:** backfillComplete；最近 1h/24h 同步率；count/hash；最大滞后；Outbox backlog；模式切换前检查。
- **测试:** 每项刚好低于/高于阈值。
- **验收:** 不通过时强制 EsOnly，并记录原因。
- **提交:** Yes。

## Task 7.2 — 自动降级与人工恢复

- **Owner:** Codex
- **目标:** 漂移或故障自动切 ES，禁止抖动回切。
- **前置:** 7.1。
- **实现:** 自动 EsOnly；告警；恢复 API/命令要求 operator 和 reason；审计。
- **测试:** 触发、重复触发、人工恢复、恢复前 Gate 失败。
- **验收:** 无自动恢复 Rrf。
- **提交:** Yes。

## Task 7.3 — Search 健康和诊断端点

- **Owner:** Codex
- **目标:** 运维可查看实时状态。
- **前置:** 7.2。
- **实现:** ES/Vespa reachability、breaker、mode、readiness、last consistency、backlog、last sync、resultVersion；结果缓存避免健康检查制造负载。
- **测试:** 停止容器、注入 drift、切模式。
- **验收:** 状态在一个检查周期内更新。
- **提交:** Yes。

## Task 7.4 — Chaos 降级测试

- **Owner:** Codex + Operator
- **目标:** 使用真实容器验证故障路径。
- **前置:** 7.3。
- **实现:** Vespa stop/slow/5xx/malformed/zero；ES stop/slow；两者 down；SQLite lock；Worker backlog。
- **测试:** Linux 集成测试和操作脚本。
- **验收:** 所有场景在 global timeout 内按文档响应。
- **提交:** Yes。

## Task 7.5 — 内网镜像和 NuGet 包

- **Owner:** Codex + Operator
- **目标:** 断网部署闭包。
- **前置:** S0–S7 package 稳定。
- **实现:** docker save/load；digest/sha 校验；应用与 Vespa 镜像；全部 nupkg 和 lock files；离线 restore；传输清单。
- **测试:** 网络完全禁用的干净机器部署。
- **验收:** 无任何公网请求；篡改包校验失败。
- **提交:** Yes。

## Task 7.6 — 运行手册

- **Owner:** Codex + Operator
- **目标:** 所有操作有可执行步骤。
- **前置:** 7.5。
- **实现:** deploy、disable-vespa、enable-rrf、reindex、repair、restart、rollback、SQLite backup、offline install；命令、预期输出、验证和回退。
- **测试:** 非作者人员逐份演练。
- **验收:** 无口头补充步骤。
- **提交:** Yes。

## Task 7.7 — 埋点保留与清理

- **Owner:** Codex
- **目标:** 防止 SQLite 无限增长。
- **前置:** S6。
- **实现:** 配置化 retention；批量清理；VACUUM 策略；指标摘要导出后删除明细；运行时限流。
- **测试:** 过期/未过期、批量边界、并发查询期间清理。
- **验收:** 清理不长时间锁住搜索请求。
- **提交:** Yes。

---

# S8 — 验收、性能、回滚和生产模式裁决

## Task 8.1 — 端到端功能验收

- **Owner:** Codex + Operator
- **目标:** 验证 plan §19 全部标准。
- **前置:** S0–S7。
- **实现:** 自动脚本 + 人工证据；生成 `docs/acceptance/e2e-report.md`。
- **测试:** Linux staging 全套。
- **验收:** 每条标准有命令输出或测试证据。
- **提交:** Yes（报告）。

## Task 8.2 — 全量一致性验收

- **Owner:** Codex + Operator
- **目标:** 验证 SQLite/ES/Vespa 数据完整。
- **前置:** 8.1。
- **实现:** 全量和 sourceType count；最近数据；≥2,000 hash 抽样；≥50 更新删除验证。
- **测试:** checker + 人工抽查。
- **验收:** 启用 RRF 阈值全部通过且留有余量。
- **提交:** Yes（报告）。

## Task 8.3 — 性能验收

- **Owner:** Codex + Operator
- **目标:** 验证双引擎总延迟和吞吐。
- **前置:** 8.1。
- **实现:** 单用户、峰值并发、P50/P95/P99、per-engine、fusion overhead、timeout fallback、回填吞吐、SQLite 写入。
- **测试:** 真实查询混合压测。
- **验收:** Fusion P95 开销目标 <20ms；总 P95 满足项目 SLA；否则先调 timeout/TopK，不擅自放宽 SLA。
- **提交:** Yes（报告）。

## Task 8.4 — 回滚演练

- **Owner:** Operator
- **目标:** 证明可快速恢复 EsOnly。
- **前置:** 8.1。
- **实现:** 执行模式切换、停止 Vespa、关闭 Vespa sink、完整撤回、Vespa 重建；测量生效时间。
- **测试:** staging 真实演练。
- **验收:** EsOnly 查询稳定；Vespa 故障不影响 Index API 接收和 ES。
- **提交:** Yes（报告）。

## Task 8.5 — 生产 RRF 启用裁决

- **Owner:** Human
- **目标:** 不强制 Shadow，两套 Gate 通过后决定生产模式。
- **前置:** 3.8、5.3、6.7、8.2、8.3、8.4。
- **实现:** 检查所有硬 Gate；产品接受分页深度；决定 `Rrf`、`Shadow`、`EsOnly` 或 `NeedTuning`。
- **测试:** 技术负责人和产品共同签字。
- **验收:** 明确书面裁决；选择 Rrf 时生产配置显式设为 Rrf，应用默认安全值仍可为 EsOnly。
- **提交:** Yes（报告/配置）。

## Task 8.6 — 最终交付包

- **Owner:** Codex + Operator
- **目标:** 可交接、可重建、可回滚。
- **前置:** 8.5。
- **实现:** README、架构图、API 文档、配置参考、镜像与 NuGet 清单、运行手册索引、known limitations、最终报告。
- **测试:** 干净离线环境只按文档部署。
- **验收:** 第二位人员无需询问开发者即可完成部署和基础验证。
- **提交:** Yes。

---

# Gate 汇总

| Gate | Owner | 阻塞范围 |
|---|---|---|
| G1 .NET 6 干净构建与离线 restore | Codex + Operator | 全部 |
| G2 Vespa 条件写入与中文技术 Gate | Codex + Human | S4+ |
| G3 ES/Vespa Filter Parity | Codex | RRF 上线 |
| G4 Vespa 全量回填和数据 Readiness | Codex + Operator | Rrf/VespaOnly |
| G5 离线质量：RRF 不低于 ES | Human | 生产 Rrf |
| G6 故障降级、性能和回滚 | Operator | 生产发布 |

# Codex 最终停止条件

Codex 在以下情况必须停止并提交阻塞报告：

1. 无法在 Windows 7 目标条件下完成 net6.0 基础构建；
2. 无法连接指定 ES 7.x 或 IK analyzer 不存在；
3. Vespa Schema 无法支持所需条件写入；
4. 中文 Gate 失败且用户未批准预分词 fallback；
5. RRF 离线质量低于 ES；
6. 过滤集合不一致；
7. 数据完整性 Gate 不通过；
8. 双引擎延迟超过 SLA 且无法通过 timeout/TopK 调整解决；
9. 离线部署仍存在公网依赖。
