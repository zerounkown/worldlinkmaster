# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore as a distinct layer so dependency restore is cached across source-only changes.
COPY WorldLinkMaster.Web.csproj .
RUN dotnet restore "WorldLinkMaster.Web.csproj"

# Copy the rest and publish.
COPY . .
RUN dotnet publish "WorldLinkMaster.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Production by default; TLS is terminated at the reverse proxy, app listens on plain HTTP :8080.
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

# Run as the non-root user baked into the .NET runtime image.
USER $APP_UID

ENTRYPOINT ["dotnet", "WorldLinkMaster.Web.dll"]
