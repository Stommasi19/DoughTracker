import { useState } from "react";
import type { FormEvent } from "react";
import {
  createUserWithEmailAndPassword,
  GoogleAuthProvider,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signInWithPopup,
} from "firebase/auth";
import { Icon } from "./components";
import {
  auth,
  authConfigurationError,
  authErrorMessage,
} from "./firebase";
import "./App.css";
import "./AuthPage.css";

type Mode = "sign-in" | "create" | "reset";

export function AuthPage({
  theme,
  onToggleTheme,
  sessionError,
}: {
  theme: string;
  onToggleTheme: () => void;
  sessionError: string;
}) {
  const [mode, setMode] = useState<Mode>("sign-in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  function changeMode(next: Mode) {
    setMode(next);
    setPassword("");
    setShowPassword(false);
    setError("");
    setNotice("");
  }
  async function submit(event?: FormEvent<HTMLFormElement>) {
    event?.preventDefault();
    if (!auth || busy) return;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      if (!event) {
        const provider = new GoogleAuthProvider();
        provider.setCustomParameters({ prompt: "select_account" });
        await signInWithPopup(auth, provider);
      } else if (mode === "reset") {
        await sendPasswordResetEmail(auth, email.trim());
        setNotice(
          "If an account exists for this email, you’ll receive a password reset link.",
        );
      } else if (mode === "create") {
        await createUserWithEmailAndPassword(auth, email.trim(), password);
      } else {
        await signInWithEmailAndPassword(auth, email.trim(), password);
      }
    } catch (reason) {
      // Keep the reset response neutral even when enumeration protection is off.
      if (
        mode === "reset" &&
        event &&
        (reason as { code?: string })?.code === "auth/user-not-found"
      )
        setNotice(
          "If an account exists for this email, you’ll receive a password reset link.",
        );
      else setError(authErrorMessage(reason));
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
            {mode === "create"
              ? "Make yourself at home."
              : mode === "reset"
                ? "A fresh start."
                : "Welcome back."}
          </h1>
          <p className="auth-subtitle">
            {mode === "create"
              ? "Create an account to get started."
              : mode === "reset"
                ? "We’ll send you a link to reset your password."
                : "Sign in to pick up where you left off."}
          </p>
          {mode !== "reset" && (
            <>
              <button
                className="button auth-google"
                onClick={() => void submit()}
                disabled={busy || !auth || Boolean(sessionError)}
              >
                Continue with Google
              </button>
              <div className="auth-divider">
                <span>or continue with email</span>
              </div>
            </>
          )}
          <form onSubmit={submit} aria-busy={busy}>
            <fieldset disabled={busy}>
              <label htmlFor="auth-email">Email address</label>
              <input
                id="auth-email"
                type="email"
                autoComplete="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                placeholder="you@example.com"
                required
                maxLength={254}
                aria-describedby={error ? "auth-error" : undefined}
              />
              {mode !== "reset" && (
                <>
                  <div className="auth-password-label">
                    <label htmlFor="auth-password">Password</label>
                    {mode === "sign-in" && (
                      <button
                        type="button"
                        className="text-button"
                        onClick={() => changeMode("reset")}
                      >
                        Forgot password?
                      </button>
                    )}
                  </div>
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
                      maxLength={4096}
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
                </>
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
                {notice && (
                  <p className="auth-notice" role="status">
                    {notice}
                  </p>
                )}
              </div>
              <button
                className="button primary auth-submit"
                type="submit"
                disabled={!auth || Boolean(sessionError)}
              >
                {busy
                  ? "Please wait…"
                  : mode === "create"
                    ? "Create account"
                    : mode === "reset"
                      ? "Send reset link"
                      : "Sign in"}
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
          <p className="auth-session-note">
            Your sign-in session ends when you close this tab.
          </p>
          {authConfigurationError && (
            <div className="auth-setup">
              <strong>Sign-in setup</strong>
              <p>{authConfigurationError}</p>
            </div>
          )}
        </section>
      </main>
      <footer className="auth-footer">
        <span>Made for everyday clarity.</span>
        <span>Firebase Authentication</span>
      </footer>
    </div>
  );
}
