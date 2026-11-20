from DoughTracker.Domain.AccountType import AccountType
from abc import ABC, abstractmethod


class IAccount:
    @abstractmethod
    def __init__(self, account_id:str, account_name:str, account_type:AccountType, account_balance:float):
        pass
        
    @abstractmethod
    def get_account_id(self) -> str:
        pass
    
    @abstractmethod
    def get_account_name(self) -> str:
        pass

    @abstractmethod
    def get_account_type(self) -> AccountType:
        pass    
    
    @abstractmethod
    def get_account_balance(self) -> float:
        pass

    @abstractmethod
    def update_account_balance(self, new_balance:float):
        pass

    @abstractmethod
    def __str__(self):
        pass