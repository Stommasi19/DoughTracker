import json
import os
from typing import Annotated, Literal
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException
from plaid.exceptions import ApiException
from pydantic import BaseModel, Field
from sqlalchemy.exc import IntegrityError, SQLAlchemyError
from urllib3.exceptions import HTTPError

from .Infrastructure.plaid_client import create_link_token, get_accounts, get_item
from .auth import Database, LocalUser, get_current_user
from .bank_link import complete_bank_link
from .connections import BankConnection, list_connections

router = APIRouter(prefix="/api/v1/connections")
CurrentUser = Annotated[LocalUser, Depends(get_current_user)]
ConnectionStatus = Literal["connected", "reconnect_required", "unavailable"]


class LinkToken(BaseModel):
    linkToken: str


class PublicToken(BaseModel):
    publicToken: str = Field(min_length=1, max_length=2048)


class SavedConnection(BaseModel):
    id: UUID


class LinkedAccount(BaseModel):
    id: str
    name: str
    type: str
    mask: str | None


class ConnectionSummary(BaseModel):
    id: UUID
    institutionName: str | None = None
    status: ConnectionStatus = "unavailable"
    accounts: list[LinkedAccount] = Field(default_factory=list)


class Connections(BaseModel):
    items: list[ConnectionSummary]


def status_for_error(error_code: str | None) -> ConnectionStatus:
    return "reconnect_required" if error_code == "ITEM_LOGIN_REQUIRED" else "unavailable"


def summarize_connection(connection: BankConnection) -> ConnectionSummary:
    summary = ConnectionSummary(id=connection.id)
    try:
        # Plaid can keep returning cached accounts while an Item needs reauthorization.
        item = get_item(connection.access_token)
        summary.institutionName = getattr(item, "institution_name", None)
        if item_error := getattr(item, "error", None):
            summary.status = status_for_error(item_error.error_code)
            return summary

        summary.accounts = [
            LinkedAccount(
                id=account.account_id,
                name=account.name,
                type=str(getattr(account, "subtype", None) or account.type),
                mask=getattr(account, "mask", None),
            )
            for account in get_accounts(connection.access_token)
        ]
        summary.status = "connected"
    except ApiException as exc:
        try:
            error = json.loads(exc.body or "{}")
        except ValueError:
            error = {}
        if isinstance(error, dict):
            summary.status = status_for_error(error.get("error_code"))
    except HTTPError:
        # A transport error gives us no evidence that the bank requires reauthorization.
        pass
    return summary


@router.post("/link-token")
def link_token(user: CurrentUser) -> LinkToken:
    if not os.environ.get("PLAID_ID") or not os.environ.get("PLAID_SANDBOX_SECRET"):
        raise HTTPException(status_code=503, detail="Configure the Plaid Sandbox credentials.")
    try:
        return LinkToken(linkToken=create_link_token(str(user.id)))
    except (ApiException, HTTPError) as exc:
        raise HTTPException(status_code=502, detail="Plaid could not start bank linking.") from exc


@router.post("/exchange-token", status_code=201)
def exchange_token(payload: PublicToken, user: CurrentUser, db: Database) -> SavedConnection:
    try:
        connection_id = complete_bank_link(db=db, user_id=user.id, public_token=payload.publicToken)
    except (ApiException, HTTPError) as exc:
        raise HTTPException(status_code=502, detail="Plaid could not complete bank linking.") from exc
    except IntegrityError as exc:
        db.rollback()
        raise HTTPException(status_code=409, detail="This bank connection could not be saved.") from exc
    except SQLAlchemyError as exc:
        db.rollback()
        raise HTTPException(status_code=503, detail="Saving a new bank connection is unavailable.") from exc
    return SavedConnection(id=connection_id)


@router.get("")
def connections(user: CurrentUser, db: Database) -> Connections:
    return Connections(
        items=[
            summarize_connection(connection)
            for connection in list_connections(db, user_id=user.id)
        ]
    )
