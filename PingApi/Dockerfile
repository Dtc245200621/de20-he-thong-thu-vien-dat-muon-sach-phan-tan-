FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Library.Contracts/Library.Contracts.csproj src/Library.Contracts/
COPY src/Library.Data/Library.Data.csproj src/Library.Data/
COPY src/Library.Messaging/Library.Messaging.csproj src/Library.Messaging/
COPY PingApi/PingApi.csproj PingApi/
RUN dotnet restore PingApi/PingApi.csproj
COPY src/ src/
COPY PingApi/ PingApi/
WORKDIR /src/PingApi
RUN dotnet publish PingApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PingApi.dll"]
