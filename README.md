# DualNewsSearch

ASP.NET Core 6.0 模块化单体，实现 Elasticsearch 7.x + Vespa 8.x 新闻公告关键词检索、BM25 候选排序和基于 rank 的 RRF 融合。项目明确不包含 RAG、Embedding、向量数据库、语义重排或 LLM 查询改写。

## 快速验证

```powershell
dotnet restore DualNewsSearch.sln --locked-mode
dotnet build DualNewsSearch.sln -c Release --no-restore
dotnet test DualNewsSearch.sln -c Release --no-build --no-restore
```

本地开发配置默认 `EsOnly`。地址、索引 alias、timeout、TopK、RRF k/权重、SQLite 路径和 readiness 阈值均可通过 `Section__Property` 环境变量覆盖。

## 目录

- `src/`：Domain、Application、Infrastructure、Worker、Api。
- `deploy/app/`：Linux 应用镜像和 Compose。
- `deploy/elasticsearch/`：ES 7.x IK mapping/alias 初始化模板。
- `deploy/vespa/`：Vespa 8.721.11 Application Package、Compose 和运维脚本。
- `tools/backfill/`：JSONL 历史回填、并发和 checkpoint。
- `tools/evaluation/`：盲化 pooled result 与 NDCG/MRR/Recall 计算。
- `docs/`：架构、API、同步、配置、运行手册、测试与 Gate 报告。

## 兼容边界

`global.json` 固定本机已验证的 SDK 6.0.402；Docker 构建镜像使用 6.0.414 SDK。Windows 7 仅承担编辑、构建和无容器单元测试；Linux 承担 Docker、真实 ES/Vespa 集成、性能、Chaos 与离线演练。.NET 6 和 Windows 7 均为存量兼容边界。

完整运行步骤见 [部署运行说明](docs/runbooks/deployment.md)，接口见 [API 文档](docs/api.md)，当前 Gate 状态见 [阻塞报告](docs/acceptance/blocker-report.md)。

