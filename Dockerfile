FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/VsngrpCoreBeWs.csproj ./
RUN dotnet restore

COPY src/ ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ARG GIT_SHA=dev
ENV GIT_SHA=${GIT_SHA}
ENV CONFIG_PATH=/app/config/config.json

COPY --from=build /app ./

EXPOSE 9002

ENTRYPOINT ["dotnet", "VsngrpCoreBeWs.dll"]
