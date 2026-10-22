// POST /webhooks/paddle: Paddle Billing's subscription events create and update the licenses.
// Subscribe the notification destination to subscription.created, .activated, .updated, .trialing, .past_due,
// .paused, .resumed and .canceled. Every one carries the whole subscription, so they are handled alike.

import { Db, type LicenseRow, type LicenseStatus } from "./db";
import { type Env, numberVar, planForPrice, planLimits } from "./env";
import { sendKey } from "./mail";
import { HttpError, addDays, iso, json, keyHash, keyHint, newLicenseId, newLicenseKey } from "./util";

interface PaddleEvent {
  event_id: string;
  event_type: string;
  occurred_at: string;
  data: PaddleSubscription;
}

interface PaddleSubscription {
  id: string;
  status: "active" | "trialing" | "past_due" | "paused" | "canceled" | string;
  customer_id?: string;
  items?: { price?: { id?: string } }[];
  current_billing_period?: { starts_at: string; ends_at: string } | null;
}

/** Signatures older than this are refused (replays). */
const MAX_SIGNATURE_AGE_SECONDS = 300;

export async function handlePaddle(request: Request, env: Env, now = new Date()): Promise<Response> {
  if (!env.PADDLE_WEBHOOK_SECRET) throw new HttpError(503, "Paddle isn't configured.");
  const raw = await request.text();
  if (!(await validSignature(raw, request.headers.get("Paddle-Signature"), env.PADDLE_WEBHOOK_SECRET, now)))
    throw new HttpError(401, "The Paddle signature doesn't match.");

  let event: PaddleEvent;
  try {
    event = JSON.parse(raw) as PaddleEvent;
  } catch {
    throw new HttpError(400, "The body isn't JSON.");
  }
  if (!event.event_type?.startsWith("subscription.") || !event.data?.id) return json({ ignored: event.event_type ?? null });

  const db = new Db(env.DB);
  if (!(await db.firstDelivery(event.event_id, event.event_type, iso(now)))) return json({ duplicate: event.event_id });
  try {
    await applySubscription(env, db, event, now);
  } catch (e) {
    // Paddle retries a failed delivery; let the retry apply it.
    await db.forgetDelivery(event.event_id);
    throw e;
  }
  return json({ ok: true });
}

/** `Paddle-Signature: ts=<unix seconds>;h1=<hex HMAC-SHA256 of "ts:body">` (several h1 while a secret rotates). */
export async function validSignature(raw: string, header: string | null, secret: string, now: Date): Promise<boolean> {
  if (!header) return false;
  const parts = header.split(";").map((p) => p.trim().split("=", 2) as [string, string]);
  const ts = parts.find(([k]) => k === "ts")?.[1];
  const signatures = parts.filter(([k]) => k === "h1").map(([, v]) => v);
  if (!ts || !/^\d+$/.test(ts) || signatures.length === 0) return false;
  if (Math.abs(now.getTime() / 1000 - Number(ts)) > MAX_SIGNATURE_AGE_SECONDS) return false;

  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const expected = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(`${ts}:${raw}`)));
  return signatures.some((s) => {
    if (!/^[0-9a-f]{64}$/i.test(s)) return false;
    const given = Uint8Array.from(s.match(/../g)!, (h) => parseInt(h, 16));
    return crypto.subtle.timingSafeEqual(given, expected);
  });
}

