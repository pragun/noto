FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY noto.sln .
COPY src/Noto.Shared/Noto.Shared.csproj src/Noto.Shared/
COPY src/Noto.Server/Noto.Server.csproj src/Noto.Server/
RUN dotnet restore src/Noto.Server/Noto.Server.csproj

# Copy everything and build
COPY src/ src/
RUN dotnet publish src/Noto.Server/Noto.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

# Media directory (mounted as volume)
RUN mkdir -p /data/media/photos /data/media/audio

EXPOSE 8080
ENTRYPOINT ["dotnet", "Noto.Server.dll"]
