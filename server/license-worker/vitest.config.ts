import { generateKeyPairSync } from "node:crypto";
import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

// The tests sign with a key made for this run; TEST_PUBLIC_KEY is its public half (SubjectPublicKeyInfo, base64).
const keys = generateKeyPairSync("ec", { namedCurve: "P-256" });

export default defineConfig(async () => ({
  plugins: [
    cloudflareTest({
      wrangler: { configPath: "./wrangler.jsonc" },
      miniflare: {
        bindings: {
          TEST_MIGRATIONS: await readD1Migrations("./migrations"),
          TEST_PUBLIC_KEY: keys.publicKey.export({ type: "spki", format: "der" }).toString("base64"),
          SIGNING_KEY: keys.privateKey.export({ type: "pkcs8", format: "pem" }).toString(),
          ADMIN_TOKEN: "test-admin-token",
          PADDLE_WEBHOOK_SECRET: "pdl_ntfset_test_secret",
        },
      },
    }),
  ],
  test: { setupFiles: ["./test/setup.ts"] },
}));
