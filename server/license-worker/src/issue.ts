// POST /v1/token: activation, renewal and binding a game account (OgameSkaner.Core.Licensing.HttpLicenseServer).

import { Db, type LicenseRow, type MachineRow } from "./db";
import { type Env, numberVar } from "./env";
import { type LicensePayload, sign, signingKey } from "./token";
import { HttpError, addDays, addHours, compareVersions, error, iso, json, keyHash, readJson } from "./util";

interface TokenRequest {
  licenseKey?: unknown;
  machine?: unknown;
  appVersion?: unknown;
  account?: { universe?: unknown; playerId?: unknown } | null;
}

const MACHINE = /^[0-9A-F]{4}(-[0-9A-F]{4}){4}$/;
const VERSION = /^\d{1,9}(\.\d{1,9}){0,3}$/;
const UNIVERSE = /^[a-z0-9-]{1,63}$/;
const PLAYER = /^[a-z0-9-]{1,64}$/;

/** A refusal the player sees in the app, logged for a known license. */
class Refusal extends HttpError {}

export async function handleToken(request: Request, env: Env, now = new Date()): Promise<Response> {
  const body = await readJson<TokenRequest>(request);
  const licenseKey = typeof body.licenseKey === "string" ? body.licenseKey : "";
  const machine = typeof body.machine === "string" ? body.machine.trim().toUpperCase() : "";
  const appVersion = typeof body.appVersion === "string" ? body.appVersion.trim() : "";
  const ip = request.headers.get("CF-Connecting-IP");

  if (env.TOKEN_LIMITER) {
    const { success } = await env.TOKEN_LIMITER.limit({ key: MACHINE.test(machine) ? machine : (ip ?? "unknown") });
    if (!success) return error(429, "Too many license requests; try again in a minute.");
  }

  if (!licenseKey.trim()) return error(400, "Enter the license key.");
  if (!MACHINE.test(machine)) return error(400, "The machine id is missing or malformed.");
  if (!VERSION.test(appVersion)) return error(400, "The app version is missing or malformed.");

  let account: { universe: string; playerId: string } | undefined;
  if (body.account) {
    const universe = String(body.account.universe ?? "").trim().toLowerCase();
    const playerId = String(body.account.playerId ?? "").trim().toLowerCase();
    if (!UNIVERSE.test(universe) || !PLAYER.test(playerId)) return error(400, "The game account is malformed.");
    account = { universe, playerId };
  }

  const db = new Db(env.DB);
  const license = await db.byKeyHash(await keyHash(licenseKey));
  if (!license) return error(403, "Unknown license key. Check it against the e-mail you got after buying.");

  const at = iso(now);
  const country = (request.cf?.country as string | undefined) ?? null;
  try {
    usable(license, now);
    const minimum = env.MIN_APP_VERSION?.trim() || null;
    if (minimum && compareVersions(appVersion, minimum) < 0)
      throw new Refusal(426, `This version of OgameSkaner (${appVersion}) is too old. Update to ${minimum} or later.`);

    const machines = await db.machines(license.id);
    const known = machines.find((m) => m.machine === machine);
    const released = known ? undefined : machineSlot(license, machines, now, numberVar(env.MACHINE_RELEASE_DAYS, 14));

    const accounts = await db.accounts(license.id);
    const bind =
      account &&
      license.max_accounts > 0 &&
      !accounts.some((a) => a.universe === account.universe && a.player_id === account.playerId);
    if (bind && accounts.length >= license.max_accounts)
      throw new Refusal(
        403,
        `All ${license.max_accounts} game account(s) of this license are taken (${accounts.map((a) => a.universe).join(", ")}). ` +
          "Ask support to move the license to another account.",
      );

    // Everything checked: now change the records.
    if (known) await db.touchMachine(license.id, machine, at, appVersion, ip, country);
    else {
      if (released) {
        await db.removeMachine(license.id, released.machine);
        await db.log(license.id, "machine_released", `${released.machine} (last seen ${released.last_seen}) for ${machine}`, at);
      }
      await db.addMachine(license.id, machine, at, appVersion, ip, country);
      await db.log(license.id, "machine_added", `${machine}, ${appVersion}, ${ip ?? "?"} ${country ?? ""}`.trim(), at);
    }
    if (bind && account) {
      await db.addAccount(license.id, account.universe, account.playerId, at);
      accounts.push({ license_id: license.id, universe: account.universe, player_id: account.playerId, bound_at: at });
      await db.log(license.id, "account_bound", `${account.universe}:${account.playerId} from ${machine}`, at);
    }

    const tokenEnd = addHours(now, numberVar(env.TOKEN_HOURS, 72));
    const payload: LicensePayload = {
      licenseId: license.id,
      machine,
      accounts: accounts.map((a) => ({ universe: a.universe, playerId: a.player_id })),
      maxAccounts: license.max_accounts,
      issuedAt: at,
      expiresAt: iso(license.valid_until && license.valid_until < iso(tokenEnd) ? new Date(license.valid_until) : tokenEnd),
      minAppVersion: minimum,
      customer: license.customer ?? license.email,
      plan: license.plan,
    };
    return json({ token: await sign(payload, await signingKey(env.SIGNING_KEY)) });
  } catch (e) {
    if (!(e instanceof Refusal)) throw e;
    await db.log(license.id, "refused", `${machine}: ${e.message}`, at);
    return error(e.status, e.message);
  }
}

/** Throws when the license can't get a token now. */
function usable(license: LicenseRow, now: Date) {
  if (license.status === "revoked") throw new Refusal(403, "This license key was revoked. Contact support.");
  if (license.status === "cancelled") throw new Refusal(403, "The subscription of this license has ended.");
  if (license.valid_until && license.valid_until <= iso(now))
    throw new Refusal(403, `This license expired on ${license.valid_until.slice(0, 10)}. Renew the subscription to continue.`);
}

/**
 * Room for a new machine: undefined when there's a free slot, else the machine idle longest (past the release days)
 * to make room. Throws when every slot is in use.
 */
function machineSlot(license: LicenseRow, machines: MachineRow[], now: Date, releaseDays: number): MachineRow | undefined {
  if (machines.length < license.max_machines) return undefined;
  if (releaseDays > 0) {
    const idleBefore = iso(addDays(now, -releaseDays));
    const idle = machines.filter((m) => m.last_seen < idleBefore).sort((a, b) => a.last_seen.localeCompare(b.last_seen));
    if (idle.length > 0 && machines.length - 1 < license.max_machines) return idle[0];
  }
  throw new Refusal(
    403,
    `This license is already active on ${machines.length} PC(s), the most it allows. ` +
      (releaseDays > 0 ? `A PC frees its place after ${releaseDays} days unused, or ` : "") +
      "ask support to move it to this PC.",
  );
}
