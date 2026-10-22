-- Times are ISO 8601 UTC strings (Date.toISOString()), so they sort and compare as text.

CREATE TABLE licenses (
    id                     TEXT PRIMARY KEY,         -- "L-…", the token's licenseId
    key_hash               TEXT NOT NULL UNIQUE,     -- SHA-256 (hex) of the license key; the key is shown/mailed once
    key_hint               TEXT NOT NULL,            -- the key's last group, to recognise it
    email                  TEXT,
    customer               TEXT,                     -- name for the token (else the e-mail)
    plan                   TEXT NOT NULL,
    max_machines           INTEGER NOT NULL,
    max_accounts           INTEGER NOT NULL,         -- 0 = any game account
    status                 TEXT NOT NULL,            -- active | past_due | cancelled | revoked
    valid_until            TEXT,                     -- null = no end (licenses made by hand)
    paddle_subscription_id TEXT UNIQUE,
    paddle_customer_id     TEXT,
    paddle_updated_at      TEXT,                     -- occurred_at of the last Paddle event applied; older ones are skipped
    notes                  TEXT,
    created_at             TEXT NOT NULL,
    updated_at             TEXT NOT NULL
);

CREATE INDEX licenses_email ON licenses (email);

-- The PCs a license is activated on (IMachineFingerprint ids).
CREATE TABLE machines (
    license_id   TEXT NOT NULL REFERENCES licenses (id) ON DELETE CASCADE,
    machine      TEXT NOT NULL,
    first_seen   TEXT NOT NULL,
    last_seen    TEXT NOT NULL,
    last_version TEXT,
    last_ip      TEXT,
    last_country TEXT,
    requests     INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (license_id, machine)
);

-- The game accounts bound to a license (universe and player id lower-case).
CREATE TABLE accounts (
    license_id TEXT NOT NULL REFERENCES licenses (id) ON DELETE CASCADE,
    universe   TEXT NOT NULL,
    player_id  TEXT NOT NULL,
    bound_at   TEXT NOT NULL,
    PRIMARY KEY (license_id, universe, player_id)
);

-- What happened to a license (activations, bindings, refusals, Paddle changes, admin changes); renewals aren't logged.
CREATE TABLE events (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    license_id TEXT,
    at         TEXT NOT NULL,
    kind       TEXT NOT NULL,
    detail     TEXT
);

CREATE INDEX events_license ON events (license_id, at);

-- Paddle event ids already applied (Paddle retries and may deliver twice).
CREATE TABLE webhook_events (
    id          TEXT PRIMARY KEY,
    type        TEXT NOT NULL,
    received_at TEXT NOT NULL
);
