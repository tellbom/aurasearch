# 纯内网安装

联网打包机运行 `tools/offline-package/export.ps1`，生成 NuGet 全局包缓存、应用/Vespa image tar 和 `SHA256SUMS`。传输后先运行 `verify.ps1`，再解压 NuGet 缓存到仓库 `.nuget/packages`，执行：

```powershell
dotnet restore DualNewsSearch.sln --configfile NuGet.Offline.Config.example --locked-mode
```

Linux 通过 `docker load` 导入两个 tar。Vespa export/import 脚本额外校验镜像 digest 与 tar SHA-256。验收机必须禁用公网网络，从空 Docker image cache 和空 NuGet cache 演练；任何 DNS/HTTP 公网请求均视为 G1/G6 失败。

