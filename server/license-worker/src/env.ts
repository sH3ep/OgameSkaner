export interface PlanConfig {
  maxMachines: number;
  /** 0 = any game account. */
  maxAccounts: number;
  paddlePriceIds?: string[];
}

export interface Env {
  DB: D1Database;
  TOKEN_LIMITER?: RateLimit;

  TOKEN_HOURS: string;
  GRACE_DAYS: string;
  MACHINE_RELEASE_DAYS: string;
  MIN_APP_VERSION: string;
  PLANS: Record<string, PlanConfig>;
  DEFAULT_PLAN: string;
  PADDLE_ENVIRONMENT: string;
  MAIL_FROM: string;

  SIGNING_KEY: string;
  ADMIN_TOKEN: string;
  PADDLE_WEBHOOK_SECRET?: string;
  PADDLE_API_KEY?: string;
  RESEND_API_KEY?: string;
}

/** A numeric var; empty or not a number = the fallback. */
export function numberVar(value: string | undefined, fallback: number): number {
  const n = Number(value);
  return value !== undefined && value.trim() !== "" && Number.isFinite(n) ? n : fallback;
}

/** The plan's limits; an unknown name falls back to DEFAULT_PLAN, then to 1 machine and 1 account. */
export function planLimits(env: Env, plan: string | undefined): { plan: string; maxMachines: number; maxAccounts: number } {
  const name = plan && env.PLANS[plan] ? plan : env.DEFAULT_PLAN;
  const config = env.PLANS[name];
  return { plan: name, maxMachines: config?.maxMachines ?? 1, maxAccounts: config?.maxAccounts ?? 1 };
}

/** The plan a Paddle price buys, or undefined when no plan lists it. */
export function planForPrice(env: Env, priceId: string | undefined): string | undefined {
  if (!priceId) return undefined;
  return Object.entries(env.PLANS).find(([, p]) => p.paddlePriceIds?.includes(priceId))?.[0];
}
