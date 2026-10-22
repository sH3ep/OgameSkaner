// Small helpers: JSON responses, license keys, ids, hashing and time.

export class HttpError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8" } });

/** The answer the app shows to the player as it is (HttpLicenseServer reads "error"). */
export const error = (status: number, message: string) => json({ error: message }, status);

export async function readJson<T>(request: Request): Promise<T> {
  try {
    return (await request.json()) as T;
  } catch {
    throw new HttpError(400, "The request body isn't JSON.");
  }
}

// Crockford base32 without I, L, O, U: easy to read out and type.
const ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

function randomGroups(groups: number, length: number): string[] {
  const bytes = crypto.getRandomValues(new Uint8Array(groups * length));
  const chars = Array.from(bytes, (b) => ALPHABET[b & 31]);
  return Array.from({ length: groups }, (_, i) => chars.slice(i * length, (i + 1) * length).join(""));
}

/** A new license key: "OGS-" + 4 groups of 5 (100 random bits). */
export const newLicenseKey = () => "OGS-" + randomGroups(4, 5).join("-");

/** A new license id: "L-" + 10 characters. */
export const newLicenseId = () => "L-" + randomGroups(1, 10)[0];

/** The key as typed: case, spaces and the look-alikes I/L → 1, O → 0 don't matter. */
export function normalizeKey(key: string): string {
  return key.trim().toUpperCase().replace(/\s+/g, "").replace(/[IL]/g, "1").replace(/O/g, "0").replace(/^0GS-/, "OGS-");
}

export async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("");
}

export const keyHash = (key: string) => sha256Hex(normalizeKey(key));

export const keyHint = (key: string) => normalizeKey(key).slice(-5);

/** Compares secrets without leaking where they differ (hashes first, so lengths match). */
export async function sameSecret(a: string, b: string): Promise<boolean> {
  const encoder = new TextEncoder();
  const [x, y] = await Promise.all([crypto.subtle.digest("SHA-256", encoder.encode(a)), crypto.subtle.digest("SHA-256", encoder.encode(b))]);
  return crypto.subtle.timingSafeEqual(x, y);
}

export const iso = (date: Date) => date.toISOString();

export const addHours = (date: Date, hours: number) => new Date(date.getTime() + hours * 3_600_000);

export const addDays = (date: Date, days: number) => addHours(date, days * 24);

/** Compares dotted versions numerically ("1.2" = "1.2.0.0"); a missing part counts as 0. */
export function compareVersions(a: string, b: string): number {
  const x = a.split(".").map(Number);
  const y = b.split(".").map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d !== 0) return Math.sign(d);
  }
  return 0;
}
