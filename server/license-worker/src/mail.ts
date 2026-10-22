// The license key e-mail, sent through Resend (https://resend.com) when RESEND_API_KEY and MAIL_FROM are set.

import type { Db, LicenseRow } from "./db";
import type { Env } from "./env";

const canMail = (env: Env) => Boolean(env.RESEND_API_KEY && env.MAIL_FROM);

/** Throws when Resend refuses; the caller logs it, the key can be rotated and sent again. */
async function mailLicenseKey(env: Env, to: string, key: string, maxMachines: number, maxAccounts: number): Promise<void> {
  const text = [
    "Thank you for subscribing to OgameSkaner.",
    "",
    `Your license key: ${key}`,
    "",
    "To activate it, start OgameSkaner, accept the terms in the license window, paste the key into",
    `"License key" and click Activate. The license works on ${maxMachines} PC(s) and ` +
      (maxAccounts === 0 ? "any game account." : `${maxAccounts} game account(s); the first ones you play are bound to it.`),
    "",
    "Keep this e-mail: the key is the only proof of your license.",
  ].join("\n");

  const response = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: { authorization: `Bearer ${env.RESEND_API_KEY}`, "content-type": "application/json" },
    body: JSON.stringify({ from: env.MAIL_FROM, to: [to], subject: "Your OgameSkaner license key", text }),
  });
  if (!response.ok) throw new Error(`Resend answered ${response.status}: ${(await response.text()).slice(0, 300)}`);
}

/** Mails the new key; without e-mail it's logged, and `ogs-license admin rotate-key` makes one to hand out. */
export async function sendKey(env: Env, db: Db, license: LicenseRow, key: string, at: string): Promise<boolean> {
  if (!license.email || !canMail(env)) {
    await db.log(license.id, "key_not_mailed", license.email ? "no mail configured" : "no e-mail address", at);
    return false;
  }
  try {
    await mailLicenseKey(env, license.email, key, license.max_machines, license.max_accounts);
    await db.log(license.id, "key_mailed", license.email, at);
    return true;
  } catch (e) {
    await db.log(license.id, "key_mail_failed", String(e), at);
    return false;
  }
}
