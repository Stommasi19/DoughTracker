import { useEffect, useState } from "react";
import App from "./App";
import { saveSeenNotices } from "./reconnectNotices";
import { AuthPage } from "./AuthPage";
import { ApiError, request } from "./api";
import type { UserProfile } from "./api";

const initialTheme = () => {
  try {
    return localStorage.getItem("doughtracker-theme") === "dark"
      ? "dark"
      : "light";
  } catch {
    return "light";
  }
};

export default function Session() {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [sessionError, setSessionError] = useState("");
  const [theme, setTheme] = useState(initialTheme);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem("doughtracker-theme", theme);
    } catch {
      /* Theme still works without storage. */
    }
  }, [theme]);
  useEffect(() => {
    const controller = new AbortController();
    request<UserProfile>("/me", { signal: controller.signal })
      .then((profile) => {
        if (!controller.signal.aborted) setUser(profile);
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || (error instanceof ApiError && error.status === 401)) return;
        setSessionError("Could not reach the local DoughTracker API. Start it and retry.");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  const toggleTheme = () => setTheme(theme === "light" ? "dark" : "light");
  if (loading)
    return (
      <div className="session-loading" role="status">
        Preparing sign-in…
      </div>
    );
  if (user)
    return (
      <App
        key={user.id}
        userId={user.id}
        theme={theme}
        onToggleTheme={toggleTheme}
        userLabel={user.username}
        onSignOut={async () => {
          await request("/auth/logout", { method: "POST" });
          saveSeenNotices(user.id, []);
          setUser(null);
        }}
      />
    );
  return (
    <AuthPage
      theme={theme}
      onToggleTheme={toggleTheme}
      sessionError={sessionError}
      onAuthenticated={(profile) => {
        saveSeenNotices(profile.id, []);
        setUser(profile);
      }}
    />
  );
}
