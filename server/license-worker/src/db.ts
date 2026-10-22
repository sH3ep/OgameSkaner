// D1 access: the licenses, their machines and game accounts, and the event log.

export type LicenseStatus = "active" | "past_due" | "cancelled" | "revoked";

export const STATUSES: readonly LicenseStatus[] = ["active", "past_due", "cancelled", "revoked"];

export interface LicenseRow {
  id: string;
  key_hash: string;
  key_hint: string;
  email: string | null;
  customer: string | null;
  plan: string;
  max_machines: number;
  max_accounts: number;
  status: LicenseStatus;
  valid_until: string | null;
  paddle_subscription_id: string | null;
  paddle_customer_id: string | null;
  paddle_updated_at: string | null;
  notes: string | null;
  created_at: string;
  updated_at: string;
}

export interface MachineRow {
  license_id: string;
  machine: string;
  first_seen: string;
  last_seen: string;
  last_version: string | null;
  last_ip: string | null;
  last_country: string | null;
  requests: number;
}

export interface AccountRow {
  license_id: string;
  universe: string;
  player_id: string;
  bound_at: string;
}

export interface EventRow {
  id: number;
  license_id: string | null;
  at: string;
  kind: string;
  detail: string | null;
}

/** The fields a new license needs; the rest are filled in. */
export type NewLicense = Pick<LicenseRow, "id" | "key_hash" | "key_hint" | "plan" | "max_machines" | "max_accounts" | "status"> &
  Partial<Pick<LicenseRow, "email" | "customer" | "valid_until" | "paddle_subscription_id" | "paddle_customer_id" | "paddle_updated_at" | "notes">>;

/** Columns an update may change. */
export type LicenseChanges = Partial<Omit<LicenseRow, "id" | "created_at" | "updated_at">>;

export class Db {
  constructor(private readonly db: D1Database) {}

  byKeyHash(hash: string) {
    return this.db.prepare("SELECT * FROM licenses WHERE key_hash = ?").bind(hash).first<LicenseRow>();
  }

  byId(id: string) {
    return this.db.prepare("SELECT * FROM licenses WHERE id = ?").bind(id).first<LicenseRow>();
  }

  bySubscription(subscriptionId: string) {
    return this.db.prepare("SELECT * FROM licenses WHERE paddle_subscription_id = ?").bind(subscriptionId).first<LicenseRow>();
  }

  /** Newest first; `query` matches the id, e-mail, customer, key hint or subscription id. */
  async list(query: string | undefined, status: string | undefined, limit = 200): Promise<LicenseRow[]> {
    const where: string[] = [];
    const args: unknown[] = [];
    if (query) {
      where.push("(id LIKE ?1 OR email LIKE ?1 OR customer LIKE ?1 OR key_hint LIKE ?1 OR paddle_subscription_id LIKE ?1)");
      args.push(`%${query}%`);
    }
    if (status) {
      where.push(`status = ?${args.length + 1}`);
      args.push(status);
    }
    const sql = `SELECT * FROM licenses ${where.length ? "WHERE " + where.join(" AND ") : ""} ORDER BY created_at DESC LIMIT ${limit}`;
    return (await this.db.prepare(sql).bind(...args).all<LicenseRow>()).results;
  }

  async insert(license: NewLicense, now: string): Promise<LicenseRow> {
    const row: LicenseRow = {
      email: null,
      customer: null,
      valid_until: null,
      paddle_subscription_id: null,
      paddle_customer_id: null,
      paddle_updated_at: null,
      notes: null,
      ...(Object.fromEntries(Object.entries(license).filter(([, v]) => v !== undefined)) as NewLicense),
      created_at: now,
      updated_at: now,
    };
    const columns = Object.keys(row);
    await this.db
      .prepare(`INSERT INTO licenses (${columns.join(", ")}) VALUES (${columns.map(() => "?").join(", ")})`)
      .bind(...Object.values(row))
      .run();
    return row;
  }

  async update(id: string, changes: LicenseChanges, now: string): Promise<void> {
    const entries = Object.entries(changes).filter(([, v]) => v !== undefined);
    if (entries.length === 0) return;
    const sets = entries.map(([k]) => `${k} = ?`).join(", ");
    await this.db
      .prepare(`UPDATE licenses SET ${sets}, updated_at = ? WHERE id = ?`)
      .bind(...entries.map(([, v]) => v), now, id)
      .run();
  }

  async machines(licenseId: string): Promise<MachineRow[]> {
    return (await this.db.prepare("SELECT * FROM machines WHERE license_id = ? ORDER BY first_seen").bind(licenseId).all<MachineRow>()).results;
  }

  async addMachine(licenseId: string, machine: string, now: string, version: string, ip: string | null, country: string | null) {
    await this.db
      .prepare(
        "INSERT INTO machines (license_id, machine, first_seen, last_seen, last_version, last_ip, last_country, requests) VALUES (?, ?, ?, ?, ?, ?, ?, 1)",
      )
      .bind(licenseId, machine, now, now, version, ip, country)
      .run();
  }

  async touchMachine(licenseId: string, machine: string, now: string, version: string, ip: string | null, country: string | null) {
    await this.db
      .prepare(
        "UPDATE machines SET last_seen = ?, last_version = ?, last_ip = ?, last_country = ?, requests = requests + 1 WHERE license_id = ? AND machine = ?",
      )
      .bind(now, version, ip, country, licenseId, machine)
      .run();
  }

  /** True when a machine was removed. */
  async removeMachine(licenseId: string, machine: string): Promise<boolean> {
    const result = await this.db.prepare("DELETE FROM machines WHERE license_id = ? AND machine = ?").bind(licenseId, machine).run();
    return result.meta.changes > 0;
  }

  async accounts(licenseId: string): Promise<AccountRow[]> {
    return (await this.db.prepare("SELECT * FROM accounts WHERE license_id = ? ORDER BY bound_at").bind(licenseId).all<AccountRow>()).results;
  }

  async addAccount(licenseId: string, universe: string, playerId: string, now: string) {
    await this.db
      .prepare("INSERT OR IGNORE INTO accounts (license_id, universe, player_id, bound_at) VALUES (?, ?, ?, ?)")
      .bind(licenseId, universe, playerId, now)
      .run();
  }

  /** True when an account was removed. */
  async removeAccount(licenseId: string, universe: string, playerId: string): Promise<boolean> {
    const result = await this.db
      .prepare("DELETE FROM accounts WHERE license_id = ? AND universe = ? AND player_id = ?")
      .bind(licenseId, universe, playerId)
      .run();
    return result.meta.changes > 0;
  }

  async log(licenseId: string | null, kind: string, detail: string | null, now: string) {
    await this.db.prepare("INSERT INTO events (license_id, at, kind, detail) VALUES (?, ?, ?, ?)").bind(licenseId, now, kind, detail).run();
  }

  async events(licenseId: string, limit = 100): Promise<EventRow[]> {
    return (await this.db.prepare(`SELECT * FROM events WHERE license_id = ? ORDER BY id DESC LIMIT ${limit}`).bind(licenseId).all<EventRow>()).results;
  }

  /** False when the webhook event was applied already. */
  async firstDelivery(eventId: string, type: string, now: string): Promise<boolean> {
    const result = await this.db.prepare("INSERT OR IGNORE INTO webhook_events (id, type, received_at) VALUES (?, ?, ?)").bind(eventId, type, now).run();
    return result.meta.changes > 0;
  }

  async forgetDelivery(eventId: string) {
    await this.db.prepare("DELETE FROM webhook_events WHERE id = ?").bind(eventId).run();
  }
}
