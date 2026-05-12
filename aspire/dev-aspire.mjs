#!/usr/bin/env node
// Boots the Aspire AppHost.
//
// By default each AppHost-owned port (dashboard, OTLP exporter, resource
// service) is picked by the OS via `listen(0)`, so parallel worktrees never
// collide. API and frontend ports are allocated by dcp inside the AppHost,
// so they're never an issue either. Supabase is intentionally shared via
// `npm run dev:infra`.
//
// Tradeoff: dashboard URL changes every run. Pin it with KALANDRA_PORT_OFFSET
// if you want a stable, bookmarkable URL (15036/19200/20056 plus offset).

import { spawn } from "node:child_process";
import { createServer } from "node:net";

function findFreePort() {
    return new Promise((resolve, reject) => {
        const srv = createServer();
        srv.on("error", reject);
        srv.listen(0, "127.0.0.1", () => {
            const { port } = srv.address();
            srv.close((err) => (err ? reject(err) : resolve(port)));
        });
    });
}

async function resolvePorts() {
    const explicit = process.env.KALANDRA_PORT_OFFSET;
    if (explicit !== undefined && explicit !== "") {
        const offset = Number.parseInt(explicit, 10);
        if (Number.isNaN(offset)) {
            console.error(`KALANDRA_PORT_OFFSET must be an integer, got: ${explicit}`);
            process.exit(1);
        }
        return {
            dashboard: 15036 + offset,
            otlp: 19200 + offset,
            resource: 20056 + offset,
            source: `KALANDRA_PORT_OFFSET=${offset}`,
        };
    }
    const [dashboard, otlp, resource] = await Promise.all([
        findFreePort(),
        findFreePort(),
        findFreePort(),
    ]);
    return { dashboard, otlp, resource, source: "OS-allocated" };
}

const ports = await resolvePorts();

console.log(`Aspire (${ports.source}):`);
console.log(`  Dashboard:        http://localhost:${ports.dashboard}`);
console.log(`  OTLP exporter:    http://localhost:${ports.otlp}`);
console.log(`  Resource service: http://localhost:${ports.resource}`);
console.log(`  API + frontend:   allocated by Aspire — see dashboard`);

const env = {
    ...process.env,
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
