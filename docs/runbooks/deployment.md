# Linux Docker 部署运行说明

## 前置

- Linux x86_64、Docker Engine/Compose v2、至少 4 CPU/8 GiB 可供单节点 Vespa POC；
- `vm.max_map_count`、磁盘和 nofile 按现场容量评估，容器 nofile 配置为 262144；
- 预加载应用镜像、`vespaengine/vespa:8.721.11` 和 NuGet 全局缓存；
- ES 7.x/IK 连通信息由环境变量提供。

## Vespa

```sh
export SEARCH_NETWORK=dual-news-search
export SEARCH_UPSTREAM_NETWORK=dual-news-search-upstream
sh deploy/vespa/scripts/up.sh
sh deploy/vespa/scripts/health-check.sh
sh deploy/vespa/scripts/deploy-app.sh
```

验证 `19071/state/v1/health` 与 `8080/ApplicationStatus`，执行 samples CRUD、v10→v12→v11 和 delete→旧 upsert 条件写测试。端口只暴露到内部 Docker 网络。

## 应用

设置 `APP_IMAGE/ELASTICSEARCH_ENDPOINT/ELASTICSEARCH_INDEX_ALIAS/VESPA_ENDPOINT`，然后：

```sh
sh deploy/app/scripts/up.sh
sh deploy/app/scripts/health.sh
```

`search-internal` 是只承载应用到 Vespa 的 internal bridge；`search-upstream` 用于访问现场 ES。生产主机必须通过防火墙/路由只允许该网络访问批准的 ES 与日志端点，禁止公网出口。

首次保持 `SearchMode__Default=EsOnly` 和 `Readiness__BackfillComplete=false`。回填、一致性、中文 Gate、过滤 Gate、质量/Chaos/性能/回滚全部通过后，设置 backfillComplete 并调用 mode API，由 operator+reason 人工启用 RRF。

## 回滚

1. `POST /api/v1/search-health/mode` 切 `EsOnly`。
2. 必要时设置 `Indexing__VespaSinkEnabled=false` 后滚动重启，ES sink 和 Index API 继续。
3. 备份 SQLite volume，再停止/重建 Vespa。
4. 回填、count/hash/readiness 全通过后才允许人工恢复；系统不会自动回切。

SQLite 备份应在一致性点使用 SQLite backup API 或短暂停写后的卷快照，不直接复制活跃 WAL 主文件。
