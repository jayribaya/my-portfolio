FROM node:24-bookworm-slim AS frontend
WORKDIR /ui
COPY src/Portfolio.Web/package*.json ./
RUN npm ci --no-audit --no-fund
COPY src/Portfolio.Web/tsconfig.json ./
COPY src/Portfolio.Web/Build ./Build
COPY src/Portfolio.Web/Scripts ./Scripts
COPY src/Portfolio.Web/Styles ./Styles
COPY src/Portfolio.Web/Views ./Views
RUN mkdir -p wwwroot/js/vendor && cp node_modules/sortablejs/modular/sortable.complete.esm.js wwwroot/js/vendor/sortable.esm.js && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json Portfolio.slnx ./
COPY src ./src
RUN dotnet restore src/Portfolio.Web/Portfolio.Web.csproj
COPY --from=frontend /ui/wwwroot ./src/Portfolio.Web/wwwroot
RUN dotnet publish src/Portfolio.Web/Portfolio.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER root
RUN mkdir -p /app/App_Data/keys && chown -R $APP_UID /app/App_Data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Portfolio.Web.dll"]

