FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Library.Contracts/Library.Contracts.csproj src/Library.Contracts/
COPY src/Library.Data/Library.Data.csproj src/Library.Data/
COPY src/Library.Messaging/Library.Messaging.csproj src/Library.Messaging/
COPY Reservation.Worker/Reservation.Worker.csproj Reservation.Worker/
RUN dotnet restore Reservation.Worker/Reservation.Worker.csproj
COPY src/ src/
COPY Reservation.Worker/ Reservation.Worker/
WORKDIR /src/Reservation.Worker
RUN dotnet publish Reservation.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Reservation.Worker.dll"]
