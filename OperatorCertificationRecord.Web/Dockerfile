# Multi-stage Dockerfile for OperatorCertificationRecord.Web (ASP.NET Core 8)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies (context is OperatorCertificationRecord.Web/)
COPY OperatorCertificationRecord.Web.csproj ./
RUN dotnet restore OperatorCertificationRecord.Web.csproj

# Copy everything and publish
COPY . .

# Copy appsettings files to a temporary location in build stage
COPY appsettings.json /app_config/
COPY appsettings.Production.json /app_config/

RUN dotnet publish OperatorCertificationRecord.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
COPY --from=build /app/publish ./
# Copy appsettings from the build stage  
COPY --from=build /app_config/appsettings*.json ./
# Create directory for persistent data protection keys
RUN mkdir -p /app/dataprotection
EXPOSE 80
ENTRYPOINT ["dotnet", "OperatorCertificationRecord.Web.dll"]

