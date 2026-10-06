FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HelpDesk.Api/HelpDesk.Api.csproj HelpDesk.Api/
RUN dotnet restore HelpDesk.Api/HelpDesk.Api.csproj
COPY HelpDesk.Api/ HelpDesk.Api/
RUN dotnet publish HelpDesk.Api/HelpDesk.Api.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelpDesk.Api.dll"]

