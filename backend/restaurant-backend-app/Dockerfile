FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY dnapp/lambdas/api-handler/APIHandler.csproj dnapp/lambdas/api-handler/
COPY server/RestaurantServer.csproj server/
RUN dotnet restore server/RestaurantServer.csproj
COPY dnapp/lambdas/api-handler/ dnapp/lambdas/api-handler/
COPY server/ server/
RUN dotnet publish server/RestaurantServer.csproj -c Release -o /app --no-restore

FROM amazon/dynamodb-local:latest AS dynamodb

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends default-jre-headless \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=dynamodb /home/dynamodblocal /opt/dynamodb
COPY --from=build /app .
COPY deployment_resources.json ./deployment_resources.json
ENV PORT=8080 \
    DYNAMODB_LOCAL_DIR=/opt/dynamodb \
    DOTNET_GCHeapHardLimitPercent=0x28
EXPOSE 8080
ENTRYPOINT ["dotnet", "RestaurantServer.dll"]
