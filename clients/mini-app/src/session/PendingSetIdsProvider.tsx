import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import type { RowStatus } from "./setRows";

/**
 * The idempotency-key map, and the local status of in-flight writes.
 *
 * `POST /api/sessions/{id}/exercises/{exerciseId}/sets` takes a client-generated `clientSetId`,
 * and the backend holds a unique index on it: replaying the same id returns the original set
 * instead of creating a second one. The client's whole duty is to **reuse the same id for the same
 * logical set**.
 *
 * Why the id is keyed to the ROW and not minted at the tap, stated plainly because getting it
 * wrong duplicates data silently:
 *
 *   - TanStack Query's automatic retries replay the same mutation variables, so automatic retries
 *     are safe wherever the id comes from.
 *   - A **manual re-tap** after a visible failure is the dangerous case. If the first request
 *     actually reached the server and only its response was lost, a freshly minted id would create
 *     a second set. Keying the id to the row closes exactly that hole.
 *   - The id must also survive a remount, which is why this lives above the component tree rather
 *     than in component state.
 *
 * The two maps are deliberately different kinds of thing:
 *
 *   - **Ids are a ref.** Minting one happens inside an event handler and must not cause a render,
 *     and the value must survive a remount untouched.
 *   - **Statuses are state.** A row that fails has to visibly become "unsynced", so writing a
 *     status must re-render. Holding these in a ref was a real defect: the row stayed looking
 *     in-flight forever and the Retry affordance never appeared.
 */

interface PendingSetIds {
  /** Returns this row's id, minting one on first use. */
  idFor: (rowKey: string) => string;
  /** Called only once the set is confirmed logged. */
  release: (rowKey: string) => void;
  /** The id already assigned to a row, or null — never mints. */
  peek: (rowKey: string) => string | null;
  setStatus: (clientSetId: string, status: RowStatus) => void;
  clearStatus: (clientSetId: string) => void;
  /**
   * Statuses by clientSetId, exposed as the map rather than as a `statusOf(id)` lookup.
   *
   * A lookup function would have to close over this state, so its identity would change on every
   * status write. Anything listing it in an effect's dependencies would then re-run, write again,
   * and loop — which is exactly what happened: the test worker died with an out-of-memory abort.
   * A map is read during render, where a changing reference is the point rather than a hazard.
   */
  statuses: ReadonlyMap<string, RowStatus>;
}

const PendingSetIdsContext = createContext<PendingSetIds | null>(null);

export function PendingSetIdsProvider({ children }: { children: ReactNode }) {
  const ids = useRef(new Map<string, string>());
  const [statuses, setStatuses] = useState<ReadonlyMap<string, RowStatus>>(() => new Map());

  const idFor = useCallback((rowKey: string): string => {
    const existing = ids.current.get(rowKey);
    if (existing !== undefined) return existing;

    const minted = crypto.randomUUID();
    ids.current.set(rowKey, minted);
    return minted;
  }, []);

  const peek = useCallback((rowKey: string): string | null => ids.current.get(rowKey) ?? null, []);

  const clearStatus = useCallback((clientSetId: string): void => {
    setStatuses((current) => {
      if (!current.has(clientSetId)) return current;
      const next = new Map(current);
      next.delete(clientSetId);
      return next;
    });
  }, []);

  const release = useCallback(
    (rowKey: string): void => {
      const id = ids.current.get(rowKey);
      ids.current.delete(rowKey);
      if (id !== undefined) clearStatus(id);
    },
    [clearStatus],
  );

  const setStatus = useCallback((clientSetId: string, status: RowStatus): void => {
    setStatuses((current) => {
      if (current.get(clientSetId) === status) return current;
      const next = new Map(current);
      next.set(clientSetId, status);
      return next;
    });
  }, []);

  const value = useMemo(
    () => ({ idFor, release, peek, setStatus, clearStatus, statuses }),
    [idFor, release, peek, setStatus, clearStatus, statuses],
  );

  return <PendingSetIdsContext.Provider value={value}>{children}</PendingSetIdsContext.Provider>;
}

export function usePendingSetIds(): PendingSetIds {
  const value = useContext(PendingSetIdsContext);
  if (value === null) {
    throw new Error("usePendingSetIds must be used inside a PendingSetIdsProvider.");
  }
  return value;
}
