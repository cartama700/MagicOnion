# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/Shared.csproj         Shared/
COPY BotClients/BotClients.csproj BotClients/
RUN dotnet restore BotClients/BotClients.csproj

COPY Shared/      Shared/
COPY BotClients/  BotClients/
RUN dotnet publish BotClients/BotClients.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "BotClients.dll"]