async function applySubscription(env: Env, db: Db, event: PaddleEvent, now: Date) {
  const at = iso(now);
  const sub = event.data;
  const existing = await db.bySubscription(sub.id);
  if (existing?.paddle_updated_at && event.occurred_at < existing.paddle_updated_at) {
    await db.log(existing.id, "paddle_skipped", `${event.event_type} of ${event.occurred_at} is older than the last applied`, at);
    return;
  }

  const priceId = sub.items?.[0]?.price?.id;
  const pricePlan = planForPrice(env, priceId);
  const limits = planLimits(env, pricePlan ?? existing?.plan);
  const status = statusOf(sub.status);
  const validUntil = validUntilOf(env, sub, status, event.occurred_at, existing);

  if (!existing) {
    const email = await customerEmail(env, sub.customer_id);
    const key = newLicenseKey();
    const license = await db.insert(
      {
        id: newLicenseId(),
        key_hash: await keyHash(key),
        key_hint: keyHint(key),
        email,
        plan: limits.plan,
        max_machines: limits.maxMachines,
        max_accounts: limits.maxAccounts,
        status,
        valid_until: validUntil,
        paddle_subscription_id: sub.id,
        paddle_customer_id: sub.customer_id ?? null,
        paddle_updated_at: event.occurred_at,
      },
      at,
    );
    await db.log(license.id, "created", `${event.event_type} ${sub.id} (${sub.status}), plan ${limits.plan}, until ${validUntil}`, at);
    if (!pricePlan) await db.log(license.id, "unknown_price", `${priceId ?? "no price"}: plan ${limits.plan} used`, at);
    await sendKey(env, db, license, key, at);
    return;
  }

  const planChanged = pricePlan !== undefined && pricePlan !== existing.plan;
  await db.update(
    existing.id,
    {
      // A license revoked by hand stays revoked.
      status: existing.status === "revoked" ? "revoked" : status,
      valid_until: validUntil,
      ...(planChanged ? { plan: limits.plan, max_machines: limits.maxMachines, max_accounts: limits.maxAccounts } : {}),
      paddle_customer_id: sub.customer_id ?? existing.paddle_customer_id,
      paddle_updated_at: event.occurred_at,
      email: existing.email ?? (await customerEmail(env, sub.customer_id)),
    },
    at,
  );
  await db.log(
    existing.id,
    "paddle",
    `${event.event_type}: ${sub.status}, until ${validUntil}` + (planChanged ? `, plan ${existing.plan} → ${limits.plan}` : ""),
    at,
  );
}

function statusOf(paddleStatus: string): LicenseStatus {
  switch (paddleStatus) {
    case "active":
    case "trialing":
      return "active";
    case "past_due":
      return "past_due";
    default: // canceled, paused
      return "cancelled";
  }
}

/**
 * Paid: the billing period's end plus the grace days. Past due: at most the grace days from now (Paddle may already
 * have moved the period on). Ended: unchanged (the status refuses it anyway).
 */
function validUntilOf(env: Env, sub: PaddleSubscription, status: LicenseStatus, occurredAt: string, existing: LicenseRow | null): string | null {
  const grace = numberVar(env.GRACE_DAYS, 3);
  const periodEnd = sub.current_billing_period?.ends_at ? iso(addDays(new Date(sub.current_billing_period.ends_at), grace)) : null;
  switch (status) {
    case "active":
      return periodEnd ?? existing?.valid_until ?? null;
    case "past_due": {
      const dunning = iso(addDays(new Date(occurredAt), grace));
      return periodEnd && periodEnd < dunning ? periodEnd : dunning;
    }
    default:
      return existing?.valid_until ?? occurredAt;
  }
}

async function customerEmail(env: Env, customerId: string | undefined): Promise<string | null> {
  if (!customerId || !env.PADDLE_API_KEY) return null;
  const base = env.PADDLE_ENVIRONMENT === "production" ? "https://api.paddle.com" : "https://sandbox-api.paddle.com";
  try {
    const response = await fetch(`${base}/customers/${encodeURIComponent(customerId)}`, {
      headers: { authorization: `Bearer ${env.PADDLE_API_KEY}` },
    });
    if (!response.ok) {
      console.warn(`Paddle customer ${customerId}: ${response.status}`);
      return null;
    }
    const body = (await response.json()) as { data?: { email?: string } };
    return body.data?.email ?? null;
  } catch (e) {
    console.warn(`Paddle customer ${customerId}: ${e}`);
    return null;
  }
}
