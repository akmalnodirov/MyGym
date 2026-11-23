import { formatRemaining, useRestTimer } from "./RestTimerProvider";

/**
 * The sticky rest countdown. Rendered only while a timer is running, and always dismissable: it is
 * advisory, and must never stand between the athlete and the next set.
 */
export function RestTimerBar() {
  const { remainingMs, totalMs, label, dismiss } = useRestTimer();

  if (remainingMs === null || totalMs === null) return null;

  const elapsedFraction = totalMs === 0 ? 1 : 1 - remainingMs / totalMs;
  const done = remainingMs === 0;

  return (
    <div className={`rest-bar${done ? " rest-bar--done" : ""}`} role="status" aria-live="off">
      <span className="rest-bar__label">{done ? "Rest done" : `Rest ${formatRemaining(remainingMs)}`}</span>
      {label === null ? null : <span className="rest-bar__exercise">{label}</span>}
      <span className="rest-bar__track" aria-hidden="true">
        <span className="rest-bar__fill" style={{ width: `${Math.round(elapsedFraction * 100)}%` }} />
      </span>
      <button type="button" className="rest-bar__dismiss" aria-label="Dismiss rest timer" onClick={dismiss}>
        ✕
      </button>
    </div>
  );
}
