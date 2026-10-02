type LinkHandler = { open(): void; destroy(): void };
type LinkOptions = {
  token: string;
  receivedRedirectUri?: string;
  onSuccess(token: string | null): void;
  onExit(error: { display_message?: string } | null): void;
};
declare global {
  interface Window { Plaid?: { create(options: LinkOptions): LinkHandler } }
}

let loading: Promise<void> | undefined;
async function load() {
  if (window.Plaid) return;
  loading ??= new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    const timeout = window.setTimeout(() => fail(), 20000);
    function fail() {
      window.clearTimeout(timeout);
      script.remove(); loading = undefined;
      reject(new Error("Bank connection tools could not load. Please try again."));
    }
    script.src = "https://cdn.plaid.com/link/v2/stable/link-initialize.js";
    script.onload = () => { window.clearTimeout(timeout); if (window.Plaid) resolve(); else fail(); };
    script.onerror = fail;
    document.head.append(script);
  });
  await loading;
}

export async function openLink(token: string, signal: AbortSignal, receivedRedirectUri?: string): Promise<{ publicToken: string | null } | null> {
  await load();
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    function cleanup() { signal.removeEventListener("abort", abort); handler.destroy(); }
    function abort() { cleanup(); reject(new DOMException("Bank connection cancelled.", "AbortError")); }
    const handler = window.Plaid!.create({
      token, ...(receivedRedirectUri ? { receivedRedirectUri } : {}),
      onSuccess: publicToken => { cleanup(); resolve({ publicToken }); },
      onExit: error => {
        cleanup();
        if (error) reject(new Error(error.display_message || "Bank connection failed. Please try again."));
        else resolve(null);
      },
    });
    signal.addEventListener("abort", abort, { once: true });
    handler.open();
  });
}
