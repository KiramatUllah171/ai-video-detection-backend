FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY AiVideoDetection.Domain/AiVideoDetection.Domain.csproj AiVideoDetection.Domain/
COPY AiVideoDetection.Application/AiVideoDetection.Application.csproj AiVideoDetection.Application/
COPY AiVideoDetection.Infrastructure/AiVideoDetection.Infrastructure.csproj AiVideoDetection.Infrastructure/
COPY AiVideoDetection.Api/AiVideoDetection.Api.csproj AiVideoDetection.Api/

RUN dotnet restore AiVideoDetection.Api/AiVideoDetection.Api.csproj

COPY . .
RUN dotnet publish AiVideoDetection.Api/AiVideoDetection.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "AiVideoDetection.Api.dll"]
