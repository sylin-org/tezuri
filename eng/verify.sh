#!/usr/bin/env sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repo_root"

dotnet restore Tezuri.slnx
(
  cd src/Tezuri.App/ClientApp
  trap 'rm -rf .verification-dist' EXIT
  npm ci --no-audit --no-fund
  npm test
  npm run check
  npx vite build --outDir=.verification-dist
)
dotnet format Tezuri.slnx --verify-no-changes --no-restore
dotnet build Tezuri.slnx --configuration Release --no-restore
dotnet test Tezuri.slnx --configuration Release --no-build --no-restore
node eng/verify-repository.mjs
git diff --check

