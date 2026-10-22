// The token format of OgameSkaner.Core.Licensing.LicenseToken: "OSL1." + base64url(payload JSON) + "." +
// base64url(signature), the signature ECDSA P-256/SHA-256 over the payload bytes in IEEE P1363 form (r‖s, 64 bytes),
// which is what WebCrypto produces. Keep the payload's names in step with LicensePayload (camelCase).

export const PREFIX = "OSL1.";

export interface LicensedAccount {
  universe: string;
  playerId: string;
}

export interface LicensePayload {
  licenseId: string;
  machine: string;
  accounts: LicensedAccount[];
  /** 0 = any game account. */
  maxAccounts: number;
  issuedAt: string;
  expiresAt: string;
  minAppVersion: string | null;
  customer: string | null;
  plan: string | null;
}

const encoder = new TextEncoder();

export function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

export function fromBase64Url(text: string): Uint8Array {
  const padded = text.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((text.length + 3) % 4);
  return Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
}

function pemBody(pem: string, label: string): Uint8Array {
  const match = pem.match(new RegExp(`-----BEGIN ${label}-----([\\s\\S]+?)-----END ${label}-----`));
  if (!match) throw new Error(`SIGNING_KEY isn't a ${label} PEM (ogs-license keygen writes one).`);
  return Uint8Array.from(atob(match[1].replace(/\s+/g, "")), (c) => c.charCodeAt(0));
}

let cached: { pem: string; key: Promise<CryptoKey> } | undefined;

/** The signing key from the SIGNING_KEY secret (PKCS#8 PEM), imported once per isolate. */
export function signingKey(pem: string): Promise<CryptoKey> {
  if (cached?.pem !== pem) {
    const key = crypto.subtle.importKey("pkcs8", pemBody(pem, "PRIVATE KEY"), { name: "ECDSA", namedCurve: "P-256" }, true, ["sign"]);
    cached = { pem, key };
  }
  return cached.key;
}

export async function sign(payload: LicensePayload, key: CryptoKey): Promise<string> {
  const bytes = encoder.encode(JSON.stringify(payload));
  const signature = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key, bytes));
  return PREFIX + base64Url(bytes) + "." + base64Url(signature);
}

/** The public key as the app embeds it (LicenseKeys.Production: SubjectPublicKeyInfo, base64). */
export async function publicKeyOf(privateKey: CryptoKey): Promise<string> {
  const jwk = (await crypto.subtle.exportKey("jwk", privateKey)) as JsonWebKey;
  delete jwk.d;
  jwk.key_ops = ["verify"];
  const publicKey = await crypto.subtle.importKey("jwk", jwk, { name: "ECDSA", namedCurve: "P-256" }, true, ["verify"]);
  const spki = new Uint8Array((await crypto.subtle.exportKey("spki", publicKey)) as ArrayBuffer);
  let binary = "";
  for (const b of spki) binary += String.fromCharCode(b);
  return btoa(binary);
}
