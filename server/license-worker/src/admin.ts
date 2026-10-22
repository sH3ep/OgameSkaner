// /admin/…: managing licenses by hand (`ogs-license admin …`), behind `Authorization: Bearer <ADMIN_TOKEN>`.
//   GET    /admin/public-key                                 the deployed key's public half (compare with LicenseKeys.Production)
//   GET    /admin/licenses?q=&status=                        list, newest first
//   POST   /admin/licenses                                   create (manual sale, tester, yourself) → the key
//   GET    /admin/licenses/:id                               details with machines, accounts and events
//   PATCH  /admin/licenses/:id                               status, validity, limits, plan, contact, notes
//   POST   /admin/licenses/:id/key                           new key (the old one stops working), mailed on request
//   DELETE /admin/licenses/:id/machines/:machine             free a PC's place
//   DELETE /admin/licenses/:id/accounts/:universe/:playerId  unbind a game account

import { Db, type LicenseChanges, type LicenseRow, STATUSES, type LicenseStatus } from "./db";
import { type Env, planLimits } from "./env";
import { sendKey } from "./mail";
import { publicKeyOf, signingKey } from "./token";
import { HttpError, addDays, iso, json, keyHash, keyHint, newLicenseId, newLicenseKey, readJson, sameSecret } from "./util";

interface LicenseInput {
  email?: string | null;
  customer?: string | null;
  plan?: string;
  maxMachines?: number;
  maxAccounts?: number;
  /** ISO date/time, or null = no end. */
  validUntil?: string | null;
  /** Instead of validUntil: this many days from now. */
  days?: number;
  status?: string;
  notes?: string | null;
  /** Mail the key (create, new key). */
  mail?: boolean;
}

export async function handleAdmin(request: Request, env: Env, url: URL, now = new Date()): Promise<Response> {
  const auth = request.headers.get("authorization") ?? "";
  if (!env.ADMIN_TOKEN || !auth.startsWith("Bearer ") || !(await sameSecret(auth.slice(7), env.ADMIN_TOKEN)))
    throw new HttpError(401, "Not authorized.");

  const db = new Db(env.DB);
  const at = iso(now);
  const path = url.pathname.split("/").filter(Boolean).map(decodeURIComponent); // ["admin", …]
  const method = request.method;
  const [, resource, id, sub, ...rest] = path;

  if (resource === "public-key" && path.length === 2 && method === "GET")
    return json({ publicKey: await publicKeyOf(await signingKey(env.SIGNING_KEY)) });

  if (resource !== "licenses") throw new HttpError(404, "Not found.");

  if (!id) {
    if (method === "GET") {
      const licenses = await db.list(url.searchParams.get("q") ?? undefined, url.searchParams.get("status") ?? undefined);
      return json({ licenses: licenses.map(view) });
    }
    if (method === "POST") return create(env, db, await readJson<LicenseInput>(request), now);
    throw new HttpError(405, "Method not allowed.");
  }

  const license = await db.byId(id);
  if (!license) throw new HttpError(404, `No license ${id}.`);

  if (!sub) {
    if (method === "GET") {
      const [machines, accounts, events] = await Promise.all([db.machines(id), db.accounts(id), db.events(id)]);
      return json({ license: view(license), machines, accounts, events });
    }
    if (method === "PATCH") {
      const changes = changesOf(env, await readJson<LicenseInput>(request), now);
      await db.update(id, changes, at);
      await db.log(id, "admin_update", JSON.stringify(changes), at);
      return json({ license: view((await db.byId(id))!) });
    }
    throw new HttpError(405, "Method not allowed.");
  }

  if (sub === "key" && rest.length === 0 && method === "POST") {
    const input = await readJson<LicenseInput>(request).catch(() => ({}) as LicenseInput);
    const key = newLicenseKey();
    await db.update(id, { key_hash: await keyHash(key), key_hint: keyHint(key) }, at);
    await db.log(id, "key_rotated", `old …${license.key_hint}`, at);
    const mailed = input.mail ? await sendKey(env, db, license, key, at) : false;
    return json({ key, mailed });
  }

  if (sub === "machines" && rest.length === 1 && method === "DELETE") {
    if (!(await db.removeMachine(id, rest[0].toUpperCase()))) throw new HttpError(404, `No machine ${rest[0]} on ${id}.`);
    await db.log(id, "machine_removed", `${rest[0].toUpperCase()} by admin`, at);
    return new Response(null, { status: 204 });
  }

  if (sub === "accounts" && rest.length === 2 && method === "DELETE") {
    const [universe, playerId] = rest.map((s) => s.toLowerCase());
    if (!(await db.removeAccount(id, universe, playerId))) throw new HttpError(404, `No account ${universe}:${playerId} on ${id}.`);
    await db.log(id, "account_unbound", `${universe}:${playerId} by admin`, at);
    return new Response(null, { status: 204 });
  }

  throw new HttpError(404, "Not found.");
}

