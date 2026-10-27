# Uma imagem: API + painel React (servido pela própria API) + jobs Hangfire.

FROM node:22-alpine AS painel
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# vite.config.ts publica em ../src/Indexa.Api/wwwroot
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Indexa.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api /app .
COPY --from=painel /src/Indexa.Api/wwwroot ./wwwroot
RUN mkdir -p /app/armazenamento && chown "$APP_UID" /app/armazenamento
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Indexa.Api.dll"]
