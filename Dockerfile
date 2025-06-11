# ===== BUILD STAGE =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set working directory
WORKDIR /src

# Copy solution file
COPY AudioTranslationAPI.sln ./

# Copy project files
COPY src/AudioTranslationAPI.API/AudioTranslationAPI.API.csproj ./src/AudioTranslationAPI.API/
COPY src/AudioTranslationAPI.Application/AudioTranslationAPI.Application.csproj ./src/AudioTranslationAPI.Application/
COPY src/AudioTranslationAPI.Domain/AudioTranslationAPI.Domain.csproj ./src/AudioTranslationAPI.Domain/
COPY src/AudioTranslationAPI.Infrastructure/AudioTranslationAPI.Infrastructure.csproj ./src/AudioTranslationAPI.Infrastructure/

# Restore dependencies
RUN dotnet restore

# Copy all source code
COPY . .

# Build the application
WORKDIR /src/src/AudioTranslationAPI.API
RUN dotnet build -c Release -o /app/build

# Publish the application
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ===== RUNTIME STAGE =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install system dependencies
RUN apt-get update && apt-get install -y \
# FFmpeg and audio libraries
ffmpeg \
# Audio codec libraries
libavcodec-extra \
libavformat-dev \
libavutil-dev \
libswresample-dev \
# Additional audio libraries
libasound2-dev \
libsox-fmt-all \
# System utilities
curl \
wget \
# Clean up
&& rm -rf /var/lib/apt/lists/*

# Verify FFmpeg installation
RUN ffmpeg -version

# Create app user (security best practice)
RUN addgroup --system --gid 1001 appgroup
RUN adduser --system --uid 1001 --gid 1001 --shell /bin/false appuser

# Set working directory
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Create directories for application data
RUN mkdir -p /app/storage/originals \
&& mkdir -p /app/storage/converted \
&& mkdir -p /app/storage/transcripts \
&& mkdir -p /app/storage/translated_audio \
&& mkdir -p /app/storage/temp \
&& mkdir -p /app/logs

# Set proper permissions
RUN chown -R appuser:appgroup /app
RUN chmod -R 755 /app/storage
RUN chmod -R 755 /app/logs

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
CMD curl -f http://localhost:8080/health || exit 1

# Start the application
ENTRYPOINT ["dotnet", "AudioTranslationAPI.API.dll"]