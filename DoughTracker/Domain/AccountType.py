from enum import Enum

class AccountType(Enum):
    CHECKING = "Checking"
    SAVINGS = "Savings"
    HYSA = "High Yield Savings"
    CREDIT_CARD = "Credit Card"
    DEBIT_CARD = "Debit Card"
    INVESTMENT = "Investment"
