FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Docker
WORKDIR /src/
COPY . .
RUN dotnet restore
RUN dotnet publish ./TelegramAIBot.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS app
WORKDIR /app/
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TelegramAIBot.dll"]
