# Console fornitore di Nicolò MES. Build dalla cartella principale del repository:
#   docker build -f CrmMes.Console/Dockerfile -t crmmes-console .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CrmMes.Console/CrmMes.Console.csproj CrmMes.Console/
COPY CrmMes.Core/CrmMes.Core.csproj CrmMes.Core/
COPY CrmMes.Licensing/CrmMes.Licensing.csproj CrmMes.Licensing/
RUN dotnet restore CrmMes.Console/CrmMes.Console.csproj

COPY CrmMes.Console/ CrmMes.Console/
COPY CrmMes.Core/ CrmMes.Core/
COPY CrmMes.Licensing/ CrmMes.Licensing/
RUN dotnet publish CrmMes.Console/CrmMes.Console.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet CrmMes.Console.dll"]
