import { env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import { handleToken } from "../src/issue";
import { compareVersions, normalizeKey } from "../src/util";
import { ADMIN, call, createLicense, details, errorOf, machineId, requestToken, tokenPayload } from "./helpers";

const tokenRequest = (body: Record<string, unknown>) =>
  new Request("https://license.test/v1/token", { method: "POST", body: JSON.stringify({ appVersion: "1.0.0.0", ...body }) });

describe("POST /v1/token", () => {
  it("issues a signed token the app accepts", async () => {
    const { id, key } = await createLicense({ customer: "Tester" });
    const machine = machineId();
    const before = Date.now();

    const payload = await tokenPayload(await requestToken(key, machine));

    expect(payload).toMatchObject({ licenseId: id, machine, accounts: [], maxAccounts: 1, customer: "Tester", plan: "standard", minAppVersion: null });
    expect(Date.parse(payload.issuedAt)).toBeGreaterThanOrEqual(before - 1000);
    expect(Date.parse(payload.expiresAt) - Date.parse(payload.issuedAt)).toBe(72 * 3_600_000);
    expect((await details(id)).machines.map((m) => m.machine)).toEqual([machine]);
  });

  it("refuses an unknown key and malformed requests", async () => {
    const unknown = await requestToken("OGS-AAAAA-BBBBB-CCCCC-DDDDD", machineId());
    expect(unknown.status).toBe(403);
    expect(await errorOf(unknown)).toMatch(/Unknown license key/);

    const { key } = await createLicense();
    expect((await requestToken(key, "not-a-machine")).status).toBe(400);
    expect((await requestToken(key, machineId(), { appVersion: "x" })).status).toBe(400);
    expect((await requestToken("", machineId())).status).toBe(400);
    expect((await call("/v1/token", { method: "POST", body: "{" })).status).toBe(400);
  });

  it("accepts the key as typed: lower case, spaces, O for 0 and I/L for 1", async () => {
    const { key } = await createLicense();
    const typed = " " + key.toLowerCase().replace(/0/g, "o").replace(/1/g, "l") + " ";
    expect(normalizeKey(typed)).toBe(key);
    expect((await requestToken(typed, machineId())).status).toBe(200);
  });

  it("allows only max_machines PCs, and frees one unused for the release days", async () => {
    const { id, key } = await createLicense();
    const first = machineId();
    const second = machineId();
    const now = new Date();
    expect((await handleToken(tokenRequest({ licenseKey: key, machine: first }), env, now)).status).toBe(200);

    const refused = await handleToken(tokenRequest({ licenseKey: key, machine: second }), env, now);
    expect(refused.status).toBe(403);
    expect(await errorOf(refused)).toMatch(/already active on 1 PC/);

    // The first PC can still renew.
    expect((await handleToken(tokenRequest({ licenseKey: key, machine: first }), env, now)).status).toBe(200);

    const later = new Date(now.getTime() + 15 * 86_400_000);
    expect((await handleToken(tokenRequest({ licenseKey: key, machine: second }), env, later)).status).toBe(200);
    const after = await details(id);
    expect(after.machines.map((m) => m.machine)).toEqual([second]);
    expect(after.events.map((e) => e.kind)).toContain("machine_released");
  });

  it("binds game accounts up to max_accounts", async () => {
    const { id, key } = await createLicense({ maxAccounts: 1 });
    const machine = machineId();

    const bound = await tokenPayload(await requestToken(key, machine, { account: { universe: "Genesis", playerId: "AB12-CD" } }));
    expect(bound.accounts).toEqual([{ universe: "genesis", playerId: "ab12-cd" }]);

    // The bound account again, and a renewal without one, keep it.
    expect((await tokenPayload(await requestToken(key, machine, { account: { universe: "genesis", playerId: "ab12-cd" } }))).accounts).toHaveLength(1);
    expect((await tokenPayload(await requestToken(key, machine))).accounts).toHaveLength(1);

    const second = await requestToken(key, machine, { account: { universe: "genesis", playerId: "other" } });
    expect(second.status).toBe(403);
    expect(await errorOf(second)).toMatch(/All 1 game account/);
    expect((await details(id)).accounts).toHaveLength(1);
  });

  it("binds nothing for a license covering any account", async () => {
    const { key } = await createLicense({ maxAccounts: 0 });
    const payload = await tokenPayload(await requestToken(key, machineId(), { account: { universe: "genesis", playerId: "p1" } }));
    expect(payload).toMatchObject({ maxAccounts: 0, accounts: [] });
  });

  it("refuses revoked, cancelled and expired licenses, and ends tokens with the license", async () => {
    const revoked = await createLicense({ status: "revoked" });
    expect(await errorOf(await requestToken(revoked.key, machineId()))).toMatch(/revoked/);

    const cancelled = await createLicense({ status: "cancelled" });
    expect(await errorOf(await requestToken(cancelled.key, machineId()))).toMatch(/ended/);

    const expired = await createLicense({ validUntil: "2020-01-01T00:00:00Z" });
    expect(await errorOf(await requestToken(expired.key, machineId()))).toMatch(/expired on 2020-01-01/);

    const ending = new Date(Date.now() + 3_600_000).toISOString();
    const soon = await createLicense({ validUntil: ending });
    expect((await tokenPayload(await requestToken(soon.key, machineId()))).expiresAt).toBe(ending);

    const events = (await details(revoked.id)).events;
    expect(events[0]).toMatchObject({ kind: "refused" });
  });

  it("refuses builds below MIN_APP_VERSION and names it in the token", async () => {
    const { key } = await createLicense();
    const machine = machineId();
    const old = await requestToken(key, machine, {}, { MIN_APP_VERSION: "1.2" });
    expect(old.status).toBe(426);
    expect(await errorOf(old)).toMatch(/Update to 1\.2 or later/);

    const payload = await tokenPayload(await requestToken(key, machine, { appVersion: "1.2.0.0" }, { MIN_APP_VERSION: "1.2" }));
    expect(payload.minAppVersion).toBe("1.2");
  });
});

describe("compareVersions", () => {
  it("compares numerically with missing parts as 0", () => {
    expect(compareVersions("1.2", "1.2.0.0")).toBe(0);
    expect(compareVersions("1.10", "1.9")).toBe(1);
    expect(compareVersions("1.0.0.0", "1.0.1")).toBe(-1);
  });
});

describe("/admin", () => {
  it("needs the admin token", async () => {
    expect((await call("/admin/licenses")).status).toBe(401);
    expect((await call("/admin/licenses", { headers: { authorization: "Bearer wrong" } })).status).toBe(401);
    expect((await call("/admin/licenses", { headers: ADMIN })).status).toBe(200);
  });

  it("shows the public key of the signing key", async () => {
    const body = (await (await call("/admin/public-key", { headers: ADMIN })).json()) as { publicKey: string };
    expect(body.publicKey).toBe(env.TEST_PUBLIC_KEY);
  });

  it("replaces a key: the old one stops working", async () => {
    const { id, key } = await createLicense();
    const response = await call(`/admin/licenses/${id}/key`, { method: "POST", headers: ADMIN });
    const { key: newKey } = (await response.json()) as { key: string };

    expect((await requestToken(key, machineId())).status).toBe(403);
    expect((await requestToken(newKey, machineId())).status).toBe(200);
  });

  it("frees a machine and unbinds an account", async () => {
    const { id, key } = await createLicense();
    const machine = machineId();
    await requestToken(key, machine, { account: { universe: "genesis", playerId: "p1" } });

    expect((await call(`/admin/licenses/${id}/machines/${machine.toLowerCase()}`, { method: "DELETE", headers: ADMIN })).status).toBe(204);
    expect((await call(`/admin/licenses/${id}/accounts/genesis/p1`, { method: "DELETE", headers: ADMIN })).status).toBe(204);
    expect((await call(`/admin/licenses/${id}/accounts/genesis/p1`, { method: "DELETE", headers: ADMIN })).status).toBe(404);
    const after = await details(id);
    expect(after.machines).toEqual([]);
    expect(after.accounts).toEqual([]);

    expect((await requestToken(key, machineId())).status).toBe(200);
  });

  it("updates status, limits, plan and validity", async () => {
    const { id } = await createLicense();
    const patch = (body: unknown) =>
      call(`/admin/licenses/${id}`, { method: "PATCH", headers: { ...ADMIN, "content-type": "application/json" }, body: JSON.stringify(body) });

    let license = ((await (await patch({ plan: "pro" })).json()) as { license: Record<string, unknown> }).license;
    expect(license).toMatchObject({ plan: "pro", max_machines: 2, max_accounts: 3 });

    license = ((await (await patch({ status: "revoked", maxMachines: 5, validUntil: null, notes: "chargeback" })).json()) as { license: Record<string, unknown> })
      .license;
    expect(license).toMatchObject({ status: "revoked", max_machines: 5, valid_until: null, notes: "chargeback" });
    expect(license).not.toHaveProperty("key_hash");

    expect((await patch({ status: "nope" })).status).toBe(400);
    expect((await patch({ maxAccounts: -1 })).status).toBe(400);
    expect((await patch({ plan: "gold" })).status).toBe(400);
  });

  it("lists and finds licenses", async () => {
    const { id } = await createLicense({ email: "findme@example.com" });
    const body = (await (await call("/admin/licenses?q=findme", { headers: ADMIN })).json()) as { licenses: { id: string }[] };
    expect(body.licenses.map((l) => l.id)).toEqual([id]);
  });
});
