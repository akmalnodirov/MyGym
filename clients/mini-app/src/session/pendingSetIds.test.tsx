import { describe, expect, test } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useEffect } from "react";
import { PendingSetIdsProvider, usePendingSetIds } from "./PendingSetIdsProvider";

function Reader({ rowKey, onId }: { rowKey: string; onId: (id: string) => void }) {
  const { idFor } = usePendingSetIds();
  useEffect(() => {
    onId(idFor(rowKey));
  }, [idFor, rowKey, onId]);
  return null;
}

describe("the idempotency-key map", () => {
  test("returns the same id for the same row key across calls", () => {
    const seen: string[] = [];
    render(
      <PendingSetIdsProvider>
        <Reader rowKey="se1:Working:0" onId={(id) => seen.push(id)} />
        <Reader rowKey="se1:Working:0" onId={(id) => seen.push(id)} />
      </PendingSetIdsProvider>,
    );

    expect(seen).toHaveLength(2);
    expect(seen[0]).toBe(seen[1]);
  });

  test("returns different ids for different row keys", () => {
    const seen: string[] = [];
    render(
      <PendingSetIdsProvider>
        <Reader rowKey="se1:Working:0" onId={(id) => seen.push(id)} />
        <Reader rowKey="se1:Working:1" onId={(id) => seen.push(id)} />
        <Reader rowKey="se1:Warmup:0" onId={(id) => seen.push(id)} />
      </PendingSetIdsProvider>,
    );

    expect(new Set(seen).size).toBe(3);
  });

  test("keeps the id when a consumer unmounts and remounts", () => {
    // This is why the map lives in the provider rather than in component state: a scroll that
    // unmounts a row, or navigating away and back, must not change the id of a set that may
    // already be in flight.
    const seen: string[] = [];

    function Harness({ show }: { show: boolean }) {
      return (
        <PendingSetIdsProvider>{show ? <Reader rowKey="se1:Working:0" onId={(id) => seen.push(id)} /> : null}</PendingSetIdsProvider>
      );
    }

    // One provider instance throughout; only the child comes and goes.
    const view = render(<Harness show={true} />);
    view.rerender(<Harness show={false} />);
    view.rerender(<Harness show={true} />);

    expect(seen).toHaveLength(2);
    expect(seen[0]).toBe(seen[1]);
  });

  test("release frees the key, so a later row in the same slot gets a fresh id", () => {
    const seen: string[] = [];

    function Harness() {
      const { idFor, release } = usePendingSetIds();
      useEffect(() => {
        seen.push(idFor("se1:Working:0"));
        release("se1:Working:0");
        seen.push(idFor("se1:Working:0"));
      }, [idFor, release]);
      return null;
    }

    render(
      <PendingSetIdsProvider>
        <Harness />
      </PendingSetIdsProvider>,
    );

    expect(seen[0]).not.toBe(seen[1]);
  });

  test("peek does not mint an id", () => {
    const seen: (string | null)[] = [];

    function Harness() {
      const { idFor, peek } = usePendingSetIds();
      useEffect(() => {
        seen.push(peek("se1:Working:0"));
        idFor("se1:Working:0");
        seen.push(peek("se1:Working:0"));
      }, [idFor, peek]);
      return null;
    }

    render(
      <PendingSetIdsProvider>
        <Harness />
      </PendingSetIdsProvider>,
    );

    expect(seen[0]).toBeNull();
    expect(seen[1]).not.toBeNull();
  });

  test("ids are RFC 4122 shaped, because the API declares clientSetId as a uuid", () => {
    const seen: string[] = [];
    render(
      <PendingSetIdsProvider>
        <Reader rowKey="se1:Working:0" onId={(id) => seen.push(id)} />
      </PendingSetIdsProvider>,
    );

    expect(seen[0]).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  });

  test("a status write re-renders consumers, so a failed row can actually show as unsynced", async () => {
    // Statuses are state, not a ref. When this was a ref the row stayed looking in-flight forever
    // and the Retry affordance never appeared -- the defect this test exists to prevent.
    function Harness() {
      const { idFor, setStatus, statuses, release } = usePendingSetIds();
      const id = idFor("se1:Working:0");
      return (
        <div>
          <output data-testid="status">{statuses.get(id) ?? "none"}</output>
          <button type="button" onClick={() => setStatus(id, "inFlight")}>
            start
          </button>
          <button type="button" onClick={() => setStatus(id, "failed")}>
            fail
          </button>
          <button type="button" onClick={() => release("se1:Working:0")}>
            release
          </button>
        </div>
      );
    }

    render(
      <PendingSetIdsProvider>
        <Harness />
      </PendingSetIdsProvider>,
    );

    expect(screen.getByTestId("status")).toHaveTextContent("none");

    await userEvent.click(screen.getByRole("button", { name: "start" }));
    expect(screen.getByTestId("status")).toHaveTextContent("inFlight");

    await userEvent.click(screen.getByRole("button", { name: "fail" }));
    expect(screen.getByTestId("status")).toHaveTextContent("failed");

    await userEvent.click(screen.getByRole("button", { name: "release" }));
    expect(screen.getByTestId("status")).toHaveTextContent("none");
  });

  test("usePendingSetIds outside its provider is a clear error", () => {
    function Orphan() {
      usePendingSetIds();
      return null;
    }

    expect(() => render(<Orphan />)).toThrow(/PendingSetIdsProvider/);
  });
});
