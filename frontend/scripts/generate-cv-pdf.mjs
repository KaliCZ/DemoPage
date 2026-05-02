import { chromium } from "playwright";
import { pathToFileURL } from "node:url";
import { mkdir } from "node:fs/promises";
import path from "node:path";

const input = process.argv[2] ?? "cv/pavel-kalandra-cv.html";
const output = process.argv[3] ?? "public/cv/pavel-kalandra-cv.pdf";

await mkdir(path.dirname(output), { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage();
await page.goto(pathToFileURL(path.resolve(input)).href, { waitUntil: "networkidle" });
await page.pdf({
  path: output,
  format: "A4",
  printBackground: true,
  margin: { top: "0", right: "0", bottom: "0", left: "0" },
  preferCSSPageSize: true,
});
await browser.close();
console.log("Wrote", output);
