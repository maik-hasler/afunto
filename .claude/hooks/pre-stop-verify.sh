#!/bin/bash

INPUT=$(cat)
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

export PATH="$HOME/.dotnet:$PATH"

SESSION_ID=$(printf '%s' "$INPUT" | grep -o '"session_id"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -E 's/.*:[[:space:]]*"(.*)"$/\1/')
SESSION_ID="${SESSION_ID:-unknown}"
COUNTER_FILE="/tmp/claude-stop-verify-count-${SESSION_ID}"
COUNT=$(cat "$COUNTER_FILE" 2>/dev/null || echo 0)

if [ "$COUNT" -ge 2 ] 2>/dev/null; then
	exit 0
fi

# Diff against the branch point, not HEAD: AGENTS.md mandates committing to a
# claude/... branch, so a HEAD-only diff goes empty the moment work is committed
# - exactly the flow this hook exists to guard. Untracked files are invisible to
# git diff entirely, so they are unioned in separately.
BASE=$(git merge-base HEAD origin/main 2>/dev/null || echo HEAD)
CHANGED=$(
	{
		git diff --name-only "$BASE" -- backend/src frontend/src
		git ls-files --others --exclude-standard -- backend/src frontend/src
	} 2>/dev/null | sort -u
)
if [ -z "$CHANGED" ]; then
	exit 0
fi

FAILED=""
LOG="/tmp/claude-stop-verify-${SESSION_ID}.log"
: > "$LOG"

# Build the test projects, not just Api.csproj: a src-side signature change
# that compiles fine on its own but breaks a test project used to pass here and
# fail in CI (jobs 94682968957, 98147126933, 98429804385). IntegrationTests
# references Api, AppHost and Infrastructure, so these three cover all of src
# plus every test project except VisualTests. VisualTests is deliberately left
# out: its InstallPlaywrightBrowsers target (VisualTests.csproj:19) runs
# `playwright install --with-deps chromium` after every build, which is not
# something to put in the path of ending a turn. ~12s warm for all three.
if printf '%s\n' "$CHANGED" | grep -q '^backend/src'; then
	for proj in \
		backend/tests/IntegrationTests/IntegrationTests.csproj \
		backend/tests/ArchitectureTests/ArchitectureTests.csproj \
		backend/tests/Application.UnitTests/Application.UnitTests.csproj; do
		if ! dotnet build "$proj" --configuration Debug --verbosity quiet >>"$LOG" 2>&1; then
			FAILED="backend build ($(basename "$proj" .csproj))"
			break
		fi
	done
fi

if [ -z "$FAILED" ] && printf '%s\n' "$CHANGED" | grep -q '^frontend/src'; then
	if ! (cd frontend && pnpm lint >>"$LOG" 2>&1 && pnpm check >>"$LOG" 2>&1); then
		FAILED="frontend lint/typecheck"
	fi
fi

# editorconfig is the repo's most-failing non-Docker CI job (57 confirmed
# violations across 44 branches, most of them on claude/* branches) and the only
# gate nothing local mirrors: Prettier passes the continuation lines it rejects.
# Scoped to $CHANGED so pre-existing debt elsewhere can never wedge a turn, and
# it fails open when the download is blocked. Keep the version and checksum in
# step with EC_VERSION/EC_SHA256 in .github/workflows/lint.yml.
if [ -z "$FAILED" ]; then
	EC_VERSION="v4.0.1"
	EC_SHA256="90139c6ed52373c0acfc9deb2a07aa812e5184753afd4bc8252bf44eb4909155"
	EC_BIN="$HOME/.cache/afunto/editorconfig-checker-${EC_VERSION}"
	if [ ! -x "$EC_BIN" ]; then
		mkdir -p "$(dirname "$EC_BIN")"
		EC_TMP=$(mktemp -d)
		if curl -sSfL -o "$EC_TMP/ec.tgz" \
			"https://github.com/editorconfig-checker/editorconfig-checker/releases/download/${EC_VERSION}/editorconfig-checker-linux-amd64.tar.gz" &&
			echo "$EC_SHA256  $EC_TMP/ec.tgz" | sha256sum --check --strict >/dev/null 2>&1 &&
			tar xzf "$EC_TMP/ec.tgz" -C "$EC_TMP" editorconfig-checker; then
			mv "$EC_TMP/editorconfig-checker" "$EC_BIN"
		fi
		rm -rf "$EC_TMP"
	fi
	if [ -x "$EC_BIN" ] &&
		! printf '%s\n' "$CHANGED" | xargs -r "$EC_BIN" -config .editorconfig-checker.json >>"$LOG" 2>&1; then
		FAILED="editorconfig"
	fi
fi

if [ -n "$FAILED" ]; then
	echo "$((COUNT + 1))" > "$COUNTER_FILE"
	REASON="$FAILED failed - see $LOG for details. Fix it before finishing. (checked at most twice per session)"
	python3 -c "import json,sys; print(json.dumps({'decision': 'block', 'reason': sys.argv[1]}))" "$REASON"
	exit 0
fi

exit 0
