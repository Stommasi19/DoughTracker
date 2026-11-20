import time
import docker
from docker.errors import NotFound
import psycopg
import os
from dotenv import load_dotenv

load_dotenv()

def ensure_postgres_running(restart_if_running: bool = True) -> str:
    """Starts, restarts, or creates a persistent Postgres container and waits until ready."""
    client = docker.from_env()

    try:
        container = client.containers.get(os.environ["CONTAINER_NAME"])
        container.reload()

        if container.status == "running":
            if restart_if_running:
                print(f"Restarting existing container '{os.environ['CONTAINER_NAME']}'...")
                container.restart()
            else:
                print(f"Container '{os.environ['CONTAINER_NAME']}' is already running.")
        else:
            print(f"Starting stopped container '{os.environ['CONTAINER_NAME']}'...")
            container.start()

    except NotFound:
        print(f"Creating new persistent container '{os.environ['CONTAINER_NAME']}'...")
        client.containers.run(
            image="postgres:16-alpine",
            name=os.environ["CONTAINER_NAME"],
            environment={
                "POSTGRES_USER": os.environ["DB_USER"],
                "POSTGRES_PASSWORD": os.environ["DB_PASSWORD"],
                "POSTGRES_DB": os.environ["DB_NAME"],
            },
            ports={f"{os.environ['DB_PORT']}/tcp": os.environ["DB_PORT"]},
            volumes={os.environ["VOLUME_NAME"]: {"bind": "/var/lib/postgresql/data", "mode": "rw"}},
            restart_policy={"Name": "unless-stopped"},
            detach=True,
        )

    conn_str = (
        f"postgresql://{os.environ['DB_USER']}:{os.environ['DB_PASSWORD']}"
        f"@{os.environ['DB_HOST']}:{os.environ['DB_PORT']}/{os.environ['DB_NAME']}"
    )
    _wait_for_db(conn_str)
    return conn_str


def _wait_for_db(conn_str: str, timeout: int = 30) -> None:
    """Polls PostgreSQL until it accepts connections."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with psycopg.connect(conn_str, connect_timeout=2) as conn:
                with conn.cursor() as cur:
                    cur.execute("SELECT 1;")
                print("PostgreSQL is ready.")
                return
        except psycopg.OperationalError:
            time.sleep(0.5)

    raise TimeoutError(f"PostgreSQL did not become ready within {timeout} seconds.")