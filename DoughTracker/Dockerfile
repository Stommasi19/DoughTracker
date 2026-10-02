FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source
COPY src/ ./src/
RUN dotnet publish src/API/API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "API.dll"]
