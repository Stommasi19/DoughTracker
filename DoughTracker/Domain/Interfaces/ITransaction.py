from DoughTracker.Domain.AccountType import AccountType
from abc import ABC, abstractmethod

class ITransaction(ABC):
    @abstractmethod
    def __init__(self, transaction_id:str, transaction_date:str, transaction_title:str ,  transaction_amount:float, transaction_description:str, account_id:str):
        pass
    
    @abstractmethod
    def get_transaction_id(self) -> str:
        pass

    @abstractmethod
    def get_transaction_title(self) -> str:
        pass
    
    @abstractmethod
    def get_transaction_date(self) -> str:
        pass
    
    @abstractmethod
    def get_transaction_amount(self) -> float:
        pass
    
    @abstractmethod
    def get_transaction_description(self) -> str:
        pass
    
    @abstractmethod
    def get_account_id(self) -> str:
        pass
    
    @abstractmethod
    def __str__(self):
        pass