(() => {
    const loginPath = /^\/Identity\/Account\/Login(?:\/|$)/i;

    function pointsToLogin(url) {
        if (!url) return false;
        try {
            return loginPath.test(new URL(url, "https://doggydrop.invalid").pathname);
        } catch {
            return false;
        }
    }

    async function isAuthResponse(response) {
        if (response.status === 401 || response.status === 403) return true;
        if (pointsToLogin(response.url) || (response.status === 302 && pointsToLogin(response.headers.get("location")))) return true;
        if (!(response.headers.get("content-type") || "").toLowerCase().includes("text/html")) return false;
        try {
            const html = await response.clone().text();
            return /<form\b[^>]*\bclass=["'][^"']*\bauth-form\b/i.test(html) &&
                /\bname=["']Input\.(?:Email|Password)["']/i.test(html);
        } catch {
            return false;
        }
    }

    function createGuard(walkId, onAuthLost) {
        if (!Number.isSafeInteger(walkId) || walkId <= 0) throw new Error("Invalid walk ID");
        let suspended = false;
        return {
            canRecord() { return !suspended; },
            loginUrl: `/Identity/Account/Login?returnUrl=${encodeURIComponent(`/Walks/Active/${walkId}`)}`,
            async check(response) {
                if (suspended) return true;
                if (!await isAuthResponse(response)) return false;
                suspended = true;
                onAuthLost();
                return true;
            }
        };
    }

    const api = { createGuard, isAuthResponse };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    if (typeof window !== "undefined") window.DoggyDropWalkRecordingAuth = api;
})();
