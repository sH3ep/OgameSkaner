// The OgameSkaner license server: signs license tokens for the app, keeps the subscriptions in step with Paddle,
// and lets the owner manage licenses. See README.md and docs/licensing-plan.md.

import { handleAdmin } from "./admin";
import type { Env } from "./env";
import { handleToken } from "./issue";
import { handlePaddle } from "./paddle";
import { HttpError, error, json } from "./util";

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/v1/token" && request.method === "POST") return await handleToken(request, env);
      if (url.pathname === "/webhooks/paddle" && request.method === "POST") return await handlePaddle(request, env);
      if (url.pathname.startsWith("/admin/")) return await handleAdmin(request, env, url);
      if (url.pathname === "/health" && request.method === "GET") return json({ ok: true });
      return error(404, "Not found.");
    } catch (e) {
      if (e instanceof HttpError) return error(e.status, e.message);
      console.error(`${request.method} ${url.pathname}:`, e);
      return error(500, "The license server failed; try again later.");
    }
  },
} satisfies ExportedHandler<Env>;
