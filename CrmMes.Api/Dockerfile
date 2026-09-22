# Multi-stage build for CrmMes.Api. Build the image from the repository root:
#   docker build -f CrmMes.Api/Dockerfile -t crmmes-api .
# See docker-compose.yml at the repo root for local usage with required env vars.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CrmMes.Api/CrmMes.Api.csproj CrmMes.Api/
COPY CrmMes.Core/CrmMes.Core.csproj CrmMes.Core/
RUN dotnet restore CrmMes.Api/CrmMes.Api.csproj

COPY CrmMes.Api/ CrmMes.Api/
COPY CrmMes.Core/ CrmMes.Core/
RUN dotnet publish CrmMes.Api/CrmMes.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# La porta è configurabile via $PORT (Render e altri host la impostano automaticamente);
# 8080 resta il default per l'uso locale con docker-compose.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet CrmMes.Api.dll"]
