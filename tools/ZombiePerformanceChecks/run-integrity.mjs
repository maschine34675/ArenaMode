// Whole-run evidence gates are separate from percentile arithmetic.
//
// What each gate can prove with the 4096-slot ring (see ring-analysis.mjs):
//   * frameTimeMatchesWall: the recorded frame durations of the analyzed interval add up to the wall time between its
//     first and last poll (3 %). A missing or duplicated stretch of frames, or a ring that is not the newest frames,
//     fails it. It cannot prove that no frame was skipped by Unity itself.
//   * frameCountMatches: on every poll the cursor advanced by as many slots as Time.frameCount advanced (the reader
//     records any difference above its tolerance as an integrity error). One slot per frame is the ring's contract, so
//     this fails when the plugin did not record a frame, when two polls were joined wrongly, or when the cursor
//     arithmetic is not what the source says. The 2026-11-19 stand-in could not have passed it: its "cursor" was the
//     eviction head, and its recorded frames ran 0.5 to 12 % above the Time.frameCount delta.
export const FRAME_TIME_WALL_TOLERANCE = 0.03;

export function runIntegrity({ requestedSeconds, activityStart, activityEnd, firstSample, lastSample, initialStatus, finalStatus, captureErrors = 0,
  frameSeconds, wallSeconds, frameCountMismatches }) {
  const declaredDurationValid = Number.isFinite(requestedSeconds) && requestedSeconds >= 30 && requestedSeconds <= 900;
  const phaseOrderValid = [activityStart, activityEnd, firstSample, lastSample].every(Number.isFinite)
    && activityStart <= firstSample && firstSample <= lastSample && lastSample <= activityEnd;
  const durationSatisfied = declaredDurationValid && phaseOrderValid && activityEnd-activityStart >= requestedSeconds*1000-250;
  const measurementCoversActivity = phaseOrderValid && firstSample-activityStart <= 1500 && activityEnd-lastSample <= 1500;
  const sameProcessAndMap = !!initialStatus?.bridge?.instanceId && !!finalStatus
    && initialStatus.bridge.instanceId === finalStatus.bridge?.instanceId
    && !!initialStatus.raid?.location && initialStatus.raid.location === finalStatus.raid?.location;
  const frameTimeMatchesWall = Number.isFinite(frameSeconds) && Number.isFinite(wallSeconds) && wallSeconds > 0
    && Math.abs(frameSeconds - wallSeconds) / wallSeconds <= FRAME_TIME_WALL_TOLERANCE;
  const frameCountMatches = Number.isSafeInteger(frameCountMismatches) && frameCountMismatches === 0;
  return { requestedSeconds: declaredDurationValid ? requestedSeconds : null, declaredDurationValid,
    phaseOrderValid, durationSatisfied, measurementCoversActivity, sameProcessAndMap, frameTimeMatchesWall, frameCountMatches,
    passed: durationSatisfied && measurementCoversActivity && sameProcessAndMap && frameTimeMatchesWall && frameCountMatches && captureErrors === 0 };
}
