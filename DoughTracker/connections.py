from uuid import UUID, uuid4

from sqlalchemy import ForeignKey, Text, select
from sqlalchemy.orm import Mapped, Session, mapped_column

from .auth import LocalUser
from .database import Base


class BankConnection(Base):
    __tablename__ = "bank_connections"

    id: Mapped[UUID] = mapped_column(primary_key=True, default=uuid4)
    user_id: Mapped[UUID] = mapped_column(
        ForeignKey(LocalUser.__table__.c.id, ondelete="CASCADE"), index=True
    )
    item_id: Mapped[str] = mapped_column(Text, unique=True)
    access_token: Mapped[str] = mapped_column(Text)


def save_connection(
    db: Session, *, user_id: UUID, item_id: str, access_token: str
) -> UUID:
    """Persist a linked Plaid Item and return its local connection ID.

    The Item ID is unique so the same Plaid Item cannot be assigned to
    multiple users. Database failures propagate to the caller.
    """
    connection = BankConnection(user_id=user_id, item_id=item_id, access_token=access_token)
    db.add(connection)
    db.commit()
    return connection.id


def list_connections(db: Session, *, user_id: UUID) -> list[BankConnection]:
    return list(db.scalars(select(BankConnection).where(BankConnection.user_id == user_id)))
