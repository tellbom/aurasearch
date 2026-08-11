# Linux Docker 部署运行说明

## 前置

- Linux x86_64、Docker Engine/Compose v2、至少 4 CPU/8 GiB 可供单节点 Vespa POC；
- `vm.max_map_count`、磁盘和 nofile 按现场容量评估，容器 nofile 配置为 262144；
- 预加载批准的 Elasticsearch 7.x/IK 镜像、`vespaengine/vespa:8.721.11`；开发机准备固定的 .NET 6 SDK 和 NuGet 缓存；
- ES 7.x/IK 连通信息由环境变量提供。

## 搜索依赖

```sh
sh deploy/linux-mvp/dependencies-up.sh
```

脚本只启动 Elasticsearch 与 Vespa Docker 服务并验证基础版本/健康，不再创建 ES index/alias，也不部署 Vespa Application Package。API 必须能访问 ES `29200`、Vespa Query/Document `28080` 和 Vespa Config `29071`。

## API

API 固定使用 .NET 6 并在开发机运行。设置：

```text
Elasticsearch__Endpoint=http://<mapped-host>:29200
Elasticsearch__IndexName=news-v1
Elasticsearch__IndexAlias=news-read
Vespa__Endpoint=http://<mapped-host>:28080
Vespa__ConfigEndpoint=http://<mapped-host>:29071
```

API 启动阶段自动创建缺失的 ES index/alias，并将内嵌 Vespa Application Package 执行 `prepareandactivate`。配置修改随 API 构建发布，不再登录服务器修改。

首次保持 `SearchMode__Default=EsOnly` 和 `Readiness__BackfillComplete=false`。回填、一致性、中文 Gate、过滤 Gate、质量/Chaos/性能/回滚全部通过后，设置 backfillComplete 并调用 mode API，由 operator+reason 人工启用 RRF。

## 回滚

1. `POST /api/v1/search-health/mode` 切 `EsOnly`。
2. 必要时设置 `Indexing__VespaSinkEnabled=false` 后滚动重启，ES sink 和 Index API 继续。
3. 备份开发机上的 SQLite 数据库，再停止/重建 Vespa。
4. 回填、count/hash/readiness 全通过后才允许人工恢复；系统不会自动回切。

SQLite 备份应在一致性点使用 SQLite backup API 或短暂停写后的卷快照，不直接复制活跃 WAL 主文件。
