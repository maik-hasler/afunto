FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# bash's /dev/tcp avoids adding curl/wget back just for this - see HEALTHCHECK below.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
	CMD bash -c '{ printf "GET /alive HTTP/1.1\r\nhost: localhost\r\nconnection: close\r\n\r\n" >&0; grep -q "HTTP/1.1 200"; } 0<>/dev/tcp/127.0.0.1/8080'

FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /repo
COPY . .
RUN dotnet restore "src/Api/Api.csproj" --locked-mode
WORKDIR "/repo/src/Api"
RUN dotnet build "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG APP_VERSION=1.0.0
RUN dotnet publish "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:Version=$APP_VERSION

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Api.dll"]
