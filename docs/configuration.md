# 配置参考

| Section | 关键字段 |
|---|---|
| ConnectionStrings | SearchDatabase（DM8 ADO.NET 连接字符串） |
| Elasticsearch | Endpoint、IndexName、IndexAlias、ProvisioningEnabled、TimeoutMs、ResultVersion |
| Vespa | Endpoint、ConfigEndpoint、ProvisioningEnabled、Namespace、DocumentType、RankProfile、TimeoutMs |
| Fusion | EsTopK、VespaTopK、FinalTopK、RankConstant、EsWeight、VespaWeight、MaxFusionDepth、GlobalTimeoutMs |
| Indexing | BatchSizeLimit、MaxRetryCount、WorkerPollIntervalMs、HtmlMaxLength、两个 sink 开关 |
| Telemetry | RetentionDays、CleanupBatchSize、StoreRawQuery、AllowRepeatedClicks |
| SearchMode | Default、RequireReadinessForVespa |
| Readiness | BackfillComplete、1h/24h 同步率、积压、最大滞后和检查间隔 |

环境变量使用 ASP.NET Core 双下划线映射，例如：

```text
Elasticsearch__Endpoint=http://192.168.124.2:29200
ConnectionStrings__SearchDatabase=Server=dm.internal;Port=5236;User Id=SYSDBA;Password=***;
Elasticsearch__IndexName=news-v1
Elasticsearch__IndexAlias=news-read
Vespa__Endpoint=http://192.168.124.2:28080
Vespa__ConfigEndpoint=http://192.168.124.2:29071
Fusion__RankConstant=60
SearchMode__Default=EsOnly
Readiness__BackfillComplete=false
```

生产配置不得提交密码或内网 IP。默认 `EsOnly` 和 `BackfillComplete=false` 是安全门禁，不应为了启动方便改成 RRF。
