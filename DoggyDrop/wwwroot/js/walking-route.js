(() => {
    "use strict";
    const valid = point => Array.isArray(point) && point.length === 2 &&
        point.every(Number.isFinite) && Math.abs(point[0]) <= 90 && Math.abs(point[1]) <= 180;
    async function request(origin, destination, token, signal) {
        if (!valid([origin.lat, origin.lng]) || !valid([destination.lat, destination.lng])) return null;
        try {
            const response = await fetch("/api/walking-route", {
                method: "POST", credentials: "same-origin", cache: "no-store", signal,
                headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
                body: JSON.stringify({ origin: { latitude: origin.lat, longitude: origin.lng },
                    destination: { latitude: destination.lat, longitude: destination.lng } })
            });
            if (!response.ok) return null;
            const route = await response.json();
            if (!Array.isArray(route.points) || route.points.length < 2 || !route.points.every(valid)
                || !Number.isFinite(route.distanceMeters) || route.distanceMeters <= 0) return null;
            return { points: route.points, distanceMeters: route.distanceMeters,
                durationSeconds: Number.isFinite(route.durationSeconds) && route.durationSeconds > 0 ? route.durationSeconds : null,
                isFallback: false };
        } catch (error) {
            if (signal?.aborted || error?.name === "AbortError") throw error;
            return null;
        }
    }
    window.DoggyDropWalkingRoute = { request };
})();
