ARG NODE_IMAGE=node:24.18.0-bookworm-slim@sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302-noble@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.11-noble@sha256:207cc51496778557731c81ff670333d8ade4a4fec22768fd1be8e78474a84ecf

FROM ${NODE_IMAGE} AS client-build
WORKDIR /src/ClientApp

COPY src/Tezuri.App/ClientApp/package.json \
     src/Tezuri.App/ClientApp/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/Tezuri.App/ClientApp/ ./
RUN npm run check && npm run build

FROM ${NODE_IMAGE} AS node-runtime
WORKDIR /runtime
COPY eng/runtime-npm/package.json eng/runtime-npm/package-lock.json \
     eng/runtime-npm/verify.mjs ./
RUN npm ci --omit=dev --ignore-scripts --no-audit --no-fund \
    && rm -rf node_modules/npm/node_modules/brace-expansion \
              node_modules/npm/node_modules/ip-address \
    && node verify.mjs \
    && npm cache clean --force

FROM ${DOTNET_SDK_IMAGE} AS dotnet-build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Tezuri.App/Tezuri.App.csproj \
     src/Tezuri.App/packages.lock.json \
     src/Tezuri.App/koan.lock.json ./src/Tezuri.App/
COPY src/Tezuri.Domain/Tezuri.Domain.csproj \
     src/Tezuri.Domain/packages.lock.json ./src/Tezuri.Domain/
COPY src/Tezuri.Infrastructure/Tezuri.Infrastructure.csproj \
     src/Tezuri.Infrastructure/packages.lock.json ./src/Tezuri.Infrastructure/

RUN dotnet restore src/Tezuri.App/Tezuri.App.csproj --locked-mode

COPY src/ ./src/
RUN dotnet publish src/Tezuri.App/Tezuri.App.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

COPY --from=client-build /src/wwwroot/ /out/wwwroot/

FROM ${ASPNET_IMAGE} AS final
ARG VERSION=0.0.0-dev
ARG REVISION=unknown

LABEL org.opencontainers.image.title="Tezuri" \
      org.opencontainers.image.description="Local-first authoring workspace for Bundling Ways" \
      org.opencontainers.image.source="https://github.com/sylin-org/tezuri" \
      org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        git \
        libatomic1 \
        libstdc++6 \
        openssh-client \
    && rm -rf /var/lib/apt/lists/*

# The final image keeps Node/npm because Tezuri's isolated Eleventy proof must use
# the same runtime as a user's repository without installing tools at startup.
COPY --from=node-runtime /usr/local/bin/node /usr/local/bin/node
COPY --from=node-runtime /runtime/node_modules/ /usr/local/lib/node_modules/
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx

WORKDIR /app
RUN mkdir -p /workspace /home/app /tmp/tezuri /app/data \
    && chown -R app:app /workspace /home/app /tmp/tezuri /app/data

COPY --from=dotnet-build --chown=app:app /out/ ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    NPM_CONFIG_CACHE=/tmp/tezuri/npm \
    TEZURI_WORKSPACE=/workspace \
    XDG_CACHE_HOME=/tmp/tezuri/cache

EXPOSE 8080
USER app

HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:8080/health/ready').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "Tezuri.App.dll"]
