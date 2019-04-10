FROM microsoft/dotnet:2.1-runtime AS base
WORKDIR /app

FROM microsoft/dotnet:2.1-sdk AS publish
WORKDIR /src
COPY . .
RUN dotnet restore 
RUN dotnet publish -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish ./app .

ENTRYPOINT ["dotnet", "sensor-data-producer.dll"]
