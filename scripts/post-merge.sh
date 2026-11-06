#!/bin/bash
set -e
# Keep post-merge setup offline-friendly and non-mutating; schema changes are
# not part of package reconciliation and must never push to a database here.
pnpm install --frozen-lockfile --prefer-offline
