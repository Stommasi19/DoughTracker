from fastapi.concurrency import asynccontextmanager
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker
from .Infrastructure.postgres_client import ensure_postgres_running
from fastapi import FastAPI, Request


@asynccontextmanager
async def lifespan(app: FastAPI):
    # 1. Start the container and build the engine locally inside lifespan
    pg_uri = ensure_postgres_running(restart_if_running=False)
    sqlalchemy_uri = pg_uri.replace("postgresql://", "postgresql+psycopg://")

    engine = create_engine(sqlalchemy_uri, pool_pre_ping=True)
    session_factory = sessionmaker(bind=engine, autoflush=False, expire_on_commit=False)

    # 2. Store both on app.state instead of module globals
    app.state.db_engine = engine
    app.state.session_factory = session_factory
    print(f"DoughTracker connected to: {sqlalchemy_uri}")

    yield

    # 3. Clean up the pool on shutdown
    engine.dispose()
    print("Database connection pool disposed.")

app = FastAPI(
    title="DoughTracker",
    description="A simple app to track your finances",
    version="0.1.0",
    lifespan=lifespan,
)

def get_db(request: Request):
    session_factory = request.app.state.session_factory
    with session_factory() as session:
        yield session

@app.get("/")
async def root():
    return {"message": "Hello World"}

@app.get("/health")
async def health():
    return {"status": "healthy"}