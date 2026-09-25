(() => {
    const radiusMeters = 120;
    const plannedRadiusMeters = 85;
    const maxAccuracyMeters = 50;
    const cooldownMs = 15000;

    function createTracker(walkId, savedState = null, now = Date.now()) {
        const sameWalk = savedState && savedState.walkId === walkId;
        const previousIds = sameWalk && Array.isArray(savedState.alertedBinIds) ? savedState.alertedBinIds : [];
        const alerted = new Set(previousIds.map(Number).filter(id => Number.isInteger(id) && id > 0));
        const previousStopIds = sameWalk && Array.isArray(savedState.alertedPlannedStopIds) ? savedState.alertedPlannedStopIds : [];
        const alertedPlannedStops = new Set(previousStopIds.map(Number).filter(id => Number.isInteger(id) && id > 0));
        const savedTime = sameWalk ? savedState.lastAlertAt : null;
        let lastAlertAt = Number.isFinite(savedTime) && savedTime > 0
            ? Math.min(savedTime, now)
            : sameWalk && (alerted.size > 0 || alertedPlannedStops.size > 0) ? now : -Infinity;

        return {
            check(candidates, accuracyMeters, now = Date.now(), plannedCandidates = []) {
                if (!Number.isFinite(accuracyMeters) || accuracyMeters > maxAccuracyMeters || accuracyMeters < 0 || now - lastAlertAt < cooldownMs) return null;
                const planned = plannedCandidates
                    .filter(item => Number.isInteger(item.id) && item.id > 0 && !alertedPlannedStops.has(item.id)
                        && Number.isFinite(item.distanceMeters) && item.distanceMeters >= 0 && item.distanceMeters <= plannedRadiusMeters)
                    .sort((a, b) => a.distanceMeters - b.distanceMeters)[0];
                const nearest = candidates
                    .filter(item => Number.isInteger(item.id) && item.id > 0 && !alerted.has(item.id)
                        && Number.isFinite(item.distanceMeters) && item.distanceMeters >= 0 && item.distanceMeters <= radiusMeters)
                    .sort((a, b) => a.distanceMeters - b.distanceMeters)[0];
                if (!planned && !nearest) return null;
                if (planned) alertedPlannedStops.add(planned.id);
                else alerted.add(nearest.id);
                lastAlertAt = now;
                return planned ? { ...planned, source: "planned" } : { ...nearest, source: "public" };
            },
            alertedIds() { return [...alerted]; },
            state() { return { walkId, alertedBinIds: [...alerted], alertedPlannedStopIds: [...alertedPlannedStops], lastAlertAt: Number.isFinite(lastAlertAt) ? lastAlertAt : null }; }
        };
    }

    const api = { createTracker, radiusMeters, plannedRadiusMeters, maxAccuracyMeters, cooldownMs };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    if (typeof window !== "undefined") window.DoggyDropWalkBinProximity = api;
})();
