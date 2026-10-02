import { useEffect, useState } from "react";
import { onAuthStateChanged, signOut } from "firebase/auth";
import type { User } from "firebase/auth";
import App from "./App";
import { AuthPage } from "./AuthPage";
import { auth, authErrorMessage } from "./firebase";

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
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(Boolean(auth));
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
    if (!auth) return;
    return onAuthStateChanged(
      auth,
      (nextUser) => {
        setUser(nextUser);
        setLoading(false);
      },
      (error) => {
        setSessionError(authErrorMessage(error));
        setLoading(false);
      },
    );
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
        key={user.uid}
        theme={theme}
        onToggleTheme={toggleTheme}
        userLabel={user.displayName || user.email || "Personal workspace"}
        onSignOut={async () => {
          if (auth) await signOut(auth);
        }}
      />
    );
  return (
    <AuthPage
      theme={theme}
      onToggleTheme={toggleTheme}
      sessionError={sessionError}
    />
  );
}
