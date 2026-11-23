import { useEffect, useMemo, useState } from "react";
import type { components } from "../api/generated";
import { useSessionSummaries } from "../api/queries";
import { EmptyState } from "../ui/EmptyState";
import { ErrorBanner } from "../ui/ErrorBanner";
import { Spinner } from "../ui/Spinner";

type SessionSummary = components["schemas"]["SessionSummaryResponse"];

const PAGE_SIZE = 20;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
  });
}

export function HistoryScreen({ onOpenSession }: { onOpenSession: (sessionId: string) => void }) {
  const [page, setPage] = useState(1);
  // Pages are kept by number so "Load more" appends. Each page is its own query, so the cache
  // keeps them independently and going back to page 1 does not refetch.
  const [pages, setPages] = useState<Record<number, SessionSummary[]>>({});
  const query = useSessionSummaries(page);

  useEffect(() => {
    if (query.data === undefined) return;
    setPages((current) => ({ ...current, [page]: query.data }));
  }, [query.data, page]);

  const sessions = useMemo(
    () =>
      Object.keys(pages)
        .map(Number)
        .sort((a, b) => a - b)
        .flatMap((number) => pages[number] ?? []),
    [pages],
  );

  if (query.isError) {
    return <ErrorBanner message="Could not load your history." onRetry={() => void query.refetch()} />;
  }

  if (!query.isSuccess && sessions.length === 0) return <Spinner label="Loading history" />;

  const lastPage = pages[page] ?? [];
  const mightHaveMore = lastPage.length === PAGE_SIZE;

  return (
    <div className="screen">
      <header className="session__header">
        <h1 className="session__title">History</h1>
      </header>

      {sessions.length === 0 ? (
        <EmptyState title="No workouts yet">
          <p>Finish a workout and it will show up here.</p>
        </EmptyState>
      ) : (
        <ul className="list">
          {sessions.map((session) => (
            <li className="list__item" key={session.id}>
              <button type="button" className="list__button" onClick={() => onOpenSession(session.id)}>
                <span>
                  <span className="list__primary">{formatDate(session.startedAt)}</span>
                  <span className="list__secondary">
                    {session.workingSetCount} working set{session.workingSetCount === 1 ? "" : "s"}
                    {session.status === "Completed" ? "" : ` · ${session.status}`}
                  </span>
                </span>
                <span aria-hidden="true">›</span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {!mightHaveMore ? null : (
        <button
          type="button"
          className="button button--secondary"
          disabled={query.isFetching}
          onClick={() => setPage((n) => n + 1)}
        >
          {query.isFetching ? "Loading…" : "Load more"}
        </button>
      )}
    </div>
  );
}
