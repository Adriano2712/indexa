# Uma imagem: API + painel React (servido pela própria API) + jobs Hangfire.
# Multi-arquitetura (amd64/arm64): as etapas de build rodam na arquitetura da máquina e
# compilam para a de destino, sem emulação (VMs ARM da Oracle Cloud, por exemplo).

FROM --platform=$BUILDPLATFORM node:22-alpine AS painel
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# vite.config.ts publica em ../src/Indexa.Api/wwwroot
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS api
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Indexa.Api -c Release -a "$TARGETARCH" --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app .
COPY --from=painel /src/Indexa.Api/wwwroot ./wwwroot
RUN mkdir -p /app/armazenamento && chown "$APP_UID" /app/armazenamento
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Indexa.Api.dll"]
