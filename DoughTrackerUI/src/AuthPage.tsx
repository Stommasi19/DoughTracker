import { useState } from "react";
import type { FormEvent } from "react";
import { Icon } from "./components";
import { request } from "./api";
import type { UserProfile } from "./api";
import "./App.css";
import "./AuthPage.css";

type Mode = "sign-in" | "create";

export function AuthPage({
  theme,
  onToggleTheme,
  sessionError,
  onAuthenticated,
}: {
  theme: string;
  onToggleTheme: () => void;
  sessionError: string;
  onAuthenticated: (user: UserProfile) => void;
}) {
  const [mode, setMode] = useState<Mode>("sign-in");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  function changeMode(next: Mode) {
    setMode(next);
    setPassword("");
    setShowPassword(false);
    setError("");
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || sessionError) return;
    setBusy(true);
    setError("");
    try {
      const profile = await request<UserProfile>(
        mode === "create" ? "/auth/register" : "/auth/login",
        {
          method: "POST",
          body: JSON.stringify({ username: username.trim(), password }),
        },
      );
      onAuthenticated(profile);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Sign-in failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="auth-shell">
      <header className="auth-header">
        <a className="brand" href="#sign-in" aria-label="DoughTracker sign-in">
          <span className="brand-mark">
            <Icon name="leaf" size={23} />
          </span>
          <span>
            doughtracker<span className="brand-period">.</span>
          </span>
        </a>
        <button
          className="icon-button"
          onClick={onToggleTheme}
          aria-label={`Switch to ${theme === "light" ? "dark" : "light"} mode`}
        >
          <Icon name={theme === "light" ? "moon" : "sun"} />
        </button>
      </header>
      <main className="auth-layout">
        <section className="auth-intro" aria-label="About DoughTracker">
          <div>
            <p className="eyebrow">A LITTLE MORE CLARITY</p>
            <h2>
              Your money,
              <br />
              in focus.
            </h2>
            <p className="auth-description">
              A quieter place to understand your spending.
              <br />
              See the details. Find the patterns. Move forward.
            </p>
          </div>
          <dl className="auth-principles">
            <div>
              <dt>01 / Understand</dt>
              <dd>Your monthly expense report, clearly laid out.</dd>
            </div>
            <div>
              <dt>02 / Explore</dt>
              <dd>The transactions behind every total.</dd>
            </div>
            <div>
              <dt>03 / Connect</dt>
              <dd>Your accounts, together in one place.</dd>
            </div>
          </dl>
        </section>
        <section className="auth-panel" aria-labelledby="auth-title">
          <p className="eyebrow">YOUR WORKSPACE</p>
          <h1 id="auth-title">
            {mode === "create" ? "Create an account" : "Welcome back."}
          </h1>
          <p className="auth-subtitle">
            {mode === "create"
              ? "Choose a username and password for this installation."
              : "Sign in to pick up where you left off."}
          </p>
          <form onSubmit={submit} aria-busy={busy}>
            <fieldset disabled={busy}>
              <label htmlFor="auth-username">Username</label>
              <input
                id="auth-username"
                autoComplete="username"
                autoCapitalize="none"
                spellCheck={false}
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                required
                minLength={3}
                maxLength={40}
                pattern="[A-Za-z0-9_.\-]+"
                title="Use letters, numbers, dots, underscores, or hyphens."
              />
              <label htmlFor="auth-password">Password</label>
              <div className="auth-password">
                <input
                  id="auth-password"
                  type={showPassword ? "text" : "password"}
                  autoComplete={
                    mode === "create" ? "new-password" : "current-password"
                  }
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  required
                  minLength={mode === "create" ? 12 : undefined}
                  maxLength={256}
                  aria-describedby={
                    mode === "create"
                      ? "password-hint"
                      : error
                        ? "auth-error"
                        : undefined
                  }
                />
                <button
                  type="button"
                  className="text-button"
                  onClick={() => setShowPassword(!showPassword)}
                  aria-label={
                    showPassword ? "Hide password" : "Show password"
                  }
                  aria-pressed={showPassword}
                >
                  {showPassword ? "Hide" : "Show"}
                </button>
              </div>
              {mode === "create" && (
                <p className="auth-hint" id="password-hint">
                  Use at least 12 characters. A few unrelated words work
                  well.
                </p>
              )}
              <div aria-live="polite">
                {(error || sessionError) && (
                  <p className="auth-error" id="auth-error" role="alert">
                    {error || sessionError}
                    {sessionError && (
                      <button
                        type="button"
                        className="text-button"
                        onClick={() => location.reload()}
                      >
                        Retry sign-in
                      </button>
                    )}
                  </p>
                )}
              </div>
              <button
                className="button primary auth-submit"
                type="submit"
                disabled={Boolean(sessionError)}
              >
                {busy ? "Please wait…" : mode === "create" ? "Create account" : "Sign in"}
                <Icon name="arrow" size={18} />
              </button>
            </fieldset>
          </form>
          <div className="auth-mode">
            {mode === "sign-in" ? (
              <>
                New here?{" "}
                <button
                  className="text-button"
                  onClick={() => changeMode("create")}
                  disabled={busy}
                >
                  Create an account
                </button>
              </>
            ) : (
              <button
                className="text-button"
                onClick={() => changeMode("sign-in")}
                disabled={busy}
              >
                Back to sign in
              </button>
            )}
          </div>
        </section>
      </main>
    </div>
  );
}
