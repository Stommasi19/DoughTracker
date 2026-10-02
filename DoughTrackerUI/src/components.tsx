import type { CSSProperties } from "react";
import { dateName, money, monthName, transactionCategory } from "./api";
import type { Snapshot, Transaction } from "./api";

const paths = {
  "expense-report": "M3 13h4v7H3z M10 8h4v12h-4z M17 3h4v17h-4z",
  transactions: "M4 5h16M4 12h16M4 19h16M8 3v4M16 10v4M8 17v4",
  accounts: "M3 9l9-6 9 6M4 10h16M6 10v8M12 10v8M18 10v8M3 21h18",
  plus: "M12 5v14M5 12h14",
  arrow: "M5 12h14M14 7l5 5-5 5",
  chevron: "M9 5l7 7-7 7",
  sun: "M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6L7 7M17 17l1.4 1.4M5.6 18.4L7 17M17 7l1.4-1.4M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
  moon: "M20.8 13.4A9 9 0 0 1 10.6 3.2a7 7 0 0 0 10.2 10.2Z",
  search: "M15.5 15.5L21 21M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0",
  close: "M6 6l12 12M6 18L18 6",
  sync: "M20 7v5h-5M4 17v-5h5M5.1 7a8 8 0 0 1 13-2L20 8M4 16l1.9 3a8 8 0 0 0 13-2",
  check: "M5 12l4 4L19 6",
  leaf: "M19 4C8 3 3 7 5 14c2 7 13 6 14-10ZM5 19l9-10",
  logout: "M9 4H4v16h5M10 12h11M17 8l4 4-4 4",
  down: "M12 5v14M7 14l5 5 5-5",
  up: "M12 19V5M7 10l5-5 5 5",
} as const;

export function Icon({
  name,
  size = 20,
}: {
  name: keyof typeof paths;
  size?: number;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name]} />
    </svg>
  );
}

