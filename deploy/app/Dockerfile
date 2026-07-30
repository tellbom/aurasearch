FROM mcr.microsoft.com/dotnet/sdk:6.0.414 AS build
WORKDIR /src
COPY . .
RUN dotnet restore DualNewsSearch.sln --locked-mode
RUN dotnet publish src/DualNewsSearch.Api/DualNewsSearch.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:6.0.25
WORKDIR /app
RUN addgroup --system app && adduser --system --ingroup app app
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DualNewsSearch.Api.dll"]

