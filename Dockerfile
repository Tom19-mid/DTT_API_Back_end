# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY DTT_Backend_API.csproj .
RUN dotnet restore DTT_Backend_API.csproj

COPY . .
RUN dotnet publish DTT_Backend_API.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render cấp PORT động lúc container khởi động (không cố định ở đây) —
# dùng shell-form CMD để $PORT được thay thế đúng lúc chạy, không phải lúc build.
ENV ASPNETCORE_ENVIRONMENT=Development
CMD ASPNETCORE_URLS=http://+:$PORT dotnet DTT_Backend_API.dll
