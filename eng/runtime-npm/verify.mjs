import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const checks = [
  {
    consumer: "npm/node_modules/minimatch/package.json",
    dependency: "brace-expansion",
    version: "5.0.9",
  },
  {
    consumer: "npm/node_modules/socks/package.json",
    dependency: "ip-address",
    version: "10.3.1",
  },
];

for (const check of checks) {
  const consumer = new URL(`./node_modules/${check.consumer}`, import.meta.url);
  const dependencyPackage = createRequire(consumer).resolve(
    `${check.dependency}/package.json`,
  );
  const expectedPackage = fileURLToPath(
    new URL(`./node_modules/${check.dependency}/package.json`, import.meta.url),
  );
  const metadata = JSON.parse(readFileSync(dependencyPackage, "utf8"));

  assert.equal(dependencyPackage, expectedPackage);
  assert.equal(metadata.version, check.version);
  process.stdout.write(`${check.dependency}@${metadata.version}\n`);
}
