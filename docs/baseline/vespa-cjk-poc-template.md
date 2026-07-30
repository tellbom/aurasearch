# Vespa 中文技术 Gate

状态：`NOT_RUN`

- 数据集：至少 2,000 篇真实匿名化新闻/公告
- 查询：至少 30 条，覆盖热门/中频/长尾/公司名/公告类型/多词
- 候选：ES IK + Vespa A–E
- 标注：两名人工，0–3 等级
- 指标：NDCG@10、MRR@10、Recall@20、Overlap@10、P50/P95/P99

| 条件 | 结果 | 证据 |
|---|---|---|
| 最佳 Vespa NDCG >= ES × 0.75 | NOT_RUN | |
| 有非零独有相关结果 | NOT_RUN | |
| 无系统性中文拆分错误 | NOT_RUN | |
| P95 符合总 timeout | NOT_RUN | |

不得在没有真实数据与双人标注时选择胜出 profile。

