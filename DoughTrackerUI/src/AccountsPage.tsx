import { useEffect, useRef, useState } from "react";
import { request } from "./api";
import { Icon } from "./components";
import { openLink } from "./plaid";
import { readSeenNotices, saveSeenNotices } from "./reconnectNotices";

type Status = "connected" | "reconnect_required" | "unavailable";
type Account = { id: string; name: string; type: string; mask: string | null };
type Bank = {
  id: string;
  institutionName: string | null;
  status: Status;
  accounts: Account[];
};

const statusLabel: Record<Status, string> = {
  connected: "Connected",
  reconnect_required: "Reconnect required",
  unavailable: "Status unavailable",
};

const bankName = (bank: Bank) => bank.institutionName || "Linked bank";

export function AccountsPage({ userId }: { userId: string }) {
  const [banks, setBanks] = useState<Bank[]>();
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [connecting, setConnecting] = useState(false);
  const [refreshing, setRefreshing] = useState(true);
  const [notices, setNotices] = useState<Bank[]>([]);
  const dialog = useRef<HTMLDialogElement>(null);
  const linkRequest = useRef<AbortController>(null);
  const seenNotices = useRef<Set<string> | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    request<{ items: Bank[] }>("/connections", { signal: controller.signal })
      .then(({ items }) => {
        if (controller.signal.aborted) return;
        setBanks(items);
        setError("");

        const seen = seenNotices.current ?? readSeenNotices(userId);
        const newlyAffected = items.filter(
          (bank) => bank.status === "reconnect_required" && !seen.has(bank.id),
        );
        // A temporary outage can't establish that a previously flagged bank recovered.
        const stillAffected = items.filter((bank) =>
          bank.status === "reconnect_required" || (bank.status === "unavailable" && seen.has(bank.id)),
        );
        const affectedIds = stillAffected.map((bank) => bank.id);
        seenNotices.current = new Set(affectedIds);
        saveSeenNotices(userId, affectedIds);
        if (newlyAffected.length) setNotices(newlyAffected);
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(
          reason instanceof Error ? reason.message : "Could not load linked banks.",
        );
      })
      .finally(() => {
        if (!controller.signal.aborted) setRefreshing(false);
      });
    return () => controller.abort();
  }, [refresh, userId]);

  useEffect(() => {
    if (notices.length && dialog.current && !dialog.current.open) dialog.current.showModal();
  }, [notices]);
  useEffect(() => () => linkRequest.current?.abort(), []);

  function refreshBanks() {
    setError("");
    setRefreshing(true);
    setRefresh((version) => version + 1);
  }

  async function connectBank() {
    const controller = new AbortController();
    linkRequest.current = controller;
    setConnecting(true);
    setError("");
    try {
      const { linkToken } = await request<{ linkToken: string }>("/connections/link-token", {
        method: "POST",
        signal: controller.signal,
      });
      const link = await openLink(linkToken, controller.signal);
      if (!link?.publicToken) return;
      await request("/connections/exchange-token", {
        method: "POST",
        body: JSON.stringify({ publicToken: link.publicToken }),
        signal: controller.signal,
      });
      if (!controller.signal.aborted) refreshBanks();
    } catch (reason) {
      if (!controller.signal.aborted) setError(
        reason instanceof Error ? reason.message : "Could not link your bank.",
      );
    } finally {
      if (!controller.signal.aborted) setConnecting(false);
    }
  }

  return (
    <>
      <section className="panel" aria-label="Connect a bank">
        <div className="section-heading">
          <div>
            <h2>Connect a bank</h2>
            <p>Link a test bank through Plaid Sandbox.</p>
          </div>
          <button className="button primary" disabled={connecting || refreshing} onClick={() => void connectBank()}>
            <Icon name="plus" size={17} />
            {connecting ? "Connecting…" : "Connect bank"}
          </button>
        </div>
      </section>
      <div className="section-heading bank-list-heading">
        <h2>Linked banks</h2>
        <button className="button" onClick={refreshBanks} disabled={refreshing || connecting}>
          {refreshing ? "Checking…" : "Refresh status"}
        </button>
      </div>
      {error && <p className="feedback error" role="alert">{error}</p>}
      {!banks && refreshing && <p role="status">Checking your linked banks…</p>}
      {banks?.length === 0 && <p className="empty-state">No banks linked yet.</p>}
      {banks?.map((bank) => (
        <section key={bank.id} className="panel institution-panel">
          <div className="institution-heading">
            <span className="institution-avatar"><Icon name="accounts" size={22} /></span>
            <div><h3>{bankName(bank)}</h3></div>
            <span className={`connection-badge ${bank.status === "connected" ? "" : "disconnected"}`}>
              <span className="status-dot" />
              {statusLabel[bank.status]}
            </span>
          </div>
          {bank.accounts.length > 0 && (
            <ul className="linked-account-list">
              {bank.accounts.map((account) => (
                <li key={account.id} className="linked-account-row">
                  <strong>{account.name}</strong>
                  <span>{account.type}{account.mask && ` · •• ${account.mask}`}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      ))}
      <dialog
        ref={dialog}
        className="transaction-dialog reconnect-dialog"
        aria-labelledby="reconnect-title"
        onClose={() => setNotices([])}
      >
        <h2 id="reconnect-title">Bank reconnection required</h2>
        <p>Plaid requires you to reconnect the following banks. Your connections are still saved.</p>
        <ul>{notices.map((bank) => <li key={bank.id}>{bankName(bank)}</li>)}</ul>
        <form method="dialog">
          <button className="button primary">Got it</button>
        </form>
      </dialog>
    </>
  );
}
