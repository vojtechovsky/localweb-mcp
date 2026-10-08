# syntax=docker/dockerfile:1

# Build the MCP server.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LocalWeb.Mcp/LocalWeb.Mcp.csproj LocalWeb.Mcp/
RUN dotnet restore LocalWeb.Mcp/LocalWeb.Mcp.csproj
COPY LocalWeb.Mcp/ LocalWeb.Mcp/
RUN dotnet publish LocalWeb.Mcp/LocalWeb.Mcp.csproj -c Release -o /app --no-restore

# Runtime image.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app /app

# Install Playwright's OS dependencies and Chromium. The maintenance flags run
# the bundled Playwright CLI, so PowerShell is not required on Linux.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
USER root
RUN dotnet LocalWeb.Mcp.dll --install-browser-deps \
 && dotnet LocalWeb.Mcp.dll --install-browser

# Run as a non-privileged user.
RUN useradd --create-home appuser \
 && chown -R appuser /app /ms-playwright
USER appuser

# Keep the writable cache outside the image app directory.
ENV LocalWeb__CachePath=/home/appuser/cache.db

ENTRYPOINT ["dotnet", "LocalWeb.Mcp.dll"]
