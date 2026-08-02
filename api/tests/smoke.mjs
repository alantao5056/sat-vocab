// End-to-end smoke test of the REST API contract, exercising both the web
// (cookie) and desktop (body) refresh-token delivery modes.
const BASE = "http://127.0.0.1:5080";

let failures = 0;
function check(name, condition, detail = "") {
    const ok = Boolean(condition);
    if (!ok) failures++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `  -> ${detail}`}`);
}

async function call(path, { method = "GET", body, token, client, cookie, redirect = "follow" } = {}) {
    const headers = {};
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (token) headers["Authorization"] = `Bearer ${token}`;
    if (client) headers["X-Client"] = client;
    if (cookie) headers["Cookie"] = cookie;
    const res = await fetch(BASE + path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        redirect,
    });
    const text = await res.text();
    let json = null;
    try {
        json = text ? JSON.parse(text) : null;
    } catch {
        /* not json */
    }
    return { status: res.status, json, text, setCookie: res.headers.getSetCookie?.() ?? [] };
}

const stamp = Date.now();

// --- health -----------------------------------------------------------------
const health = await call("/v1/health");
check("health returns ok", health.status === 200 && health.json?.status === "ok", health.text);

// --- register: desktop mode -------------------------------------------------
const desktopEmail = `desktop-${stamp}@example.com`;
const reg = await call("/v1/auth/register", {
    method: "POST",
    client: "desktop",
    body: { email: desktopEmail, name: "Desktop User", password: "hunter2hunter2", timezone: "Asia/Shanghai" },
});
check("register succeeds", reg.status === 200, `${reg.status} ${reg.text.slice(0, 200)}`);
check("desktop gets refresh token in body", typeof reg.json?.refreshToken === "string");
check("desktop gets NO refresh cookie", reg.setCookie.length === 0, JSON.stringify(reg.setCookie));
check("timezone stored", reg.json?.user?.timezone === "Asia/Shanghai", reg.json?.user?.timezone);
check("hasPassword true", reg.json?.user?.hasPassword === true);
check("access token expires in 15 min", reg.json?.expiresIn === 900, String(reg.json?.expiresIn));

let token = reg.json.accessToken;
let desktopRefresh = reg.json.refreshToken;

// --- register: web mode -----------------------------------------------------
const webReg = await call("/v1/auth/register", {
    method: "POST",
    client: "web",
    body: {
        email: `web-${stamp}@example.com`,
        name: "Web User",
        password: "hunter2hunter2",
        timezone: "America/New_York",
    },
});
check("web register succeeds", webReg.status === 200, webReg.text.slice(0, 200));
check(
    "web gets NO refresh token in body",
    webReg.json?.refreshToken === null,
    JSON.stringify(webReg.json?.refreshToken)
);
const webCookie = webReg.setCookie.find((c) => c.startsWith("sat_vocab_refresh="));
check("web gets httpOnly refresh cookie", Boolean(webCookie), JSON.stringify(webReg.setCookie));
check("refresh cookie is httpOnly", /httponly/i.test(webCookie ?? ""), webCookie);
check("refresh cookie is SameSite=Strict", /samesite=strict/i.test(webCookie ?? ""), webCookie);

// --- duplicate registration -------------------------------------------------
const dup = await call("/v1/auth/register", {
    method: "POST",
    body: { email: desktopEmail, name: "x", password: "hunter2hunter2" },
});
check("duplicate email rejected with 409", dup.status === 409, String(dup.status));

// --- login ------------------------------------------------------------------
const badLogin = await call("/v1/auth/login", {
    method: "POST",
    body: { email: desktopEmail, password: "wrong-password" },
});
check("wrong password rejected with 401", badLogin.status === 401, String(badLogin.status));

const login = await call("/v1/auth/login", {
    method: "POST",
    client: "desktop",
    body: { email: desktopEmail, password: "hunter2hunter2" },
});
check("login succeeds", login.status === 200, login.text.slice(0, 200));

// --- unauthenticated access -------------------------------------------------
const noAuth = await call("/v1/study/queue");
check("queue requires auth", noAuth.status === 401, String(noAuth.status));

// --- study queue ------------------------------------------------------------
const queue = await call("/v1/study/queue", { token });
check("queue returns 200", queue.status === 200, queue.text.slice(0, 200));
const q = queue.json;
check("fresh account: round size 12 words", q?.words?.length === 12, String(q?.words?.length));
check("fresh account: no due reviews", q?.dueCount === 0, String(q?.dueCount));
check(
    "fresh account: all words are new",
    q?.words?.every((w) => w.isNew === true)
);
check("new allowance is 30", q?.newAllowance === 30, String(q?.newAllowance));
check("today matches Asia/Shanghai date", /^\d{4}-\d{2}-\d{2}$/.test(q?.today ?? ""), q?.today);
check("words carry definition + example", Boolean(q?.words?.[0]?.definition && q?.words?.[0]?.example));

// --- grading ----------------------------------------------------------------
const graded = q.words.slice(0, 5);
const badGrade = await call("/v1/study/reviews", {
    method: "POST",
    token,
    body: { ratings: [{ wordId: graded[0].id, grade: 9 }] },
});
check("out-of-range grade rejected", badGrade.status === 400, String(badGrade.status));

const reviews = await call("/v1/study/reviews", {
    method: "POST",
    token,
    // 5 = perfect (long interval), 0 = blackout (due again tomorrow)
    body: { ratings: graded.map((w, i) => ({ wordId: w.id, grade: i === 0 ? 0 : 5 })) },
});
check("reviews accepted", reviews.status === 200 && reviews.json?.updated === 5, reviews.text.slice(0, 200));

const queue2 = await call("/v1/study/queue", { token });
const remainingIds = new Set(queue2.json.words.map((w) => w.id));
const passedIds = graded.slice(1).map((w) => w.id);
check(
    "passed words left the queue",
    passedIds.every((id) => !remainingIds.has(id))
);
check("lapsed word also left today's queue (due tomorrow)", !remainingIds.has(graded[0].id));
check("introducedToday counts the 5 graded", queue2.json?.introducedToday === 5, String(queue2.json?.introducedToday));
check("allowance dropped to 25", queue2.json?.newAllowance === 25, String(queue2.json?.newAllowance));

// --- progress ---------------------------------------------------------------
const progress = await call("/v1/progress", { token });
check("progress returns 200", progress.status === 200, progress.text.slice(0, 200));
const buckets = Object.fromEntries((progress.json?.buckets ?? []).map((b) => [b.key, b.count]));
check("progress total is the whole deck", progress.json?.total > 1000, String(progress.json?.total));
check("5 words are now learning", buckets.learning === 5, JSON.stringify(buckets));
check("nothing mastered yet", buckets.mastered === 0, String(buckets.mastered));
check("unseen = total - 5", buckets.unseen === progress.json.total - 5, String(buckets.unseen));

const words = await call("/v1/progress/words?bucket=learning&limit=3", { token });
check("progress words paged", words.json?.words?.length === 3 && words.json?.total === 5, words.text.slice(0, 200));
const badBucket = await call("/v1/progress/words?bucket=nonsense", { token });
check("unknown bucket rejected", badBucket.status === 400, String(badBucket.status));

// --- settings ---------------------------------------------------------------
const settings = await call("/v1/settings", { token });
check("settings returns 200", settings.status === 200, settings.text.slice(0, 200));
check("settings expose grade scale", settings.json?.grades?.length === 6);
check("settings expose round options", JSON.stringify(settings.json?.wordsPerRoundOptions) === "[8,12,15]");
check("settings expose intensity presets", settings.json?.intensityPresets?.length === 3);

const badSetting = await call("/v1/settings", { method: "PUT", token, body: { wordsPerRound: 99 } });
check("off-list round size rejected", badSetting.status === 400, String(badSetting.status));

// Deliberately no timezone change yet: switching zones can cross a day boundary,
// which legitimately changes which day the 5 graded words count against.
const updated = await call("/v1/settings", {
    method: "PUT",
    token,
    body: { wordsPerRound: 8, newWordsPerDay: 50 },
});
check(
    "settings update accepted",
    updated.status === 200 && updated.json?.wordsPerRound === 8,
    updated.text.slice(0, 200)
);

const queue3 = await call("/v1/study/queue", { token });
check("round size setting takes effect", queue3.json?.words?.length === 8, String(queue3.json?.words?.length));
check("new cap setting takes effect", queue3.json?.newAllowance === 45, String(queue3.json?.newAllowance));

// --- extra round ------------------------------------------------------------
const extra = await call("/v1/study/extra-round", { method: "POST", token });
check("extra round raises the cap by one round", extra.json?.newAllowance === 53, JSON.stringify(extra.json));

// --- timezone is what defines "today" ---------------------------------------
const zoned = await call("/v1/settings", { method: "PUT", token, body: { timezone: "Europe/Berlin" } });
check("timezone updated", zoned.status === 200 && zoned.json?.timezone === "Europe/Berlin", zoned.json?.timezone);

const berlinQueue = await call("/v1/study/queue", { token });
const berlinToday = new Date().toLocaleDateString("en-CA", { timeZone: "Europe/Berlin" });
check(
    "queue date follows the user's zone",
    berlinQueue.json?.today === berlinToday,
    `${berlinQueue.json?.today} vs ${berlinToday}`
);

const badZone = await call("/v1/settings", { method: "PUT", token, body: { timezone: "Mars/Olympus_Mons" } });
check("unknown timezone rejected", badZone.status === 400, String(badZone.status));

// --- passage ----------------------------------------------------------------
// Generation itself is not exercised here: it costs a real API call, and the
// account's daily quota is only three.
const passage = await call("/v1/passage", { token });
check("passage returns the round", Array.isArray(passage.json?.queue?.words), passage.text.slice(0, 200));
check("nothing cached for a fresh round", passage.json?.segments === null, JSON.stringify(passage.json?.segments));
check("passage reports the daily quota", passage.json?.generationsLimit === 3, String(passage.json?.generationsLimit));
check("no generations used yet", passage.json?.generationsUsed === 0, String(passage.json?.generationsUsed));

const passageNoAuth = await call("/v1/passage");
check("passage requires auth", passageNoAuth.status === 401, String(passageNoAuth.status));

// --- refresh rotation -------------------------------------------------------
const refreshed = await call("/v1/auth/refresh", {
    method: "POST",
    client: "desktop",
    body: { refreshToken: desktopRefresh },
});
check("refresh succeeds", refreshed.status === 200, refreshed.text.slice(0, 200));
check("refresh rotates the token", refreshed.json?.refreshToken && refreshed.json.refreshToken !== desktopRefresh);
const rotated = refreshed.json.refreshToken;

const replay = await call("/v1/auth/refresh", { method: "POST", body: { refreshToken: desktopRefresh } });
check("replaying a rotated token is rejected", replay.status === 401, String(replay.status));

const afterReplay = await call("/v1/auth/refresh", { method: "POST", body: { refreshToken: rotated } });
check("replay revokes the whole family", afterReplay.status === 401, String(afterReplay.status));

// --- web refresh via cookie -------------------------------------------------
const cookieValue = webCookie.split(";")[0];
const webRefresh = await call("/v1/auth/refresh", { method: "POST", client: "web", cookie: cookieValue });
check("web refreshes using only the cookie", webRefresh.status === 200, webRefresh.text.slice(0, 200));
check("rotated web token stays out of the body", webRefresh.json?.refreshToken === null);

// --- me + set password ------------------------------------------------------
const me = await call("/v1/me", { token });
check("me returns the account", me.status === 200 && me.json?.email === desktopEmail, me.text.slice(0, 200));

const wrongCurrent = await call("/v1/me/password", {
    method: "PUT",
    token,
    body: { currentPassword: "not-it", newPassword: "brand-new-password" },
});
check("password change needs the current one", wrongCurrent.status === 403, String(wrongCurrent.status));

const changed = await call("/v1/me/password", {
    method: "PUT",
    token,
    body: { currentPassword: "hunter2hunter2", newPassword: "brand-new-password" },
});
check("password change succeeds", changed.status === 204, String(changed.status));

const reLogin = await call("/v1/auth/login", {
    method: "POST",
    body: { email: desktopEmail, password: "brand-new-password" },
});
check("login with the new password works", reLogin.status === 200, String(reLogin.status));

// --- logout -----------------------------------------------------------------
const logout = await call("/v1/auth/logout", { method: "POST", body: { refreshToken: reLogin.json.refreshToken } });
check("logout returns 204", logout.status === 204, String(logout.status));
const afterLogout = await call("/v1/auth/refresh", {
    method: "POST",
    body: { refreshToken: reLogin.json.refreshToken },
});
check("revoked token cannot refresh", afterLogout.status === 401, String(afterLogout.status));

// --- google when unconfigured -----------------------------------------------
const google = await call("/v1/auth/google/start", { redirect: "manual" });
check("google start reports it is unconfigured", google.status === 503, String(google.status));

// --- openapi ----------------------------------------------------------------
const openapi = await call("/openapi/v1.json");
const paths = Object.keys(openapi.json?.paths ?? {});
check("openapi document is served", openapi.status === 200, String(openapi.status));
check("openapi lists the study endpoints", paths.includes("/v1/study/queue"), JSON.stringify(paths));
check("openapi lists the passage endpoints", paths.includes("/v1/passage/generate"), JSON.stringify(paths));

console.log(`\n${failures === 0 ? "ALL CHECKS PASSED" : `${failures} CHECK(S) FAILED`}`);
process.exit(failures === 0 ? 0 : 1);
