# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json dotnet-tools.json ./
COPY src/Transit.Gtfs/Transit.Gtfs.csproj src/Transit.Gtfs/
COPY src/Transit.Data/Transit.Data.csproj src/Transit.Data/
COPY src/Transit.Ingestion/Transit.Ingestion.csproj src/Transit.Ingestion/
COPY src/Transit.Web/Transit.Web.csproj src/Transit.Web/
RUN dotnet tool restore \
    && dotnet restore src/Transit.Ingestion/Transit.Ingestion.csproj \
    && dotnet restore src/Transit.Web/Transit.Web.csproj

COPY src/ src/

FROM build AS publish-ingestion
RUN dotnet publish src/Transit.Ingestion/Transit.Ingestion.csproj --no-restore -c Release -o /app

FROM build AS publish-web
RUN dotnet publish src/Transit.Web/Transit.Web.csproj --no-restore -c Release -o /app

FROM build AS publish-migrations
RUN dotnet ef migrations bundle \
        --project src/Transit.Data \
        --startup-project src/Transit.Web \
        --configuration Release \
        --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrations
WORKDIR /app
COPY --from=publish-migrations /app ./
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec ./efbundle --connection \"$ConnectionStrings__Transit\""]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS ingestion
WORKDIR /app
COPY --from=publish-ingestion /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Transit.Ingestion.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=publish-web /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Transit.Web.dll"]
