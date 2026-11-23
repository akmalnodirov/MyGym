export function ErrorBanner({
  message,
  onRetry,
  retryLabel = "Try again",
}: {
  message: string;
  onRetry?: () => void;
  retryLabel?: string;
}) {
  return (
    <div className="banner banner--error" role="alert">
      <p className="banner__message">{message}</p>
      {onRetry === undefined ? null : (
        <button type="button" className="button button--secondary" onClick={onRetry}>
          {retryLabel}
        </button>
      )}
    </div>
  );
}