async function create(env: Env, db: Db, input: LicenseInput, now: Date): Promise<Response> {
  const at = iso(now);
  const limits = planLimits(env, input.plan);
  if (input.plan && limits.plan !== input.plan) throw new HttpError(400, `No plan "${input.plan}" (see PLANS).`);
  const changes = changesOf(env, { ...input, plan: undefined }, now);
  const key = newLicenseKey();
  const license = await db.insert(
    {
      id: newLicenseId(),
      key_hash: await keyHash(key),
      key_hint: keyHint(key),
      plan: limits.plan,
      max_machines: changes.max_machines ?? limits.maxMachines,
      max_accounts: changes.max_accounts ?? limits.maxAccounts,
      status: changes.status ?? "active",
      email: changes.email,
      customer: changes.customer,
      valid_until: changes.valid_until,
      notes: changes.notes,
    },
    at,
  );
  await db.log(license.id, "created", "by admin", at);
  const mailed = input.mail ? await sendKey(env, db, license, key, at) : false;
  return json({ license: view(license), key, mailed }, 201);
}

/** The columns an input sets; throws on a bad value. */
function changesOf(env: Env, input: LicenseInput, now: Date): LicenseChanges {
  const changes: LicenseChanges = {};
  const count = (value: unknown, name: string) => {
    if (typeof value !== "number" || !Number.isInteger(value) || value < 0) throw new HttpError(400, `${name} must be a whole number ≥ 0.`);
    return value;
  };
  const text = (value: string | null | undefined) => (value === undefined ? undefined : value?.trim() || null);

  if (input.plan !== undefined) {
    const limits = planLimits(env, input.plan);
    if (limits.plan !== input.plan) throw new HttpError(400, `No plan "${input.plan}" (see PLANS).`);
    Object.assign(changes, { plan: limits.plan, max_machines: limits.maxMachines, max_accounts: limits.maxAccounts });
  }
  if (input.maxMachines !== undefined) changes.max_machines = count(input.maxMachines, "maxMachines");
  if (input.maxAccounts !== undefined) changes.max_accounts = count(input.maxAccounts, "maxAccounts");
  if (input.status !== undefined) {
    if (!STATUSES.includes(input.status as LicenseStatus)) throw new HttpError(400, `status must be one of ${STATUSES.join(", ")}.`);
    changes.status = input.status as LicenseStatus;
  }
  if (input.days !== undefined) changes.valid_until = iso(addDays(now, count(input.days, "days")));
  else if (input.validUntil !== undefined) {
    if (input.validUntil === null) changes.valid_until = null;
    else {
      const date = new Date(input.validUntil);
      if (Number.isNaN(date.getTime())) throw new HttpError(400, "validUntil isn't a date.");
      changes.valid_until = iso(date);
    }
  }
  changes.email = text(input.email);
  changes.customer = text(input.customer);
  changes.notes = text(input.notes);
  return changes;
}

const VIEW_COLUMNS = [
  "id", "status", "plan", "email", "customer", "key_hint", "max_machines", "max_accounts", "valid_until", "notes",
  "paddle_subscription_id", "paddle_customer_id", "paddle_updated_at", "created_at", "updated_at",
] as const satisfies readonly (keyof LicenseRow)[];

/** A license without its key hash, in a readable order. */
function view(license: LicenseRow) {
  return Object.fromEntries(VIEW_COLUMNS.map((c) => [c, license[c]]));
}
