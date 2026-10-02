import { useEffect, useRef, useState } from "react";
import {
  dateName,
  loadWorkspace,
  money,
  monthName,
  request,
  transactionCategory,
} from "./api";
import type { Snapshot, Transaction, TransactionPage } from "./api";
import {
  CategoryBreakdown,
  Icon,
  SpendingChart,
  TransactionTable,
} from "./components";
import "./App.css";

type Page = "expense-report" | "transactions" | "accounts";
const currentPage = (): Page =>
  ["transactions", "accounts"].includes(location.hash.slice(1))
    ? (location.hash.slice(1) as Page)
    : "expense-report";
function App({
  theme,
  onToggleTheme,
  userLabel,
  onSignOut,
}: {
  theme: string;
  onToggleTheme: () => void;
  userLabel: string;
  onSignOut: () => Promise<void>;
}) {
  const [page, setPage] = useState<Page>(currentPage);
  const [data, setData] = useState<Snapshot | null>(null);
  const [month, setMonth] = useState("");
  const [accountId, setAccountId] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [search, setSearch] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [completedRequest, setCompletedRequest] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [currency, setCurrency] = useState("");
  const [institutionId, setInstitutionId] = useState("");
  const [selectedTransaction, setSelectedTransaction] =
    useState<Transaction | null>(null);
  const [editingCategory, setEditingCategory] = useState("");
  const currentRequest = useRef("");
  const dialog = useRef<HTMLDialogElement>(null);
  const hasFilters = Boolean(
    accountId || categoryId || (currency && currency !== data?.defaultCurrency) || (page === "transactions" && search),
  );
  const params = new URLSearchParams();
  if (month) params.set("month", month);
  if (accountId) params.set("accountId", accountId);
  if (categoryId) params.set("categoryId", categoryId);
  if (page === "transactions" && search) params.set("search", search);
  if (currency) params.set("currency", currency);
  const requestPath = params.toString();
  const requestKey = `${requestPath}|${page}|${refresh}`;
  const loading = completedRequest !== requestKey;

  useEffect(() => {
    const change = () => {
      const next = location.hash.slice(1) || "expense-report";
      if (["expense-report", "transactions", "accounts"].includes(next)) {
        setPage(next as Page);
        setNotice("");
        window.scrollTo(0, 0);
      }
    };
    window.addEventListener("hashchange", change);
    return () => window.removeEventListener("hashchange", change);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    currentRequest.current = requestKey;
    loadWorkspace(new URLSearchParams(requestPath), { signal: controller.signal })
      .then((snapshot) => {
        if (!controller.signal.aborted) {
          setData(snapshot);
          setError("");
        }
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted)
          setError(
            reason instanceof Error
              ? reason.message
              : "Could not load your expenses.",
          );
      })
      .finally(() => {
        if (!controller.signal.aborted) setCompletedRequest(requestKey);
      });
    return () => controller.abort();
  }, [requestPath, requestKey]);
  useEffect(() => {
    if (selectedTransaction && dialog.current && !dialog.current.open)
      dialog.current.showModal();
  }, [selectedTransaction]);

  async function mutate(
    path: string,
    body: object | undefined,
    message: string,
    method = "POST",
  ) {
    setBusy(true);
    setError("");
    try {
      await request(path, {
        method,
        ...(body ? { body: JSON.stringify(body) } : {}),
      });
      setRefresh((value) => value + 1);
      setNotice(message);
      return true;
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "The change could not be saved. Please try again.",
      );
      return false;
    } finally {
      setBusy(false);
    }
  }
  function openTransaction(transaction: Transaction) {
    setSelectedTransaction(transaction);
    setEditingCategory(transaction.manualCategoryId ?? "");
  }
  function closeTransaction() {
    dialog.current?.close();
    setSelectedTransaction(null);
  }
  function clearFilters() {
    setAccountId("");
    setCategoryId("");
    setSearch("");
    setCurrency("");
  }
  function viewCategory(id: string) {
    setCategoryId(id);
    location.hash = "transactions";
  }
  async function loadMore() {
    if (!data) return;
    const key = currentRequest.current;
    const filters = new URLSearchParams(requestPath);
    filters.set("month", data.month);
    filters.set("currency", data.currency);
    filters.set("page", String(Math.floor(data.transactions.length / 20) + 1));
    setBusy(true);
    setError("");
    try {
      const result = await request<TransactionPage>(`/transactions?${filters}`);
      if (currentRequest.current === key)
        setData(previous => previous ? { ...previous, transactions: [...previous.transactions, ...result.items], totalCount: result.totalCount } : previous);
    } catch (reason) {
      if (currentRequest.current === key) setError(reason instanceof Error ? reason.message : "Could not load more transactions.");
    } finally { setBusy(false); }
  }

  const connectedCount =
    data?.connections.filter((connection) => connection.status === "connected")
      .length ?? 0;
  const title =
    page === "expense-report"
      ? "Expense Report"
      : page === "transactions"
        ? "Your transactions"
        : "Connected accounts";
  const subtitle =
    page === "expense-report"
      ? "A clearer picture of where your money goes."
      : page === "transactions"
        ? "The details behind your spending."
        : "Your accounts, together in one place.";
  const report = data?.report;
  const availableInstitutions = data?.connectableInstitutions.filter(institution =>
    !data.connections.some(connection => connection.institutionId === institution.id && connection.status === "connected")) ?? [];
  const selectedInstitution = availableInstitutions.find(institution => institution.id === institutionId) ?? availableInstitutions[0];
  const priorMonthName = report && new Date(`${report.previousDateFrom}T12:00:00`).toLocaleDateString("en-US", {
    month: "long",
  });

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar" aria-label="Application navigation">
        <a
          className="brand"
          href="#expense-report"
          aria-label="DoughTracker Expense Report"
        >
          <span className="brand-mark">
            <Icon name="leaf" size={23} />
          </span>
          <span className="brand-wordmark">
            doughtracker<span className="brand-period">.</span>
          </span>
        </a>
        <span className="rail-label">YOUR WORKSPACE</span>
        <nav>
          {(["expense-report", "transactions", "accounts"] as const).map(
            (item) => (
              <a
                href={`#${item}`}
                key={item}
                className={page === item ? "nav-link active" : "nav-link"}
                aria-current={page === item ? "page" : undefined}
                aria-label={
                  item === "expense-report"
                    ? "Expense Report"
                    : item === "transactions"
                      ? "Transactions"
                      : "Accounts"
                }
              >
                <Icon name={item} />
                <span>
                  {item === "expense-report"
                    ? "Expense Report"
                    : item === "transactions"
                      ? "Transactions"
                      : "Accounts"}
                </span>
                {item === "accounts" && data && (
                  <span className="nav-count">{data.accounts.length}</span>
                )}
              </a>
            ),
          )}
        </nav>
        <div className="rail-bottom">
          <div className="connection-note">
            <span className="status-dot" />
            <span>{connectedCount} institutions connected</span>
            <a href="#accounts" aria-label="View connection status">
              <Icon name="chevron" size={16} />
            </a>
          </div>
          <button
            className="theme-button"
            onClick={onToggleTheme}
            aria-label={`Switch to ${theme === "light" ? "dark" : "light"} mode`}
          >
            <Icon name={theme === "light" ? "moon" : "sun"} />
            <span>{theme === "light" ? "Dark mode" : "Light mode"}</span>
            <span className="theme-switch" aria-hidden="true">
              <i />
            </span>
          </button>
          <button
            className="theme-button"
            aria-label="Sign out"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              try {
                await onSignOut();
              } catch {
                setError("We couldn’t sign you out. Please try again.");
              } finally {
                setBusy(false);
              }
            }}
          >
            <Icon name="logout" />
            <span>Sign out</span>
          </button>
          <div className="profile">
            <span className="profile-avatar">{userLabel[0].toUpperCase()}</span>
            <div>
              <strong title={userLabel}>{userLabel}</strong>
              <span>Personal ledger</span>
            </div>
          </div>
        </div>
      </aside>
      <main id="main-content" tabIndex={-1}>
        <div className="topbar">
          <span className="breadcrumb">
            Workspace <span>/</span>{" "}
            {page === "expense-report"
              ? "Expense Report"
              : page === "transactions"
                ? "Transactions"
                : "Accounts"}
          </span>
          <span className="demo-badge">
            <span className="status-dot" />
            Personal workspace
          </span>
        </div>
        <header className="page-heading">
          <div>
            <p className="eyebrow">YOUR MONEY, IN FOCUS</p>
            <h1>{title}</h1>
            <p className="subtitle">{subtitle}</p>
          </div>
        </header>
        <div className="feedback-space" aria-live="polite">
          {error && (
            <div className="feedback error" role="alert">
              <span>
                {error} {!data && "Check that the local API is running."}
              </span>
              <button
                className="text-button"
                onClick={() => setRefresh((value) => value + 1)}
              >
                Retry
              </button>
            </div>
          )}
          {notice && !error && (
            <div className="feedback success">
              <Icon name="check" size={17} />
              <span>{notice}</span>
              <button
                className="icon-button"
                aria-label="Dismiss notification"
                onClick={() => setNotice("")}
              >
                <Icon name="close" size={16} />
              </button>
            </div>
          )}
        </div>
        {!data ? (
          <div className="initial-state" role="status">
            <div className="loading-line" />
            <h2>
              {loading
                ? "Preparing your workspace"
                : "Your workspace is unavailable"}
            </h2>
            <p>
              {loading
                ? "Gathering accounts and expense history…"
                : "Start the API, then use Retry above."}
            </p>
          </div>
        ) : (
          <>
            {page !== "accounts" && (
              <div className="filter-bar">
                <label className="filter-label">
                  <span>Period</span>
                  <select
                    aria-label="Month"
                    value={month || data.month}
                    onChange={(event) => setMonth(event.target.value)}
                  >
                    {[...new Set([data.month, ...data.months])].sort().reverse().map((item) => (
                      <option key={item} value={item}>
                        {monthName(item)}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="filter-label">
                  <span>Account</span>
                  <select
                    aria-label="Account"
                    value={accountId}
                    onChange={(event) => setAccountId(event.target.value)}
                  >
                    <option value="">All accounts</option>
                    {data.accounts.map((account) => (
                      <option value={account.id} key={account.id}>
                        {account.name} •• {account.mask ?? "—"}
                      </option>
                    ))}
                  </select>
                </label>
                {page === "transactions" && (
                  <label className="filter-label">
                    <span>Category</span>
                    <select
                      value={categoryId}
                      onChange={(event) => setCategoryId(event.target.value)}
                    >
                      <option value="">All categories</option>
                      {data.categories.map((category) => (
                        <option key={category.id} value={category.id}>
                          {category.name}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
                {page === "expense-report" && categoryId && (
                  <span className="filter-chip">
                    {
                      data.categories.find(
                        (category) => category.id === categoryId,
                      )?.name
                    }
                  </span>
                )}
                {hasFilters && (
                  <button className="text-button" onClick={clearFilters}>
                    Clear filters
                  </button>
                )}
                {data.currencies.length > 1 && (
                  <label className="filter-label">
                    <span>Currency</span>
                    <select aria-label="Currency" value={currency || data.currency} onChange={event => setCurrency(event.target.value)}>
                      {[...new Set([data.currency, ...data.currencies])].map(code => <option key={code} value={code}>{code}</option>)}
                    </select>
                  </label>
                )}
                <span className="filter-end" role="status">
                  {loading
                    ? "Updating…"
                    : `${dateName(data.report.dateFrom)}–${dateName(data.report.daily.at(-1)!.date)}, ${data.report.dateFrom.slice(0, 4)}`}
                </span>
              </div>
            )}
            <div
              className={loading ? "page-content updating" : "page-content"}
              aria-busy={loading}
            >
              {page === "expense-report" && report && (
                <>
                  <section
                    className="summary-strip"
                    aria-label="Monthly spending summary"
                  >
                    <div className="primary-stat">
                      <span className="stat-label">Total spent this month</span>
                      <strong className="stat-value">
                        {money(report.spending, data.currency)}
                      </strong>
                      <span
                        className={`comparison ${report.changePercent !== null && report.changePercent <= 0 ? "decreased" : ""}`}
                      >
                        {report.changePercent === null ? (
                          "No prior-month history available"
                        ) : (
                          <>
                            <span className="change-pill">
                              <Icon
                                name={report.changePercent <= 0 ? "down" : "up"}
                                size={13}
                              />
                              {Math.abs(report.changePercent)}%
                            </span>{" "}
                            vs. {priorMonthName}{" "}
                            <span className="previous-total">
                              ({money(report.previousSpending, data.currency)})
                            </span>
                          </>
                        )}
                      </span>
                    </div>
                    <div>
                      <span className="stat-label">Daily average</span>
                      <strong className="stat-value secondary">
                        {money(report.dailyAverage, data.currency)}
                      </strong>
                      <span className="stat-footnote">
                        Across {report.daily.length} calendar days
                      </span>
                    </div>
                    <div>
                      <span className="stat-label">Posted purchases</span>
                      <strong className="stat-value secondary">
                        {report.expenseCount}
                        <span className="metric-unit">transactions</span>
                      </strong>
                      <span className="stat-footnote">
                        {money(report.refunds, data.currency)} in refunds deducted
                      </span>
                    </div>
                    <div>
                      <span className="stat-label">
                        Pending spending <span className="pending-dot" />
                      </span>
                      <strong className="stat-value secondary">
                        {money(report.pendingSpending, data.currency)}
                      </strong>
                      <span className="stat-footnote">
                        Not included in your total
                      </span>
                    </div>
                  </section>
                  <div className="report-grid">
                    <section className="panel spending-panel">
                      <div className="section-heading">
                        <div>
                          <h2>Spending over time</h2>
                          <p>Cumulative spending, net of refunds</p>
                        </div>
                        <span className="panel-kicker">MONTHLY VIEW</span>
                      </div>
                      <SpendingChart data={data} />
                    </section>
                    <section className="panel categories-panel">
                      <div className="section-heading">
                        <div>
                          <h2>Where it went</h2>
                          <p>Spending by category</p>
                        </div>
                      </div>
                      <CategoryBreakdown
                        data={data}
                        onCategory={viewCategory}
                      />
                    </section>
                  </div>
                  <div className="lower-grid">
                    <section className="panel recent-panel">
                      <div className="section-heading">
                        <div>
                          <h2>Recent activity</h2>
                          <p>The latest from your accounts</p>
                        </div>
                        <a className="text-link" href="#transactions">
                          All transactions <Icon name="arrow" size={16} />
                        </a>
                      </div>
                      <TransactionTable
                        data={data}
                        transactions={data.transactions.slice(0, 5)}
                        onOpen={openTransaction}
                      />
                    </section>
                    <section className="merchants-panel">
                      <div className="section-heading">
                        <div>
                          <h2>Top merchants</h2>
                          <p>By net posted spending</p>
                        </div>
                      </div>
                      {report.merchants.map((merchant, index) => (
                        <div className="merchant-ranking" key={merchant.id}>
                          <span className="rank">0{index + 1}</span>
                          <div>
                            <strong>{merchant.name}</strong>
                            <span>
                              {merchant.count}{" "}
                              {merchant.count === 1
                                ? "transaction"
                                : "transactions"}
                            </span>
                          </div>
                          <strong>{money(merchant.amount, data.currency)}</strong>
                        </div>
                      ))}
                      {!report.merchants.length && (
                        <p className="empty-copy">
                          Your top merchants will appear here when there are
                          posted expenses.
                        </p>
                      )}
                    </section>
                  </div>
                  <section className="history-section">
                    <div>
                      <h2>The bigger picture</h2>
                      <p>{report.trend.length} months of posted spending</p>
                    </div>
                    <div className="month-history">
                      {report.trend.map((item) => (
                        <button
                          key={item.month}
                          className={
                            item.month === data.month
                              ? "month-item selected"
                              : "month-item"
                          }
                          onClick={() => setMonth(item.month)}
                          aria-label={`View ${monthName(item.month)}, ${money(item.amount, data.currency)} spent`}
                          aria-pressed={item.month === data.month}
                        >
                          <span>{monthName(item.month, true)}</span>
                          <strong>{money(item.amount, data.currency)}</strong>
                        </button>
                      ))}
                    </div>
                  </section>
                  <p className="report-note">
                    Posted expenses only. Transfers and card payments are
                    excluded. Refunds reduce spending.
                  </p>
                </>
              )}
              {page === "transactions" && (
                <section className="panel transactions-panel">
                  <div className="transaction-toolbar">
                    <div>
                      <h2>Transaction ledger</h2>
                      <p>
                        {data.totalCount}{" "}
                        {data.totalCount === 1
                          ? "transaction"
                          : "transactions"}{" "}
                        · {money(report?.spending ?? 0, data.currency)} in posted expenses
                      </p>
                    </div>
                    <label className="search-input">
                      <Icon name="search" size={18} />
                      <span className="sr-only">Search transactions</span>
                      <input
                        type="search"
                        maxLength={120}
                        placeholder="Search merchants or descriptions"
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                      />
                    </label>
                  </div>
                  <TransactionTable
                    data={data}
                    transactions={data.transactions}
                    onOpen={openTransaction}
                  />
                  <div className="ledger-footer">
                    <span>
                      Showing {data.transactions.length}{" "}
                      of {data.totalCount}
                    </span>
                    {data.transactions.length < data.totalCount && (
                      <button
                        className="button"
                        disabled={busy || loading}
                        onClick={() => void loadMore()}
                      >
                        Show more
                      </button>
                    )}
                    <span>Purchases, refunds, income & transfers</span>
                  </div>
                </section>
              )}
              {page === "accounts" && (
                <>
                  {data.connectableInstitutions.length > 0 && (
                    <section className="panel connect-panel" aria-label="Connect an account">
                      <div className="section-heading">
                        <div>
                          <h2>Connect an account</h2>
                          <p>Choose a mock bank to add sample accounts and transactions.</p>
                        </div>
                      </div>
                      <form onSubmit={async event => {
                        event.preventDefault();
                        if (selectedInstitution) await mutate("/connections", { institutionId: selectedInstitution.id }, `${selectedInstitution.name} connected. Sample history added to your ledger.`);
                      }}>
                        <label>
                          <span>Bank</span>
                          <select value={selectedInstitution?.id ?? ""} onChange={event => setInstitutionId(event.target.value)} disabled={busy || loading || !availableInstitutions.length}>
                            {!availableInstitutions.length && <option value="">All mock banks connected</option>}
                            {availableInstitutions.map(institution => <option key={institution.id} value={institution.id}>{institution.name}</option>)}
                          </select>
                        </label>
                        <button type="submit" className="button primary" disabled={busy || loading || !selectedInstitution}>
                          <Icon name="plus" size={17} /> {busy ? "Connecting…" : "Connect account"}
                        </button>
                      </form>
                    </section>
                  )}
                  <div className="accounts-heading">
                    <span>
                      {data.accounts.length} accounts across{" "}
                      {data.connections.length} institutions
                    </span>
                    <span>Balances are last-known values</span>
                  </div>
                  {!data.accounts.length && <div className="empty-state"><h3>No accounts yet</h3><p>Your accounts will appear here after data is imported.</p></div>}
                  {data.connections.map((connection) => {
                    const institution = data.institutions.find(
                      (institution) =>
                        institution.id === connection.institutionId,
                    )!;
                    const accounts = data.accounts.filter(
                      (account) => account.connectionId === connection.id,
                    );
                    const connected = connection.status === "connected";
                    return (
                      <section
                        className="panel institution-panel"
                        key={connection.id}
                      >
                        <div className="institution-heading">
                          <span
                            className={`institution-avatar ${institution.id}`}
                          >
                            {institution.initials}
                          </span>
                          <div>
                            <h2>{institution.name}</h2>
                            <p>
                              {connection.lastSyncAt
                                ? `Last successful sync ${new Date(connection.lastSyncAt).toLocaleString("en-US", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" })}`
                                : "No successful sync recorded"}
                            </p>
                          </div>
                          <span
                            className={`connection-badge ${connected ? "" : "disconnected"}`}
                          >
                            <span className="status-dot" />
                            {connection.status.replaceAll("_", " ").replace(/^./, letter => letter.toUpperCase())}
                          </span>
                        </div>
                        <div className="account-list">
                          {accounts.map((account) => (
                            <div className="account-row" key={account.id}>
                              <Icon name="accounts" size={22} />
                              <div className="account-identity">
                                <strong>{account.name}</strong>
                                <span>
                                  {(account.subtype || account.type).replaceAll("_", " ").replace(/^./, letter => letter.toUpperCase())}{" "}
                                  · •• {account.mask ?? "—"}
                                </span>
                              </div>
                              <div>
                                <span>
                                  {account.type === "credit"
                                    ? "Amount owed"
                                    : "Current balance"}
                                </span>
                                <strong>
                                  {account.currentBalance === null
                                    ? "Unavailable"
                                    : money(
                                        account.currentBalance,
                                        account.currency,
                                      )}
                                </strong>
                              </div>
                              <div>
                                <span>
                                  {account.type === "credit"
                                    ? "Available credit"
                                    : "Available balance"}
                                </span>
                                <strong>
                                  {account.availableBalance === null
                                    ? "Not provided"
                                    : money(
                                        account.availableBalance,
                                        account.currency,
                                      )}
                                </strong>
                              </div>
                            </div>
                          ))}
                        </div>
                      </section>
                    );
                  })}
                  <div className="account-explainer">
                    <Icon name="leaf" size={23} />
                    <div>
                      <h3>Your account history</h3>
                      <p>
                        Account balances and imported history are stored in your
                        personal ledger. {data.connectableInstitutions.length ? "Mock connections use sample bank data. Real bank linking is coming next." : "Bank connection tools are coming next."}
                      </p>
                    </div>
                  </div>
                </>
              )}
            </div>
            <footer className="page-footer">
              <span>
                doughtracker<span className="brand-period">.</span>{" "}
                <span>A little more clarity.</span>
              </span>
              <span>
                {data.asOf
                  ? `${data.seeded ? "Sample history through" : "History through"} ${dateName(data.asOf)}, ${data.asOf.slice(0, 4)}`
                  : "No transaction history yet"}
              </span>
            </footer>
          </>
        )}
      </main>
      <dialog
        ref={dialog}
        className="transaction-dialog"
        onCancel={() => setSelectedTransaction(null)}
        onClose={() => setSelectedTransaction(null)}
        aria-labelledby="transaction-title"
      >
        {selectedTransaction && data && (
          <>
            <div className="section-heading">
              <span className="eyebrow">TRANSACTION DETAILS</span>
              <button
                className="icon-button"
                aria-label="Close transaction details"
                onClick={closeTransaction}
              >
                <Icon name="close" />
              </button>
            </div>
            <h2 id="transaction-title">{selectedTransaction.merchant}</h2>
            <div
              className={`detail-amount ${selectedTransaction.amount > 0 ? "inflow" : ""}`}
            >
              {selectedTransaction.amount > 0 ? "+" : "−"}
              {money(
                Math.abs(selectedTransaction.amount),
                selectedTransaction.currency,
              )}
            </div>
            <span className="detail-status">
              {selectedTransaction.pending
                ? "Pending · excluded from posted spending"
                : selectedTransaction.classification === "expense"
                  ? selectedTransaction.amount > 0
                    ? "Posted refund · reduces spending"
                    : "Posted expense"
                  : `${transactionCategory(selectedTransaction, data.categories)} · excluded from expense totals`}
            </span>
            <dl className="detail-list">
              <div>
                <dt>Account</dt>
                <dd>
                  {
                    data.accounts.find(
                      (account) => account.id === selectedTransaction.accountId,
                    )?.name
                  }
                </dd>
              </div>
              <div>
                <dt>
                  {selectedTransaction.pending
                    ? "Transaction date"
                    : "Posted date"}
                </dt>
                <dd>
                  {dateName(selectedTransaction.date)},{" "}
                  {selectedTransaction.date.slice(0, 4)}
                </dd>
              </div>
              <div>
                <dt>Authorized date</dt>
                <dd>
                  {selectedTransaction.authorizedDate
                    ? dateName(selectedTransaction.authorizedDate)
                    : "Not provided"}
                </dd>
              </div>
              <div>
                <dt>Original description</dt>
                <dd>{selectedTransaction.description}</dd>
              </div>
              <div>
                <dt>Payment channel</dt>
                <dd>{selectedTransaction.paymentChannel}</dd>
              </div>
              <div>
                <dt>Provider category</dt>
                <dd>
                  {data.categories.find(
                    (category) =>
                      category.id === selectedTransaction.providerCategoryId,
                  )?.name ?? "Uncategorized"}
                  {selectedTransaction.categoryConfidence && (
                    <span>
                      {" "}
                      ·{" "}
                      {selectedTransaction.categoryConfidence
                        .toLowerCase()
                        .replaceAll("_", " ")}{" "}
                      confidence
                    </span>
                  )}
                </dd>
              </div>
            </dl>
            {selectedTransaction.classification === "expense" && (
              <form
                className="category-form"
                onSubmit={async (event) => {
                  event.preventDefault();
                  if (
                    await mutate(
                      `/transactions/${selectedTransaction.id}/category`,
                      { categoryId: editingCategory || null },
                      "Category saved. Your reports have been updated.",
                      "PATCH",
                    )
                  )
                    closeTransaction();
                }}
              >
                <label>
                  Category
                  <select
                    value={editingCategory}
                    onChange={(event) => setEditingCategory(event.target.value)}
                  >
                    <option value="">Use provider category</option>
                    {data.categories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                  </select>
                </label>
                <p>
                  A manual category takes priority over future provider updates.
                </p>
                <button className="button primary" disabled={busy}>
                  {busy ? "Saving…" : "Save category"}
                </button>
                {error && (
                  <p className="dialog-error" role="alert">
                    {error}
                  </p>
                )}
              </form>
            )}
          </>
        )}
      </dialog>
    </div>
  );
}

export default App;
