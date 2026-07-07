#!/usr/bin/env bash
set -euo pipefail

echo "=== ai-memory MCP Setup ==="

TOOL_PATH="${HOME}/.dotnet/tools/ai-memory"
CONFIG='{"command":"ai-memory","args":["mcp"]}'

mkdir -p "${HOME}/.config/claude"
CONFIG_FILE="${HOME}/.config/claude/mcp.json"
if [ -f "$CONFIG_FILE" ]; then
    python3 -c "
import json
with open('$CONFIG_FILE') as f:
    s = json.load(f)
s.setdefault('mcpServers', {})['ai-memory'] = json.loads('''$CONFIG''')
with open('$CONFIG_FILE', 'w') as f:
    json.dump(s, f, indent=2)
" 2>/dev/null || true
else
    echo "{\"mcpServers\":{\"ai-memory\":$CONFIG}}" > "$CONFIG_FILE"
fi
echo "OK: Cursor / Claude Desktop / Codex"

if [ -f "${HOME}/.config/Antigravity/User/settings.json" ]; then
    python3 -c "
import json
with open('${HOME}/.config/Antigravity/User/settings.json') as f:
    s = json.load(f)
s.setdefault('mcpServers', {})['ai-memory'] = {'command': 'ai-memory', 'args': ['mcp']}
with open('${HOME}/.config/Antigravity/User/settings.json', 'w') as f:
    json.dump(s, f, indent=2)
" 2>/dev/null || true
    echo "OK: Antigravity / VS Code"
fi

if [ -d "${HOME}/.cursor" ]; then
    mkdir -p "${HOME}/.cursor"
    echo "{\"mcpServers\":{\"ai-memory\":$CONFIG}}" > "${HOME}/.cursor/mcp.json"
    echo "OK: Cursor"
fi

if command -v opencode &>/dev/null; then
    echo "OK: opencode (configured in ~/.config/opencode/opencode.jsonc)"
fi

echo ""
echo "=== Done ==="
echo "To test: ai-memory mcp"
echo "Send: {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"
