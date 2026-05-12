#!/usr/bin/env node
// Boots the Aspire AppHost with per-worktree ports.
//
// Set KALANDRA_PORT_OFFSET to a non-zero integer when running a second
// worktree in parallel. Only the AppHost-owned ports get the offset added
// (dashboard, OTLP exporter, resource service). API and frontend ports are
// allocated dynamically by dcp and discovered via service discovery, so they
// can't clash across worktrees. Supabase is intentionally shared via a
// single `npm run dev:infra`.

import { spawn } from "node:child_process";

const offset = Number.parseInt(process.env.KALANDRA_PORT_OFFSET ?? "0", 10);
if (Number.isNaN(offset)) {
    console.error(`KALANDRA_PORT_OFFSET must be an integer, got: ${process.env.KALANDRA_PORT_OFFSET}`);
    process.exit(1);
}

const ports = {
    dashboard: 15036 + offset,
    otlp: 19200 + offset,
    resource: 20056 + offset,
};

console.log(`Aspire (KALANDRA_PORT_OFFSET=${offset}):`);
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
