

from uuid import UUID

from sqlalchemy.orm import Session

from DoughTracker.connections import save_connection
from DoughTracker.Infrastructure.plaid_client import exchange_public_token


def complete_bank_link(*, db: Session, user_id: UUID, public_token: str) -> UUID:
    """Exchanges a Plaid public token for an access token and saves the connection.

    Returns the local connection ID.
    """
    access_token, item_id = exchange_public_token(public_token)
    return save_connection(db=db, user_id=user_id, item_id=item_id, access_token=access_token)