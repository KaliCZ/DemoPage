#!/usr/bin/env node
// Boots the Aspire AppHost with per-worktree ports.
//
// The offset is derived deterministically from the worktree path (SHA-256
// of cwd, mod 800), so two worktrees on the same machine almost never pick
// the same offset. Only the AppHost-owned ports (dashboard, OTLP exporter,
// resource service) shift with the offset — API and frontend ports are
// dynamic via dcp and can't clash. Supabase is intentionally shared via
// a single `npm run dev:infra`.
//
// Set KALANDRA_PORT_OFFSET to override (e.g. on the rare collision, or to
// pin a memorable port).

import { spawn } from "node:child_process";
import { createHash } from "node:crypto";

function deriveOffset() {
    const explicit = process.env.KALANDRA_PORT_OFFSET;
    if (explicit !== undefined && explicit !== "") {
        const parsed = Number.parseInt(explicit, 10);
        if (Number.isNaN(parsed)) {
            console.error(`KALANDRA_PORT_OFFSET must be an integer, got: ${explicit}`);
            process.exit(1);
        }
        return { offset: parsed, source: "env" };
    }
    // Hash the absolute cwd so each worktree gets a stable offset across runs.
    // Range capped at 800 — smaller than the gap between OTLP (19200) and
    // resource service (20056), so cross-port collisions stay impossible.
    const hash = createHash("sha256").update(process.cwd()).digest();
    const offset = hash.readUInt32BE(0) % 800;
    return { offset, source: "hash" };
}

const { offset, source } = deriveOffset();

const ports = {
    dashboard: 15036 + offset,
    otlp: 19200 + offset,
    resource: 20056 + offset,
};

const sourceLabel = source === "env" ? "from KALANDRA_PORT_OFFSET" : "derived from worktree path";
console.log(`Aspire (offset=${offset}, ${sourceLabel}):`);
console.log(`  Dashboard:        http://localhost:${ports.dashboard}`);
console.log(`  OTLP exporter:    http://localhost:${ports.otlp}`);
console.log(`  Resource service: http://localhost:${ports.resource}`);
console.log(`  API + frontend:   allocated by Aspire — see dashboard`);

const env = {
    ...process.env,
    KALANDRA_PORT_OFFSET: String(offset),
    ASPNETCORE_URLS: `http://localhost:${ports.dashboard}`,
    ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL: `http://localhost:${ports.otlp}`,
    ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL: `http://localhost:${ports.resource}`,
    ASPIRE_ALLOW_UNSECURED_TRANSPORT: "true",
    ASPNETCORE_ENVIRONMENT: "Development",
    DOTNET_ENVIRONMENT: "Development",
};

// --no-launch-profile so launchSettings.json doesn't override our ports.
const child = spawn(
    "dotnet",
    ["run", "--project", "aspire/Kalandra.AppHost", "--no-launch-profile"],
    { stdio: "inherit", env, shell: process.platform === "win32" }
);

child.on("exit", (code) => process.exit(code ?? 1));
