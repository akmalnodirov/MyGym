import { expect, test } from "vitest";
import { messageForCode, recoveryForCode } from "./codes";

test("maps the codes that drive behaviour rather than only a message", () => {
  expect(recoveryForCode("session.already_in_progress")).toBe("resumeCurrentSession");
  expect(recoveryForCode("session.closed")).toBe("refetchReadOnly");
  expect(recoveryForCode("auth.refresh_token_invalid")).toBe("reauthenticate");
  expect(recoveryForCode("auth.init_data_expired")).toBe("reauthenticate");
  expect(recoveryForCode("session.not_found")).toBe("goHome");
  expect(recoveryForCode("program.not_found")).toBe("goHome");
  expect(recoveryForCode("program.day_not_found")).toBe("goHome");
  expect(recoveryForCode("session.needs_at_least_one_working_set")).toBe("none");
});

test("degrades for a code it has never seen rather than throwing", () => {
  // The backend will gain codes this client has never heard of. An unmapped code must produce a
  // usable screen, not a crash and not a blank message.
  expect(recoveryForCode("brand.new.code.from.the.future")).toBe("none");
  expect(messageForCode("brand.new.code.from.the.future")).toMatch(/try again/i);
  expect(messageForCode(null)).toMatch(/try again/i);
  expect(messageForCode("")).toMatch(/try again/i);
});

test("gives a specific, non-generic message to the codes an athlete will actually hit", () => {
  const generic = messageForCode("a.code.that.is.definitely.not.mapped");

  for (const code of [
    "session.already_in_progress",
    "session.closed",
    "session.needs_at_least_one_working_set",
    "session.needs_at_least_one_exercise",
    "set.reps_out_of_bounds",
    "set.rir_out_of_bounds",
    "session.not_found",
    "program.not_found",
    "profile.time_zone_unknown",
    "profile.weight_unit_invalid",
  ]) {
    expect(messageForCode(code), code).not.toBe(generic);
    expect(messageForCode(code).length, code).toBeGreaterThan(0);
  }
});

test("every mapped message is prose, not the error code echoed back", () => {
  // Guards against a code being mapped to its own identifier, which reads as a crash to a user.
  const looksLikeACode = /^[a-z][a-z_]*(\.[a-z_]+)+$/;
  for (const code of [
    "session.closed",
    "session.already_in_progress",
    "set.reps_out_of_bounds",
    "program.not_found",
  ]) {
    expect(messageForCode(code), code).not.toMatch(looksLikeACode);
    expect(messageForCode(code), code).toMatch(/\s/);
  }
});
