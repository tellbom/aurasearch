# 测试报告

日期：2026-07-30

执行命令：

```powershell
dotnet restore DualNewsSearch.sln --locked-mode
dotnet build DualNewsSearch.sln -c Release --no-restore
dotnet test DualNewsSearch.sln -c Release --no-build --no-restore
dotnet restore tools/evaluation/DualNewsSearch.Evaluation.csproj --locked-mode
dotnet build tools/evaluation/DualNewsSearch.Evaluation.csproj -c Release --no-restore
dotnet restore tools/backfill/DualNewsSearch.Backfill.csproj --locked-mode
dotnet build tools/backfill/DualNewsSearch.Backfill.csproj -c Release --no-restore
```

本机证据：

- SDK 6.0.402；
- Release build 0 warning / 0 error；
- UnitTests：46；
- IntegrationTests：2；
- evaluation/backfill tools：0 warning / 0 error。
- JSON：15 个可解析；
- XML：2 个可解析；
- Shell：16 个通过 `bash -n`；
- `git diff --check`：通过；
- 私网地址硬编码扫描：0。

已修复的测试发现：不存在 offline source、误用高版本 `AddProblemDetails`、Outbox 重复 insert、HTML table/tab 归一化、SQLite DateTimeOffset 不可翻译、migration/model 列名不一致。

限制：本机没有 Docker CLI，未运行真实 ES/Vespa/Linux/Chaos/性能/离线恢复测试；这些条目在 Gate 报告中保持 `NOT_RUN`。
