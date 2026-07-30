# E2E 验收报告

## 当前自动证据

- .NET 6 Release build：见 `test-report.md`。
- 单元测试：HTML/hash/state machine/RRF/tie-break/分页/降级/metrics/options/request construction。
- 无容器集成测试：真实 SQLite migration、live health、ES/Vespa 均不可用时 Index API 仍接受。
- 部署物静态审计：services.xml 可解析，Schema 不含 tensor/embedding/onnx。

## Linux staging 待执行

| 项目 | 命令/证据 | 状态 |
|---|---|---|
| Vespa validate/deploy | deploy-app + config log | NOT_RUN |
| Document CRUD/condition | v10→v12→v11/delete复活 | NOT_RUN |
| ES external version/IK | live integration output | NOT_RUN |
| ≥2,000 回填与 ≥50 更新删除 | checker output | NOT_RUN |
| count/sourceType/hash ≥2,000 | consistency report | NOT_RUN |
| filter parity | newsId set diff | NOT_RUN |
| 中文人工 Gate | vespa-cjk-poc report | NOT_RUN |
| RRF 人工质量 Gate | PASS_RRF/KEEP_ES_ONLY/NEED_TUNING | NOT_RUN |
| Chaos | `deploy/tests/chaos.sh` + responses | NOT_RUN |
| 性能 | raw samples；fusion P95 <20ms；项目 SLA | NOT_RUN |
| 回滚 | EsOnly/stop Vespa/disable sink/rebuild | NOT_RUN |
| 断网恢复 | clean host network disabled | NOT_RUN |

