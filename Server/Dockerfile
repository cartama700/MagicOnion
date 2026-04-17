# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore (cache-friendly)
COPY Shared/Shared.csproj         Shared/
COPY Server/Server.csproj         Server/
RUN dotnet restore Server/Server.csproj

# build + publish
COPY Shared/  Shared/
COPY Server/  Server/
RUN dotnet publish Server/Server.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=
ENV DOTNET_gcServer=1
EXPOSE 5050 5001
ENTRYPOINT ["dotnet", "Server.dll"]
