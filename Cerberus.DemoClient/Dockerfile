# Build context: repository root (docker compose sets it).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY Cerberus.DemoClient/Cerberus.DemoClient.csproj Cerberus.DemoClient/
RUN dotnet restore Cerberus.DemoClient/Cerberus.DemoClient.csproj
COPY Cerberus.DemoClient/ Cerberus.DemoClient/
RUN dotnet publish Cerberus.DemoClient/Cerberus.DemoClient.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8443
ENTRYPOINT ["dotnet", "Cerberus.DemoClient.dll"]
