import { useEffect, useRef } from "react";
import {
  Navigate,
  Outlet,
  useLocation,
  useNavigate,
  useParams,
  type RouteObject,
} from "react-router";
import { useBackButton } from "./telegram/useBackButton";
import { useThemeParams } from "./telegram/useThemeParams";
import { ErrorBoundary } from "./ErrorBoundary";
import { TodayScreen } from "./today/TodayScreen";
import { DayPreviewScreen } from "./preview/DayPreviewScreen";
import { SessionScreen } from "./session/SessionScreen";
import { HistoryScreen } from "./history/HistoryScreen";
import { ProfileScreen } from "./profile/ProfileScreen";

/**
 * The shell every route renders inside.
 *
 * It owns the two pieces of Telegram chrome that are global rather than per-screen: the theme
 * variables, and the back button's mapping to history depth. Screens declare their main button
 * individually, because that one changes with what the screen does.
 */
function Shell() {
  const navigate = useNavigate();
  const location = useLocation();
  useThemeParams();

  // Whether there is anywhere to go back to comes from the route, not from window.history.
  // Reading window.history.state.idx coupled this to the browser router -- a memory router never
  // touches it -- and it is also wrong for a deep link: Telegram can open the app straight on a
  // session with an empty history, where the athlete still needs a way back to Today.
  const atRoot = location.pathname === "/";

  // Counts in-app navigations so going back can use history when there is history, and fall back
  // to Today when the app was opened directly on a child route.
  const visits = useRef(0);
  useEffect(() => {
    visits.current += 1;
  }, [location.key]);

  useBackButton({
    canGoBack: !atRoot,
    onBack: () => void (visits.current > 1 ? navigate(-1) : navigate("/", { replace: true })),
  });

  return (
    <ErrorBoundary>
      <Outlet />
    </ErrorBoundary>
  );
}

function TodayRoute() {
  const navigate = useNavigate();
  return (
    <TodayScreen
      onOpenDay={(programId, dayId) => void navigate(`/programs/${programId}/days/${dayId}`)}
      onResume={(sessionId) => void navigate(`/sessions/${sessionId}`)}
      onOpenHistory={() => void navigate("/history")}
      onOpenProfile={() => void navigate("/profile")}
    />
  );
}

function DayPreviewRoute() {
  const navigate = useNavigate();
  const { programId, dayId } = useParams();

  if (programId === undefined || dayId === undefined) return <Navigate to="/" replace />;

  return (
    <DayPreviewScreen
      programId={programId}
      dayId={dayId}
      // replace, not push: the preview is where Start was tapped, and going back to it from a live
      // session would offer to start a second one.
      onStarted={(sessionId) => void navigate(`/sessions/${sessionId}`, { replace: true })}
      onResumeCurrent={() => void navigate("/", { replace: true })}
      onLeave={() => void navigate("/", { replace: true })}
    />
  );
}

function SessionRoute() {
  const navigate = useNavigate();
  const { sessionId } = useParams();

  if (sessionId === undefined) return <Navigate to="/" replace />;

  return (
    <SessionScreen sessionId={sessionId} onLeave={() => void navigate("/", { replace: true })} />
  );
}

function HistoryRoute() {
  const navigate = useNavigate();
  return <HistoryScreen onOpenSession={(sessionId) => void navigate(`/sessions/${sessionId}`)} />;
}

function ProfileRoute() {
  const navigate = useNavigate();
  return <ProfileScreen onSaved={() => void navigate("/", { replace: true })} />;
}

export const routes: RouteObject[] = [
  {
    path: "/",
    element: <Shell />,
    children: [
      { index: true, element: <TodayRoute /> },
      { path: "programs/:programId/days/:dayId", element: <DayPreviewRoute /> },
      { path: "sessions/:sessionId", element: <SessionRoute /> },
      { path: "history", element: <HistoryRoute /> },
      { path: "profile", element: <ProfileRoute /> },
      // Telegram can open the Mini App on a start-parameter path we do not serve; land on Today
      // rather than on nothing.
      { path: "*", element: <Navigate to="/" replace /> },
    ],
  },
];
