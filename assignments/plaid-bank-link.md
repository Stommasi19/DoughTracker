# Complete a Plaid bank link

## Your assignment

Create `DoughTracker/bank_link.py` and implement `complete_bank_link` as a
module-level function. This is the backend step that saves a bank connection
after the user finishes Plaid Link.

```python
def complete_bank_link(
    *, db: Session, user_id: UUID, public_token: str
) -> UUID:
```

- `db` is the database session for the current request.
- `user_id` comes from the signed-in DoughTracker session.
- `public_token` is the temporary token the browser received from Plaid Link.
- The return value is the **local connection ID** generated when the connection
  is saved.

## Read before coding

1. [`exchange_token` in plaid_routes.py](../DoughTracker/plaid_routes.py)
   calls your method and turns failures into HTTP responses.
2. [`exchange_public_token` in plaid_client.py](../DoughTracker/Infrastructure/plaid_client.py)
   returns the Plaid access token first and the Plaid Item ID second.
3. [`save_connection` in connections.py](../DoughTracker/connections.py)
   takes the local user ID, Plaid Item ID, and access token. It commits the
   connection and returns its local ID.

## What done looks like

- A successful exchange and save produce one connection owned by the supplied
  user. The method returns the connection's local ID and keeps the access token
  on the backend.
- An exchange failure saves no connection. Let Plaid and database exceptions
  reach the route, which handles the HTTP response and any database rollback.
  The method does not retry the exchange.

Your contribution is the method. The assistant owns the route wiring, reviews
your implementation, and writes and runs the tests, including the full Sandbox
flow and persistence after restart.
