import { QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider, createBrowserRouter, createMemoryRouter } from "react-router";
import { useMemo } from "react";
import { createQueryClient } from "./api/queryClient";
import { AuthProvider } from "./auth/AuthProvider";
import { PendingSetIdsProvider } from "./session/PendingSetIdsProvider";
import { RestTimerProvider } from "./session/RestTimerProvider";
import { TelegramProvider } from "./telegram/TelegramProvider";
import type { TelegramRuntime } from "./telegram/runtime";
import { routes } from "./routes";

/**
 * The provider stack, outermost first.
 *
 * The order is load-bearing in two places:
 *
 *   - `RestTimerProvider` sits **above** the router, so a rest countdown survives navigating
 *     between screens. Inside the router it would be unmounted on every route change.
 *   - `PendingSetIdsProvider` also sits above the router, so a row's idempotency key survives a
 *     remount. An id lost mid-flight would let a re-tap create a duplicate set.
 *
 * `initialEntries` exists for tests, which need a memory router rather than the browser's history.
 */
export function App({
  runtime,
  initialEntries,
}: {
  runtime: TelegramRuntime;
  initialEntries?: string[];
}) {
  const queryClient = useMemo(() => createQueryClient(), []);
  const router = useMemo(
    () =>
      initialEntries === undefined
        ? createBrowserRouter(routes)
        : createMemoryRouter(routes, { initialEntries }),
    [initialEntries],
  );

  return (
    <TelegramProvider runtime={runtime}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <RestTimerProvider>
            <PendingSetIdsProvider>
              <RouterProvider router={router} />
            </PendingSetIdsProvider>
          </RestTimerProvider>
        </AuthProvider>
      </QueryClientProvider>
    </TelegramProvider>
  );
}
