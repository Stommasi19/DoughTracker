import { apiToken } from "./firebase";

export type Category = { id: string; name: string };
export type Institution = { id: string; name: string; initials: string };
export type Connection = {
  id: string;
  institutionId: string;
  status: string;
  lastSyncAt: string | null;
};
export type Account = {
  id: string;
  connectionId: string;
  institutionName: string;
  provider: string;
  connectionStatus: string;
  lastSyncAt: string | null;
  institutionId: string;
  name: string;
  mask: string | null;
  type: string;
  subtype: string;
  currentBalance: number | null;
  availableBalance: number | null;
  currency: string;
};
export type Transaction = {
  id: string;
  accountId: string;
  amount: number;
  currency: string;
  date: string;
  authorizedDate: string | null;
  description: string;
  merchantName: string | null;
  merchant: string;
  categoryId: string;
  providerCategoryId: string;
  providerCategoryKey: string | null;
  categoryConfidence: string | null;
  classification: string;
  pending: boolean;
  pendingTransactionId: string | null;
  paymentChannel: string;
  manualCategoryId: string | null;
};
export type SpendingGroup = {
  id: string;
  name: string;
  amount: number;
  count: number;
};
export type Snapshot = {
  currency: string;
  currencies: string[];
  totalCount: number;
  seeded: boolean;
  asOf: string;
  month: string;
  months: string[];
  categories: Category[];
  institutions: Institution[];
  connections: Connection[];
  accounts: Account[];
  transactions: Transaction[];
  report: {
    spending: number;
    previousSpending: number;
    changePercent: number | null;
    pendingSpending: number;
    refunds: number;
    dailyAverage: number;
    expenseCount: number;
    categories: SpendingGroup[];
    merchants: SpendingGroup[];
    daily: { day: number; current: number; previous: number }[];
    trend: { month: string; amount: number }[];
  };
};

export async function request<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set("Content-Type", "application/json");
  headers.set(
    "Authorization",
    `Bearer ${await apiToken(options.signal ?? undefined)}`,
  );
  const response = await fetch(`/api/v1${path}`, {
    ...options,
    headers,
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(
      detail?.detail ?? detail?.title ??
        "The API could not complete this request. Please try again.",
    );
  }
  const body = await response.text();
  return body ? (JSON.parse(body) as T) : (undefined as T);
}

export type TransactionPage = { items: Transaction[]; page: number; pageSize: number; totalCount: number };
type Summary = Pick<Snapshot["report"], "spending" | "previousSpending" | "changePercent" | "pendingSpending" | "refunds" | "dailyAverage" | "expenseCount">;
type Spending = Pick<Snapshot["report"], "categories" | "merchants" | "daily" | "trend">;

export async function loadWorkspace(params: URLSearchParams, options: RequestInit): Promise<Snapshot> {
  const [{ items: accounts }, { items: categories }] = await Promise.all([
    request<{ items: Account[] }>("/accounts", options),
    request<{ items: Category[] }>("/categories", options),
  ]);
  const seeded = accounts.some(a => a.provider === "development");
  const month = params.get("month") || (seeded ? "2026-09" : new Date().toISOString().slice(0, 7));
  const currency = params.get("currency") || "USD";
  const filters = new URLSearchParams(params);
  filters.set("month", month);
  filters.set("currency", currency);
  const reportFilters = new URLSearchParams(filters);
  reportFilters.delete("search");
  const allCurrencies = new URLSearchParams(reportFilters);
  allCurrencies.delete("currency");
  const [transactions, summary, spending, currencySummary] = await Promise.all([
    request<TransactionPage>(`/transactions?${filters}&pageSize=20`, options),
    request<{ currencies: (Summary & { currency: string })[] }>(`/reports/summary?${reportFilters}`, options),
    request<{ currencies: (Spending & { currency: string })[] }>(`/reports/spending?${reportFilters}`, options),
    request<{ currencies: { currency: string }[] }>(`/reports/summary?${allCurrencies}`, options),
  ]);
  const connections = [...new Map(accounts.map(a => [a.connectionId, {
    id: a.connectionId, institutionId: a.institutionId, status: a.connectionStatus, lastSyncAt: a.lastSyncAt,
  }])).values()];
  const institutions = [...new Map(accounts.map(a => [a.institutionId, {
    id: a.institutionId, name: a.institutionName, initials: a.institutionName.slice(0, 2).toUpperCase(),
  }])).values()];
  const report = { ...summary.currencies[0], ...spending.currencies[0] };
  return { seeded, currency, currencies: currencySummary.currencies.map(c => c.currency),
    asOf: seeded ? "2026-09-30" : new Date().toISOString().slice(0, 10), month,
    months: Array.from({ length: 6 }, (_, n) => {
      const anchor = new Date(`${seeded ? "2026-09" : new Date().toISOString().slice(0, 7)}-01T12:00:00Z`);
      anchor.setUTCMonth(anchor.getUTCMonth() - n);
      return anchor.toISOString().slice(0, 7);
    }), accounts, categories, institutions, connections,
    transactions: transactions.items, totalCount: transactions.totalCount, report };
}

export const money = (value: number, currency = "USD") =>
  new Intl.NumberFormat("en-US", { style: "currency", currency }).format(value);
export const monthName = (month: string, short = false) =>
  new Date(`${month}-01T12:00:00`).toLocaleDateString("en-US", {
    month: short ? "short" : "long",
    ...(short ? {} : { year: "numeric" }),
  });
export const dateName = (date: string) =>
  new Date(`${date}T12:00:00`).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
  });

export const transactionCategory = (
  transaction: Transaction,
  categories: Category[],
) =>
  transaction.classification === "income"
    ? "Income"
    : transaction.classification === "transfer"
      ? "Transfer"
      : transaction.classification === "card-payment"
        ? "Card payment"
        : (categories.find((category) => category.id === transaction.categoryId)
            ?.name ?? "Uncategorized");
