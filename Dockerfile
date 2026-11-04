FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PsySite.csproj .
RUN dotnet restore PsySite.csproj

COPY . .
RUN dotnet publish PsySite.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /app/publish .
RUN mkdir -p /app/App_Data /app/wwwroot/uploads && chown -R app:app /app

USER app
VOLUME ["/app/App_Data", "/app/wwwroot/uploads"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "PsySite.dll"]
