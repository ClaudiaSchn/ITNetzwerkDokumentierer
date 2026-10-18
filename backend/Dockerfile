# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NetzDoku.Api/NetzDoku.Api.csproj NetzDoku.Api/
RUN dotnet restore NetzDoku.Api/NetzDoku.Api.csproj
COPY . .
RUN dotnet publish NetzDoku.Api/NetzDoku.Api.csproj -c Release -o /app --no-restore

# Laufzeit (lauscht im Container auf Port 8080)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "NetzDoku.Api.dll"]
