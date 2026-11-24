import os
from datetime import datetime

import plaid
from plaid.api import plaid_api
from plaid.model.account_base import AccountBase
from plaid.model.accounts_get_request import AccountsGetRequest
from plaid.model.country_code import CountryCode
from plaid.model.item import Item
from plaid.model.item_get_request import ItemGetRequest
from plaid.model.item_public_token_exchange_request import ItemPublicTokenExchangeRequest
from plaid.model.link_token_create_request import LinkTokenCreateRequest
from plaid.model.link_token_create_request_user import LinkTokenCreateRequestUser
from plaid.model.products import Products
from plaid.model.transactions_get_request import TransactionsGetRequest
from plaid.model.transactions_get_request_options import TransactionsGetRequestOptions
# Available environments are
# 'Production'
# 'Sandbox'
configuration = plaid.Configuration(
    host=plaid.Environment.Sandbox,
    api_key={
        'clientId': os.environ.get("PLAID_ID"),
        'secret': os.environ.get("PLAID_SANDBOX_SECRET"),
    }
)

api_client = plaid.ApiClient(configuration)
client = plaid_api.PlaidApi(api_client)
REQUEST_TIMEOUT = 10



def create_link_token(user_id: str) -> str:
    """Create a Sandbox Link token scoped to a local DoughTracker user."""
    request = LinkTokenCreateRequest(
        user=LinkTokenCreateRequestUser(client_user_id=user_id),
        client_name="DoughTracker",
        products=[Products("transactions")],
        country_codes=[CountryCode("US")],
        language="en",
    )
    return client.link_token_create(request, _request_timeout=REQUEST_TIMEOUT)["link_token"]


def exchange_public_token(public_token: str) -> tuple[str, str]:
    """Exchanges a public token for an access token."""
    exchange_request = ItemPublicTokenExchangeRequest(
        public_token=public_token
    )
    exchange_response = client.item_public_token_exchange(
        exchange_request, _request_timeout=REQUEST_TIMEOUT
    )
    return exchange_response['access_token'], exchange_response['item_id']


def get_item(access_token: str) -> Item:
    request = ItemGetRequest(access_token=access_token)
    return client.item_get(request, _request_timeout=REQUEST_TIMEOUT).item


def get_accounts(access_token: str) -> list[AccountBase]:
    request = AccountsGetRequest(access_token=access_token)
    return client.accounts_get(request, _request_timeout=REQUEST_TIMEOUT).accounts


def retrieve_transactions(access_token: str) -> list[dict]:
    """Retrieves transactions for a given access token and date range."""
    request = TransactionsGetRequest(
    access_token=access_token,
    start_date=datetime.strptime('2020-01-01', '%Y-%m-%d').date(),
    end_date=datetime.strptime('2021-01-01', '%Y-%m-%d').date(),
)
    response = client.transactions_get(request)
    transactions = response['transactions']

    # the transactions in the response are paginated, so make multiple calls while incrementing the cursor to
    # retrieve all transactions
    while len(transactions) < response['total_transactions']:
        options = TransactionsGetRequestOptions()
        options.offset = len(transactions)

        request = TransactionsGetRequest(
            access_token=access_token,
            start_date=datetime.strptime('2020-01-01', '%Y-%m-%d').date(),
            end_date=datetime.strptime('2021-01-01', '%Y-%m-%d').date(),
            options=options
        )
        response = client.transactions_get(request)
