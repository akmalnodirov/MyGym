import { useProgram, useProgramSummaries, useCurrentSession } from "../api/queries";
import { ErrorBanner } from "../ui/ErrorBanner";
import { EmptyState } from "../ui/EmptyState";
import { Spinner } from "../ui/Spinner";
import { pickTodaysDay } from "./todaysDay";

export interface TodayScreenProps {
  onOpenDay: (programId: string, dayId: string) => void;
  onResume: (sessionId: string) => void;
  onOpenHistory: () => void;
  onOpenProfile: () => void;
}

export function TodayScreen({ onOpenDay, onResume, onOpenHistory, onOpenProfile }: TodayScreenProps) {
  const summaries = useProgramSummaries();
  const current = useCurrentSession();

  const active = summaries.data?.find((program) => program.status === "Active" && program.isOwnedByMe);
  const program = useProgram(active?.id ?? "", { enabled: active !== undefined });

  if (summaries.isError) {
    return <ErrorBanner message="Could not load your programs." onRetry={() => void summaries.refetch()} />;
  }

  if (!summaries.isSuccess || !current.isSuccess) return <Spinner label="Loading" />;

  const openSession = current.data;

  return (
    <div className="screen">
      <header className="session__header">
        <h1 className="session__title">MyGym</h1>
      </header>

      {openSession === null ? null : (
        <ul className="list">
          <li className="list__item">
            <button type="button" className="list__button" onClick={() => onResume(openSession.id)}>
              <span>
                <span className="list__primary">Resume workout</span>
                <span className="badge">in progress</span>
                <span className="list__secondary">
                  {openSession.workingSetCount} working set{openSession.workingSetCount === 1 ? "" : "s"} so far
                </span>
              </span>
              <span aria-hidden="true">›</span>
            </button>
          </li>
        </ul>
      )}

      {active === undefined ? (
        <EmptyState title="No active program">
          <p>
            Create a program and activate it, then today&apos;s workout will show up here. Programs
            are set up through the API for now.
          </p>
        </EmptyState>
      ) : !program.isSuccess ? (
        <Spinner label="Loading program" />
      ) : (
        <DayList program={program.data} onOpenDay={onOpenDay} />
      )}

      <nav className="list" aria-label="More">
        <div className="list__item">
          <button type="button" className="list__button" onClick={onOpenHistory}>
            <span className="list__primary">History</span>
            <span aria-hidden="true">›</span>
          </button>
        </div>
        <div className="list__item">
          <button type="button" className="list__button" onClick={onOpenProfile}>
            <span className="list__primary">Profile</span>
            <span aria-hidden="true">›</span>
          </button>
        </div>
      </nav>
    </div>
  );
}

function DayList({
  program,
  onOpenDay,
}: {
  program: NonNullable<ReturnType<typeof useProgram>["data"]>;
  onOpenDay: (programId: string, dayId: string) => void;
}) {
  const { today, rest } = pickTodaysDay(program.days, new Date().getDay());

  const dayButton = (
    day: (typeof program.days)[number],
    isToday: boolean,
  ) => (
    <li className="list__item" key={day.id}>
      <button type="button" className="list__button" onClick={() => onOpenDay(program.id, day.id)}>
        <span>
          <span className="list__primary">{day.label}</span>
          {isToday ? <span className="badge">Today</span> : null}
          <span className="list__secondary">
            {day.prescriptions.length} exercise{day.prescriptions.length === 1 ? "" : "s"}
            {day.dayOfWeek === null ? "" : ` · ${day.dayOfWeek}`}
          </span>
        </span>
        <span aria-hidden="true">›</span>
      </button>
    </li>
  );

  if (program.days.length === 0) {
    return (
      <EmptyState title={program.name}>
        <p>This program has no training days yet.</p>
      </EmptyState>
    );
  }

  return (
    <>
      <h2 className="card__section">{program.name}</h2>
      <ul className="list">
        {today === null ? null : dayButton(today, true)}
        {rest.map((day) => dayButton(day, false))}
      </ul>
    </>
  );
}
