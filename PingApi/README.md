# Library API and Web UI

`PingApi` serves the library interface from `wwwroot` and exposes the Minimal API routes documented in the repository root `README.md`. The API requires PostgreSQL. Reservation events are published by the outbox service to RabbitMQ for `Reservation.Worker` to process.

Run the full dependency stack with `docker compose up --build` from the repository root, or start PostgreSQL and RabbitMQ first and then run:

```powershell
dotnet run --project PingApi.csproj
```

Open <http://localhost:5173>. See the root README for local connection settings and the worker startup steps.
