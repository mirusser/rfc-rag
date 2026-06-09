# src/RfcRag/Dockerfile
# Multi-stage build: filter (extract csproj) → build (restore + publish) → runtime (alpine)
#
# Build:
#   docker build -t rfc-rag -f src/RfcRag/Dockerfile .
#
# Run (stdio MCP server — keeps running, listening on stdin):
#   docker run --rm -i --network host \
#     -v ~/rfc-mirror:/rfc-mirror:ro \
#     -e RfcRag__PostgresConnectionString="Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres" \
#     -e RfcRag__RfcMirrorPath=/rfc-mirror \
#     -e OpenRouter__ApiKey="sk-or-..." \
#     rfc-rag
#
# Re-index a specific RFC (runs then exits):
#   docker run --rm -i \
#     ...same env vars... \
#     rfc-rag --reindex=2119
#
# Force re-index all RFCs (runs then exits):
#   docker run --rm -i \
#     ...same env vars... \
#     rfc-rag --force

FROM alpine:3.21 AS filter
WORKDIR /src
COPY src/ src/
RUN mkdir /out && \
    find src -name '*.csproj' | tar -cf - -T - | tar -xf - -C /out

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .
COPY .editorconfig .
COPY --from=filter /out/src/ ./src/
RUN dotnet restore src/RfcRag/RfcRag.csproj

COPY src/ src/
RUN dotnet publish src/RfcRag/RfcRag.csproj \
    --configuration Release \
    --output /app/rfc-rag

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/rfc-rag
COPY --from=build /app/rfc-rag .

# krb5-libs: suppresses Npgsql "Cannot load library libgssapi_krb5.so.2" warning
RUN apk add --no-cache krb5-libs

ENV \
    RfcRag__RunMigrationsOnStartup=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

USER $APP_UID
ENTRYPOINT ["dotnet", "RfcRag.dll"]
