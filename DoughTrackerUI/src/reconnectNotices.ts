const storageKey = (userId: string) => `doughtracker-reconnect:${userId}`;

export function readSeenNotices(userId: string): Set<string> {
  try {
    return new Set(JSON.parse(sessionStorage.getItem(storageKey(userId)) || "[]"));
  } catch {
    return new Set();
  }
}

export function saveSeenNotices(userId: string, banks: string[]) {
  try {
    if (banks.length) sessionStorage.setItem(storageKey(userId), JSON.stringify(banks));
    else sessionStorage.removeItem(storageKey(userId));
  } catch {
    // The account view also keeps its own notice state.
  }
}
