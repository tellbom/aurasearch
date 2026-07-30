# Elasticsearch 7.x 配置

1. 先连接现场集群，确认主版本为 7、目标 alias 可用、IK analyzer 存在。
2. 导出现有 `_mapping` 与 `_settings` 到 `docs/baseline/`，冻结 `result_version`。
3. 使用 `deploy/elasticsearch/create-index.ps1` 默认 dry-run 检查 mapping；只有新索引才允许 `-Apply`。脚本不删除、不覆盖已有索引。
4. 上线前由 Operator 将模板中的 IK 字段与现场既有查询核对；本项目不修改 IK 字典。

写入使用 `version={indexVersion}&version_type=external`；409 被分类为 stale，不无限重试。Suggest 只使用 ES。若查询权重或 IK 配置变化，必须提升 `Elasticsearch:ResultVersion`，不同版本指标不可混合。

