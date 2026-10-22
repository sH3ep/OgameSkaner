import { env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import type { Env } from "../src/env";
import { ADMIN, call, details } from "./helpers";

const SECRET = "pdl_ntfset_test_secret";
const PRICE = "pri_test_pro";
const plans: Partial<Env> = {
  PLANS: { standard: { maxMachines: 1, maxAccounts: 1 }, pro: { maxMachines: 2, maxAccounts: 3, paddlePriceIds: [PRICE] } },
};

async function signature(body: string, secret = SECRET, ts = Math.floor(Date.now() / 1000)): Promise<string> {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(`${ts}:${body}`)));
  return `ts=${ts};h1=${Array.from(mac, (b) => b.toString(16).padStart(2, "0")).join("")}`;
}

let counter = 0;

function subscriptionEvent(subscriptionId: string, type: string, status: string, occurredAt: string, periodEnd: string | null, price = PRICE) {
  return {
    event_id: `evt_${++counter}_${crypto.randomUUID()}`,
    event_type: type,
    occurred_at: occurredAt,
    notification_id: "ntf_1",
    data: {
      id: subscriptionId,
      status,
      customer_id: "ctm_1",
      items: [{ price: { id: price }, quantity: 1 }],
      current_billing_period: periodEnd ? { starts_at: occurredAt, ends_at: periodEnd } : null,
    },
  };
}

async function deliver(event: unknown, sign: (body: string) => Promise<string> = (b) => signature(b)) {
  const body = JSON.stringify(event);
  return call("/webhooks/paddle", { method: "POST", headers: { "paddle-signature": await sign(body) }, body }, plans);
}

async function licenseOf(subscriptionId: string) {
  const list = (await (await call(`/admin/licenses?q=${subscriptionId}`, { headers: ADMIN })).json()) as { licenses: { id: string }[] };
  expect(list.licenses).toHaveLength(1);
  return details(list.licenses[0].id);
}

describe("POST /webhooks/paddle", () => {
  it("creates a license for a new subscription with the price's plan", async () => {
    const sub = `sub_${crypto.randomUUID()}`;
    const response = await deliver(subscriptionEvent(sub, "subscription.created", "active", "2026-10-01T10:00:00Z", "2026-11-01T10:00:00Z"));
    expect(response.status).toBe(200);

    const { license, events } = await licenseOf(sub);
    expect(license).toMatchObject({ status: "active", plan: "pro", max_machines: 2, max_accounts: 3, valid_until: "2026-11-04T10:00:00.000Z" });
    // No PADDLE_API_KEY / mail in the tests: the key is logged as not mailed, to be handed out with a new key.
    expect(events.map((e) => e.kind)).toEqual(expect.arrayContaining(["created", "key_not_mailed"]));
  });

  it("applies a delivery once and skips older events", async () => {
    const sub = `sub_${crypto.randomUUID()}`;
    const renewed = subscriptionEvent(sub, "subscription.updated", "active", "2026-11-01T10:00:00Z", "2026-12-01T10:00:00Z");
    await deliver(subscriptionEvent(sub, "subscription.created", "active", "2026-10-01T10:00:00Z", "2026-11-01T10:00:00Z"));
    await deliver(renewed);

    expect(await (await deliver(renewed)).json()).toMatchObject({ duplicate: renewed.event_id });
    await deliver(subscriptionEvent(sub, "subscription.activated", "active", "2026-10-01T10:00:05Z", "2026-11-01T10:00:00Z"));

    const { license, events } = await licenseOf(sub);
    expect(license.valid_until).toBe("2026-12-04T10:00:00.000Z");
    expect(events.map((e) => e.kind)).toContain("paddle_skipped");
  });

  it("limits a past-due subscription to the grace days and ends a cancelled one", async () => {
    const sub = `sub_${crypto.randomUUID()}`;
    await deliver(subscriptionEvent(sub, "subscription.created", "active", "2026-10-01T10:00:00Z", "2026-11-01T10:00:00Z"));
    await deliver(subscriptionEvent(sub, "subscription.past_due", "past_due", "2026-11-01T11:00:00Z", "2026-12-01T10:00:00Z"));
    expect((await licenseOf(sub)).license).toMatchObject({ status: "past_due", valid_until: "2026-11-04T11:00:00.000Z" });

    await deliver(subscriptionEvent(sub, "subscription.canceled", "canceled", "2026-11-05T00:00:00Z", null));
    expect((await licenseOf(sub)).license).toMatchObject({ status: "cancelled" });
  });

  it("keeps a license revoked by hand revoked", async () => {
    const sub = `sub_${crypto.randomUUID()}`;
    await deliver(subscriptionEvent(sub, "subscription.created", "active", "2026-10-01T10:00:00Z", "2026-11-01T10:00:00Z"));
    const { license } = await licenseOf(sub);
    await call(`/admin/licenses/${license.id}`, {
      method: "PATCH",
      headers: { ...ADMIN, "content-type": "application/json" },
      body: JSON.stringify({ status: "revoked" }),
    });

    await deliver(subscriptionEvent(sub, "subscription.updated", "active", "2026-11-01T10:00:00Z", "2026-12-01T10:00:00Z"));
    expect((await licenseOf(sub)).license).toMatchObject({ status: "revoked", valid_until: "2026-12-04T10:00:00.000Z" });
  });

  it("uses the default plan for an unknown price and says so", async () => {
    const sub = `sub_${crypto.randomUUID()}`;
    await deliver(subscriptionEvent(sub, "subscription.created", "active", "2026-10-01T10:00:00Z", "2026-11-01T10:00:00Z", "pri_unknown"));
    const { license, events } = await licenseOf(sub);
    expect(license).toMatchObject({ plan: "standard", max_machines: 1, max_accounts: 1 });
    expect(events.map((e) => e.kind)).toContain("unknown_price");
  });

  it("refuses bad, old and missing signatures", async () => {
    const event = subscriptionEvent(`sub_${crypto.randomUUID()}`, "subscription.created", "active", "2026-10-01T10:00:00Z", null);
    expect((await deliver(event, (b) => signature(b, "wrong secret"))).status).toBe(401);
    expect((await deliver(event, (b) => signature(b, SECRET, Math.floor(Date.now() / 1000) - 3600))).status).toBe(401);
    expect((await deliver(event, async () => "")).status).toBe(401);
  });

  it("ignores other events", async () => {
    const response = await deliver({ event_id: "evt_x", event_type: "transaction.completed", occurred_at: "2026-10-01T10:00:00Z", data: { id: "txn_1" } });
    expect(await response.json()).toEqual({ ignored: "transaction.completed" });
  });

  it("is off without a webhook secret", async () => {
    const response = await call("/webhooks/paddle", { method: "POST", body: "{}" }, { PADDLE_WEBHOOK_SECRET: undefined });
    expect(response.status).toBe(503);
    expect(env.PADDLE_WEBHOOK_SECRET).toBe(SECRET);
  });
});
