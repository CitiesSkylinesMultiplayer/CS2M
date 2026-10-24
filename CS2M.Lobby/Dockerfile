# CS2M lobby server. Build from this directory:  docker build -t cs2m-lobby .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CS2M.Lobby.csproj .
RUN dotnet restore CS2M.Lobby.csproj
COPY . .
RUN dotnet publish CS2M.Lobby.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENV LOBBY_UDP_PORT=4242 \
    LOBBY_HTTP_PORT=8080 \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimit=0x8000000
EXPOSE 4242/udp 8080/tcp
USER app
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["dotnet", "CS2M.Lobby.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "CS2M.Lobby.dll"]