export function SpendingChart({ data }: { data: Snapshot }) {
  const { daily, spending, previousSpending } = data.report;
  const values = daily.flatMap((day) => [day.current, day.previous]);
  const max = Math.max(1, ...values);
  const min = Math.min(0, ...values);
  const x = (index: number) =>
    56 + (index / Math.max(1, daily.length - 1)) * 590;
  const y = (value: number) => 206 - ((value - min) / (max - min)) * 180;
  const line = (field: "current" | "previous") =>
    daily
      .map(
        (day, index) =>
          `${index === 0 ? "M" : "L"}${x(index).toFixed(1)},${y(day[field]).toFixed(1)}`,
      )
      .join(" ");
  return (
    <>
      <div className="chart-legend">
        <span>
          <i className="legend-line" />
          {monthName(data.month, true)}
        </span>
        <span>
          <i className="legend-line previous" />
          Previous month
        </span>
      </div>
      <svg
        className="spending-chart"
        viewBox="0 0 670 245"
        role="img"
        aria-label={`Cumulative posted spending: ${money(spending, data.currency)} this month, ${money(previousSpending, data.currency)} previous month.`}
      >
        {[0, 1, 2, 3, 4].map((step) => {
          const value = min + ((max - min) * step) / 4;
          return (
            <g key={step}>
              <line
                x1="56"
                x2="646"
                y1={y(value)}
                y2={y(value)}
                className="chart-grid"
              />
              <text x="42" y={y(value) + 4} textAnchor="end">
                {new Intl.NumberFormat("en-US", { style: "currency", currency: data.currency, notation: "compact" }).format(value)}
              </text>
            </g>
          );
        })}
        <path
          d={`${line("current")} L646,${y(0)} L56,${y(0)} Z`}
          className="chart-fill"
        />
        <path d={line("previous")} className="chart-previous" />
        <path d={line("current")} className="chart-current" />
        <circle
          cx="646"
          cy={y(daily.at(-1)?.current ?? 0)}
          r="4"
          className="chart-end"
        />
        {[1, 5, 10, 15, 20, 25, daily.length]
          .filter((day, index, list) => list.indexOf(day) === index)
          .map((day) => (
            <text key={day} x={x(day - 1)} y="236" textAnchor="middle">
              {day === 1 ? `${monthName(data.month, true)} 1` : day}
            </text>
          ))}
      </svg>
      <details className="chart-data">
        <summary>View daily chart data</summary>
        <table>
          <caption>Cumulative posted expenses, net of refunds</caption>
          <thead>
            <tr>
              <th>Day</th>
              <th>This month</th>
              <th>Previous month</th>
            </tr>
          </thead>
          <tbody>
            {daily.map((day) => (
              <tr key={day.day}>
                <td>{day.day}</td>
                <td>{money(day.current, data.currency)}</td>
                <td>{money(day.previous, data.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </>
  );
}

export function CategoryBreakdown({
  data,
  onCategory,
}: {
  data: Snapshot;
  onCategory: (id: string) => void;
}) {
  const max = Math.max(
    1,
    ...data.report.categories.map((category) => Math.abs(category.amount)),
  );
  return (
    <div className="category-list">
      {data.report.categories.length ? (
        data.report.categories.map((category) => (
          <button
            className="category-row"
            key={category.id}
            onClick={() => onCategory(category.id)}
            aria-label={`View ${category.name} transactions, ${money(category.amount, data.currency)}`}
          >
            <div className="category-label">
              <span>
                <i
                  className="category-dot"
                  style={
                    {
                      "--category-color": `var(--category-${category.id}, var(--accent))`,
                    } as CSSProperties
                  }
                />
                {category.name}
              </span>
              <strong>{money(category.amount, data.currency)}</strong>
            </div>
            <div className="category-track" aria-hidden="true">
              <div
                style={{
                  width: `${(Math.abs(category.amount) / max) * 100}%`,
                  background: `var(--category-${category.id}, var(--accent))`,
                }}
              />
            </div>
          </button>
        ))
      ) : (
        <p className="empty-copy">No posted expenses match these filters.</p>
      )}
    </div>
  );
}

export function TransactionTable({
  data,
  transactions,
  onOpen,
}: {
  data: Snapshot;
  transactions: Transaction[];
  onOpen: (transaction: Transaction) => void;
}) {
  return (
    <div className="table-scroll">
      <table className="ledger">
        <caption className="sr-only">
          Transactions for {monthName(data.month)}
        </caption>
        <thead>
          <tr>
            <th>Merchant / description</th>
            <th>Category</th>
            <th>Account</th>
            <th>Date</th>
            <th className="amount">Amount</th>
          </tr>
        </thead>
        <tbody>
          {transactions.map((transaction) => {
            const account = data.accounts.find(
              (account) => account.id === transaction.accountId,
            );
            const status = transaction.pending
              ? "Pending"
              : transaction.classification === "expense" &&
                  transaction.amount > 0
                ? "Refund"
                : null;
            return (
              <tr key={transaction.id}>
                <td>
                  <button
                    className="merchant-button"
                    onClick={() => onOpen(transaction)}
                  >
                    <span
                      className={`merchant-avatar ${transaction.categoryId}`}
                      aria-hidden="true"
                    >
                      {transaction.merchant.slice(0, 1)}
                    </span>
                    <span>
                      <span className="merchant-name">
                        {transaction.merchant}
                      </span>
                      {status && (
                        <span
                          className={`transaction-status ${status.toLowerCase()}`}
                        >
                          {status}
                        </span>
                      )}
                    </span>
                  </button>
                </td>
                <td>
                  <span className="category-tag">
                    {transactionCategory(transaction, data.categories)}
                  </span>
                  {transaction.manualCategoryId && (
                    <span className="sr-only">Manually categorized</span>
                  )}
                </td>
                <td className="account-cell">
                  {account?.name ?? "Unknown account"}
                  <span>•• {account?.mask ?? "—"}</span>
                </td>
                <td className="date-cell">{dateName(transaction.date)}</td>
                <td
                  className={`amount ${transaction.amount > 0 ? "inflow" : ""}`}
                >
                  {transaction.amount > 0 ? "+" : "−"}
                  {money(Math.abs(transaction.amount), transaction.currency)}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {!transactions.length && (
        <div className="empty-state">
          <Icon name="search" size={28} />
          <h3>No matching transactions</h3>
          <p>Try another month or clear your search and filters.</p>
        </div>
      )}
    </div>
  );
}
