import os
import plaid
from plaid.api import plaid_api
from plaid.model.item_public_token_exchange_request import ItemPublicTokenExchangeRequest, datetime
from plaid.model.transactions_get_request_options import TransactionsGetRequestOptions, TransactionsGetRequest
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



def exchange_public_token(public_token: str) -> tuple[str, str]:
    """Exchanges a public token for an access token."""
    exchange_request = ItemPublicTokenExchangeRequest(
        public_token=public_token
    )
    exchange_response = client.item_public_token_exchange(exchange_request)
    return exchange_response['access_token'], exchange_response['item_id']


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