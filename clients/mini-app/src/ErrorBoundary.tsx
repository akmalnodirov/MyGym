import { Component, type ErrorInfo, type ReactNode } from "react";

interface State {
  error: Error | null;
}

/**
 * Catches a render error so one broken screen cannot blank the whole app mid-workout.
 *
 * A class component because this is the only thing React has no hook for. Reload rather than
 * "dismiss": by the time a render has thrown, the component tree's state is not trustworthy, and
 * a fresh launch re-exchanges initData and refetches everything anyway.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  override state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // The console is the only sink there is: the backend has no client-error endpoint, and
    // inventing one here would be guessing at a contract. Recorded as a known gap.
    console.error("Unhandled render error", error, info.componentStack);
  }

  override render(): ReactNode {
    if (this.state.error === null) return this.props.children;

    return (
      <div className="screen screen--centred" role="alert">
        <p>Something broke on this screen.</p>
        <p className="list__secondary">Your logged sets are saved.</p>
        <button type="button" className="button button--primary" onClick={() => window.location.reload()}>
          Reload
        </button>
      </div>
    );
  }
}
