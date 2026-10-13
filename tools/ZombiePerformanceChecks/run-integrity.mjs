// Whole-run evidence gates are separate from percentile arithmetic.
export function runIntegrity({ requestedSeconds, activityStart, activityEnd, firstSample, lastSample, initialStatus, finalStatus, captureErrors = 0 }) {
  const declaredDurationValid = Number.isFinite(requestedSeconds) && requestedSeconds >= 30 && requestedSeconds <= 900;
  const phaseOrderValid = [activityStart, activityEnd, firstSample, lastSample].every(Number.isFinite)
    && activityStart <= firstSample && firstSample <= lastSample && lastSample <= activityEnd;
  const durationSatisfied = declaredDurationValid && phaseOrderValid && activityEnd-activityStart >= requestedSeconds*1000-250;
  const measurementCoversActivity = phaseOrderValid && firstSample-activityStart <= 1500 && activityEnd-lastSample <= 1500;
  const sameProcessAndMap = !!initialStatus?.bridge?.instanceId && !!finalStatus
    && initialStatus.bridge.instanceId === finalStatus.bridge?.instanceId
    && !!initialStatus.raid?.location && initialStatus.raid.location === finalStatus.raid?.location;
  return { requestedSeconds: declaredDurationValid ? requestedSeconds : null, declaredDurationValid,
    phaseOrderValid, durationSatisfied, measurementCoversActivity, sameProcessAndMap,
    passed: durationSatisfied && measurementCoversActivity && sameProcessAndMap && captureErrors === 0 };
}
