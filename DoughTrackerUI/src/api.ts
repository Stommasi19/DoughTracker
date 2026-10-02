import { apiToken } from "./firebase";

export type Category = { id: string; name: string };
export type Institution = { id: string; name: string; initials: string };
export type Connection = {
  institutionId: string;
  status: string;
  lastSyncAt: string;
};
export type Account = {
  id: string;
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
  const response = await fetch(`/api/demo${path}`, {
    ...options,
    headers,
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(
      detail?.error ??
        "The demo API could not complete this request. Please try again.",
    );
  }
  const body = await response.text();
  return body ? (JSON.parse(body) as T) : (undefined as T);
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
