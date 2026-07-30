# NuGet 依赖清单

所有版本已固定，且每个项目生成 `packages.lock.json`。以下为 `dotnet list DualNewsSearch.sln package --include-transitive` 的生产闭包摘要；测试闭包以测试项目 lock file 为准。

## 直接依赖

- HtmlAgilityPack 1.11.54
- Microsoft.EntityFrameworkCore.Design 6.0.25
- Microsoft.EntityFrameworkCore.Sqlite 6.0.25
- Microsoft.Extensions.Hosting.Abstractions 6.0.0
- Microsoft.Extensions.Http.Polly 6.0.25
- Microsoft.Extensions.Logging.Abstractions 6.0.0
- Microsoft.Extensions.Options 6.0.0
- Swashbuckle.AspNetCore 6.5.0

测试直接依赖：

- FluentAssertions 6.12.0
- Microsoft.AspNetCore.Mvc.Testing 6.0.25
- Microsoft.NET.Test.Sdk 17.7.2
- xunit / xunit.runner.visualstudio 2.5.3

## 生产传递依赖

- Humanizer.Core 2.8.26
- Microsoft.Data.Sqlite.Core 6.0.25
- Microsoft.EntityFrameworkCore / Abstractions / Analyzers / Relational / Sqlite.Core 6.0.25
- Microsoft.Extensions.ApiDescription.Server 6.0.5
- Microsoft.Extensions.Caching.Abstractions 6.0.0、Caching.Memory 6.0.1
- Microsoft.Extensions.Configuration.Abstractions 6.0.0
- Microsoft.Extensions.DependencyInjection 6.0.1、Abstractions 6.0.0
- Microsoft.Extensions.DependencyModel 6.0.0
- Microsoft.Extensions.FileProviders.Abstractions 6.0.0
- Microsoft.Extensions.Http 6.0.0
- Microsoft.Extensions.Logging 6.0.0
- Microsoft.Extensions.Primitives 6.0.0
- Microsoft.OpenApi 1.2.3
- Polly 7.2.2、Polly.Extensions.Http 3.0.0
- SQLitePCLRaw.bundle_e_sqlite3/core/lib.e_sqlite3/provider.e_sqlite3 2.1.2
- Swashbuckle.AspNetCore.Swagger/SwaggerGen/SwaggerUI 6.5.0
- System.Buffers 4.5.1
- System.Collections.Immutable 6.0.0
- System.Diagnostics.DiagnosticSource 6.0.1
- System.Memory 4.5.4
- System.Runtime.CompilerServices.Unsafe 6.0.0
- System.Text.Encodings.Web / System.Text.Json 6.0.0

测试平台和旧 netstandard/runtime 资产的完整 RID 列表较长，不在此手工复制；`tests/*/packages.lock.json` 是可机器校验的完整名称、版本、内容哈希与依赖关系清单，离线导出脚本打包实际 `.nuget/packages` 闭包。

