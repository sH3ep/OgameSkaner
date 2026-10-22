import { env } from "cloudflare:test";
import worker from "../src/index";
import type { Env } from "../src/env";
import { type LicensePayload, PREFIX, fromBase64Url } from "../src/token";

export const ADMIN = { authorization: "Bearer test-admin-token" };

export function call(path: string, init: RequestInit & { body?: string } = {}, overrides: Partial<Env> = {}): Promise<Response> {
  const request = new Request(`https://license.test${path}`, init) as Parameters<typeof worker.fetch>[0];
  return worker.fetch(request, { ...env, ...overrides } as never);
}

export const post = (path: string, body: unknown, headers: Record<string, string> = {}, overrides: Partial<Env> = {}) =>
  call(path, { method: "POST", headers: { "content-type": "application/json", ...headers }, body: JSON.stringify(body) }, overrides);

/** A random machine id in the app's format (XXXX-XXXX-XXXX-XXXX-XXXX). */
export function machineId(): string {
  const hex = Array.from(crypto.getRandomValues(new Uint8Array(10)), (b) => b.toString(16).padStart(2, "0")).join("").toUpperCase();
  return hex.match(/.{4}/g)!.join("-");
}

export async function createLicense(input: Record<string, unknown> = {}): Promise<{ id: string; key: string }> {
  const response = await post("/admin/licenses", { email: "buyer@example.com", ...input }, ADMIN);
  if (response.status !== 201) throw new Error(`create: ${response.status} ${await response.text()}`);
  const body = (await response.json()) as { license: { id: string }; key: string };
  return { id: body.license.id, key: body.key };
}

export const requestToken = (licenseKey: string, machine: string, extra: Record<string, unknown> = {}, overrides: Partial<Env> = {}) =>
  post("/v1/token", { licenseKey, machine, appVersion: "1.0.0.0", ...extra }, {}, overrides);

/** Verifies the token with the test public key the way the app does, and returns its payload. */
export async function verify(token: string): Promise<LicensePayload> {
  if (!token.startsWith(PREFIX)) throw new Error("no prefix");
  const [payload, signature] = token.slice(PREFIX.length).split(".");
  const spki = Uint8Array.from(atob(env.TEST_PUBLIC_KEY), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("spki", spki, { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  const bytes = fromBase64Url(payload);
  const sig = fromBase64Url(signature);
  if (sig.length !== 64) throw new Error(`signature is ${sig.length} bytes, not P1363`);
  if (!(await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, sig, bytes))) throw new Error("signature doesn't verify");
  return JSON.parse(new TextDecoder().decode(bytes)) as LicensePayload;
}

export async function tokenPayload(response: Response): Promise<LicensePayload> {
  const body = (await response.json()) as { token?: string; error?: string };
  if (!body.token) throw new Error(`no token: ${response.status} ${body.error}`);
  return verify(body.token);
}

export async function errorOf(response: Response): Promise<string> {
  return ((await response.json()) as { error: string }).error;
}

export const details = async (id: string) =>
  (await (await call(`/admin/licenses/${id}`, { headers: ADMIN })).json()) as {
    license: Record<string, unknown>;
    machines: { machine: string }[];
    accounts: { universe: string; player_id: string }[];
    events: { kind: string; detail: string }[];
  };
