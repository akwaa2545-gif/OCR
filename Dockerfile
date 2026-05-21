# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore layer cached until csproj changes
COPY OperatorCertificationRecord/OperatorCertificationRecord.csproj OperatorCertificationRecord/
COPY OperatorCertificationRecord.Web/OperatorCertificationRecord.Web.csproj OperatorCertificationRecord.Web/
RUN dotnet restore OperatorCertificationRecord.Web/OperatorCertificationRecord.Web.csproj

# Copy source and publish
COPY OperatorCertificationRecord/ OperatorCertificationRecord/
COPY OperatorCertificationRecord.Web/ OperatorCertificationRecord.Web/
RUN dotnet publish OperatorCertificationRecord.Web/OperatorCertificationRecord.Web.csproj \
    -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Chromium for PuppeteerSharp PDF generation
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        chromium \
        fonts-liberation \
        libglib2.0-0 \
        libnss3 \
        libatk-bridge2.0-0 \
        libx11-xcb1 \
        libxcomposite1 \
        libxdamage1 \
        libxrandr2 \
        libgbm1 \
        libasound2 && \
    rm -rf /var/lib/apt/lists/*

# Tell PuppeteerSharp where system Chromium lives (no download needed)
ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium
ENV PUPPETEER_SKIP_CHROMIUM_DOWNLOAD=true

COPY --from=build /app/publish .

# Pre-create volume mount points so permissions are correct
RUN mkdir -p /app/dataprotection \
             /app/wwwroot/certs \
             /app/wwwroot/uploads \
             /app/wwwroot/photos \
             logs

EXPOSE 80

ENTRYPOINT ["dotnet", "OperatorCertificationRecord.Web.dll"]
