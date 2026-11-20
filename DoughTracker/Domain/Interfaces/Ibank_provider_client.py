from typing import Protocol
from sqlalchemy import Transaction
from DoughTracker.Domain.Interfaces.IAccount import IAccount


class BankProvider(Protocol):
    """Contract for any financial aggregator (Teller, Plaid, etc.)."""
    async def get_accounts(self, access_token: str) -> list[IAccount]:
        ...

    async def get_transactions(self, access_token: str, account_id: str) : ## -> list[]:
        ...

