# Build context: repository root (docker compose sets it).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY Cerberus.Domain/Cerberus.Domain.csproj Cerberus.Domain/
COPY Cerberus.Application/Cerberus.Application.csproj Cerberus.Application/
COPY Cerberus.Infrastructure/Cerberus.Infrastructure.csproj Cerberus.Infrastructure/
COPY Cerberus.Web/Cerberus.Web.csproj Cerberus.Web/
RUN dotnet restore Cerberus.Web/Cerberus.Web.csproj
COPY Cerberus.Domain/ Cerberus.Domain/
COPY Cerberus.Application/ Cerberus.Application/
COPY Cerberus.Infrastructure/ Cerberus.Infrastructure/
COPY Cerberus.Web/ Cerberus.Web/
RUN dotnet publish Cerberus.Web/Cerberus.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8443
ENTRYPOINT ["dotnet", "Cerberus.Web.dll"]
