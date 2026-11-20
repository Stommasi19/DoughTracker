from typing import Protocol
from sqlalchemy import Transaction
from DoughTracker.Domain.Interfaces.IAccount import IAccount

class DatabaseClient(Protocol):
    """Contract for any storage backend (Postgres, SQLite, etc.)."""
    def connect(self) -> None:
        ...

    def disconnect(self) -> None:
        ...

    def save_accounts(self, accounts: list[IAccount]) -> None:
        ...

    def get_all_accounts(self) -> list[IAccount]:
        ...
