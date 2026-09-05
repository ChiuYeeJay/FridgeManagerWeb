FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FridgeManager.csproj .
RUN dotnet restore FridgeManager.csproj
COPY . .
RUN dotnet publish FridgeManager.csproj -c Release -o /app/publish \
    && test -f /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p wwwroot/uploads && chown app:app wwwroot/uploads
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FridgeManager.dll"]
