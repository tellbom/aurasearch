# 当前生产 Gate 阻塞报告

日期：2026-07-30

代码、配置、自动测试、部署物和报告模板已实现，但当前 Windows 环境没有 Docker CLI、真实 ES 7.x/IK、Vespa 容器、2,000 篇匿名化数据、30 条真实查询或人工标注，因此以下结论仍为 `NOT_RUN`：

1. G1 纯内网干净机 restore/deploy；
2. G2 Vespa Application Package 实机 validate、条件写与中文 Gate；
3. G3 真实 ES/Vespa filter parity；
4. G4 全量回填、引擎 count/sourceType/hash readiness；
5. G5 双人人工标注后的 RRF 质量；
6. G6 Linux Chaos、性能 SLA 与回滚演练。

调整说明：用户要求不等待阶段确认并继续完整开发，因此在 G2 无法实机裁决时继续完成了后续代码和工具；这不等同于绕过生产 Gate。安全默认保持 `EsOnly`、`BackfillComplete=false`，mode API 在 readiness 未通过时拒绝 `Rrf/VespaOnly`。

解除阻塞所需动作见 `e2e-report.md`。任何未执行项不得写成 PASS。

