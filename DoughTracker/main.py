from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from starlette.middleware.trustedhost import TrustedHostMiddleware

from .Infrastructure.postgres_client import ensure_postgres_running
from .auth import router as auth_router
from .database import Base
from .plaid_routes import router as plaid_router


@asynccontextmanager
async def lifespan(app: FastAPI):
    pg_uri = ensure_postgres_running(restart_if_running=False)
    sqlalchemy_uri = pg_uri.replace("postgresql://", "postgresql+psycopg://", 1)
    engine = create_engine(sqlalchemy_uri, pool_pre_ping=True, hide_parameters=True)
    try:
        Base.metadata.create_all(engine)
        app.state.session_factory = sessionmaker(bind=engine, expire_on_commit=False)
        yield
    finally:
        engine.dispose()

app = FastAPI(
    title="DoughTracker",
    description="A simple app to track your finances",
    version="0.1.0",
    lifespan=lifespan,
)

app.add_middleware(TrustedHostMiddleware, allowed_hosts=["127.0.0.1", "localhost"])
app.include_router(auth_router)
app.include_router(plaid_router)


@app.middleware("http")
async def require_same_origin_for_writes(request: Request, call_next):
    origin = request.headers.get("origin")
    if request.method not in {"GET", "HEAD", "OPTIONS"} and origin is not None:
        expected_origin = f"{request.url.scheme}://{request.headers.get('host')}"
        if origin != expected_origin:
            return JSONResponse(status_code=403, content={"detail": "Use the local DoughTracker UI."})
    response = await call_next(request)
    if request.url.path.startswith("/api/v1/"):
        response.headers["Cache-Control"] = "no-store"
    return response

@app.get("/")
async def root():
    return {"message": "Hello World"}

@app.get("/health")
async def health():
    return {"status": "healthy"}
