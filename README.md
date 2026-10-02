# DoughTracker
An evolving expense tracking application that will be expanded into a budgeting app -> an all in one finance application

## Project documentation

- [Architecture handoff](docs/ARCHITECTURE.md)
- [Sequential backend tasks](docs/BACKEND-TASKS.md)

## Start the backend locally

Start Docker Desktop, then run from the repository root:

```sh
docker compose up --build -d
curl --fail http://localhost:5083/health
```

The API runs at `http://localhost:5083`. Its development OpenAPI document is at
`http://localhost:5083/openapi/v1.json`. PostgreSQL stays on the Compose network
and stores its data in a named volume. `docker compose down` stops the services
and retains that data.

Compose waits for PostgreSQL's health check before starting the API. See
[Docker's startup-order documentation](https://docs.docker.com/compose/how-tos/startup-order/).

The default database password is for this local setup. Set `POSTGRES_PASSWORD`
in a root `.env` file before the first start if you want a different password.
Changing it later also requires updating the existing database user's password.

This first step runs the API and database; database access and migrations are
not implemented yet. The frontend continues to run separately while it is being
built. RabbitMQ/MassTransit will be added with the synchronization worker.

Follow the [backend task checklist](docs/BACKEND-TASKS.md) for implementation order,
completion checks, and decisions needed along the way.
