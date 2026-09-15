FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ScanFace.SyncApi/ScanFace.SyncApi.csproj -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir /data
ENV ASPNETCORE_URLS=http://+:8080
ENV SCANFACE_DB_PATH=/data/scanface-sync.db
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "ScanFace.SyncApi.dll"]
