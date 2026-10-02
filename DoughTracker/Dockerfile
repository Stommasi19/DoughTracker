FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json ./
COPY DoughTracker/src/ ./src/
RUN dotnet publish src/API/API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir /app/secrets && chown $APP_UID /app/secrets
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "API.dll"]
