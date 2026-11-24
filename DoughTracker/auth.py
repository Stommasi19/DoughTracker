import hashlib
import secrets
from datetime import datetime, timedelta, timezone
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Cookie, Depends, HTTPException, Response
from pydantic import BaseModel, Field
from sqlalchemy import DateTime, ForeignKey, String, delete, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Mapped, Session, mapped_column

from .database import Base, get_db

router = APIRouter(prefix="/api/v1")
SESSION_COOKIE = "doughtracker_session"
SESSION_LIFETIME = timedelta(days=7)
Database = Annotated[Session, Depends(get_db)]
SessionCookie = Annotated[str | None, Cookie(alias=SESSION_COOKIE, max_length=43)]


class LocalUser(Base):
    __tablename__ = "local_users"

    id: Mapped[UUID] = mapped_column(primary_key=True, default=uuid4)
    username: Mapped[str] = mapped_column(String(40), unique=True)
    password_hash: Mapped[str] = mapped_column(String(97))


class LocalSession(Base):
    __tablename__ = "local_sessions"

    token_hash: Mapped[str] = mapped_column(String(64), primary_key=True)
    user_id: Mapped[UUID] = mapped_column(ForeignKey("local_users.id", ondelete="CASCADE"))
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class Credentials(BaseModel):
    username: str = Field(min_length=3, max_length=40, pattern=r"^[A-Za-z0-9_.-]+$")
    password: str = Field(min_length=1, max_length=256)


class UserProfile(BaseModel):
    id: UUID
    username: str


def hash_password(password: str, salt: bytes) -> str:
    digest = hashlib.scrypt(
        password.encode(), salt=salt, n=2**17, r=8, p=1, dklen=32, maxmem=256 * 1024**2,
    )
    return f"{salt.hex()}:{digest.hex()}"


def hash_token(token: str) -> str:
    return hashlib.sha256(token.encode()).hexdigest()


def start_session(db: Session, user: LocalUser, response: Response, previous_token: str | None) -> UserProfile:
    now = datetime.now(timezone.utc)
    token = secrets.token_urlsafe(32)
    db.execute(delete(LocalSession).where(LocalSession.expires_at <= now))
    if previous_token:
        db.execute(delete(LocalSession).where(LocalSession.token_hash == hash_token(previous_token)))
    db.add(LocalSession(token_hash=hash_token(token), user_id=user.id, expires_at=now + SESSION_LIFETIME))
    db.commit()
    # The app is served over loopback HTTP. The cookie stays inaccessible to browser JS.
    response.set_cookie(SESSION_COOKIE, token, httponly=True, samesite="strict")
    return UserProfile(id=user.id, username=user.username)


def get_current_user(db: Database, token: SessionCookie = None) -> LocalUser:
    if token:
        user = db.scalar(
            select(LocalUser)
            .join(LocalSession, LocalSession.user_id == LocalUser.id)
            .where(
                LocalSession.token_hash == hash_token(token),
                LocalSession.expires_at > datetime.now(timezone.utc),
            )
        )
        if user is not None:
            return user
    raise HTTPException(status_code=401, detail="Sign in to continue.")


@router.post("/auth/register", status_code=201)
def register(credentials: Credentials, db: Database, response: Response, token: SessionCookie = None) -> UserProfile:
    if len(credentials.password) < 12:
        raise HTTPException(status_code=400, detail="Use at least 12 characters for your password.")
    user = LocalUser(
        username=credentials.username.lower(),
        password_hash=hash_password(credentials.password, secrets.token_bytes(16)),
    )
    db.add(user)
    try:
        db.flush()
    except IntegrityError as exc:
        db.rollback()
        raise HTTPException(status_code=409, detail="That username is already in use.") from exc
    return start_session(db, user, response, token)


@router.post("/auth/login")
def login(credentials: Credentials, db: Database, response: Response, token: SessionCookie = None) -> UserProfile:
    user = db.scalar(select(LocalUser).where(LocalUser.username == credentials.username.lower()))
    # Run the same password derivation for an unknown username.
    salt = bytes.fromhex(user.password_hash.split(":", 1)[0]) if user else bytes(16)
    candidate = hash_password(credentials.password, salt)
    if user is None or not secrets.compare_digest(user.password_hash, candidate):
        raise HTTPException(status_code=401, detail="Incorrect username or password.")
    return start_session(db, user, response, token)


@router.post("/auth/logout", status_code=204)
def logout(db: Database, response: Response, token: SessionCookie = None) -> None:
    if token:
        db.execute(delete(LocalSession).where(LocalSession.token_hash == hash_token(token)))
        db.commit()
    response.delete_cookie(SESSION_COOKIE, httponly=True, samesite="strict")


@router.get("/me")
def me(user: Annotated[LocalUser, Depends(get_current_user)]) -> UserProfile:
    return UserProfile(id=user.id, username=user.username)
