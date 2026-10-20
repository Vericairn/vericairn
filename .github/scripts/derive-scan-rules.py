# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

# Derives the secret scan's configuration, src/Vericairn.Adapters.Git/gitleaks.toml, from gitleaks' default rules: each rule
# unchanged, every built-in allowlist removed, then Vericairn's own allowances. For a gitleaks upgrade, read config/gitleaks.toml
# at the release's tag and run:
#   python3 -I .github/scripts/derive-scan-rules.py <config/gitleaks.toml> <version> <tag commit> src/Vericairn.Adapters.Git/gitleaks.toml
# then review each allowance against the new rules, and the scanner's minimum version.
import hashlib, json, re, sys, tomllib
src, version, commit, dst = sys.argv[1:5]
raw = open(src, 'rb').read()
upstream = raw.decode('utf-8')
digest = hashlib.sha256(raw).hexdigest()
c = tomllib.loads(upstream)
body = upstream.split('\n[[rules]]', 1)[1]
out, skip = [], False
for ln in ('[[rules]]' + body).split('\n'):
    h = ln.strip()
    if h in ('[[rules.allowlists]]', '[allowlist]'): skip = True
    elif h == '[[rules]]': skip = False
    if not skip: out.append(ln)
rules = re.sub(r'\n{3,}', '\n\n', '\n'.join(out)).rstrip('\n') + '\n'
by = {r['id']: r for r in c['rules']}
def q(s): assert "'''" not in s; return "'''" + s + "'''"
def arr(v): return '[\n' + ''.join(f'    {q(x)},\n' for x in v) + ']'
g = by['generic-api-key']['allowlists']
assert g[0]['regexes'] == ['^[a-zA-Z_.-]+$'] and g[1].get('regexTarget') == 'match' and g[2].get('regexTarget') == 'line' and 'paths' in g[3]
curl = by['curl-auth-user']['allowlists'][0]['regexes']; assert curl[0].startswith('[^:]+:(?:change')
k8s = by['kubernetes-secret-yaml']['allowlists']
nuget = by['nuget-config-password']['allowlists'][0]['regexes']; assert nuget[2] == r'^\%\S.*\%$'
vault = by['vault-service-token']['allowlists'][0]['regexes']
allow = []
def block(desc, rules_, regexes, target=None, stop=None):
    s = f'[[allowlists]]\ndescription = "{desc}"\n'
    if rules_: s += 'targetRules = ' + json.dumps(rules_) + '\n'
    if target: s += f'regexTarget = "{target}"\n'
    s += 'regexes = ' + arr(regexes) + '\n'
    if stop: s += 'stopwords = [\n' + ''.join(f'    {json.dumps(w)},\n' for w in stop) + ']\n'
    allow.append(s)
block("Placeholders, template references, booleans, repeated characters, format specifiers and file paths", None, c['allowlist']['regexes'])
block("Generic keys: a value without a digit", ['generic-api-key'], g[0]['regexes'])
block("Generic keys: key, token and secret words that name no credential, and common words", ['generic-api-key'], g[1]['regexes'], 'match', g[1]['stopwords'])
block("Generic keys: build secret mounts and module imports", ['generic-api-key'], g[2]['regexes'], 'line')
block("Basic authorization in curl: placeholders and variable references", ['curl-auth-user'], curl[1:])
block("Kubernetes secrets: template or empty values", ['kubernetes-secret-yaml'], k8s[0]['regexes'])
block("Kubernetes secrets: kind and data in separate documents", ['kubernetes-secret-yaml'], k8s[1]['regexes'], 'match')
block("NuGet configuration passwords: environment variable references", ['nuget-config-password'], [nuget[2]])
block("Vault service tokens: a value without a digit", ['vault-service-token'], vault)
header = f'''# Vericairn's secret-scanning configuration for `vericairn changes push`.
#
# The rules are gitleaks' default rules: config/gitleaks.toml of gitleaks v{version} (commit
# {commit}, SHA-256 {digest}),
# Copyright (c) 2019 Zachary Rice, under the MIT License in licenses/MIT-gitleaks.txt. Each rule is unchanged, except that
# every built-in allowlist is removed: the global allowlist with its path exemptions, and each rule's own.
# .github/scripts/derive-scan-rules.py derives this file.
#
# The allowances after the rules are the only ones. Each describes a value that is not the credential its rule finds;
# none exempts a path, and none admits a sample credential such as an AWS key id ending in EXAMPLE. docs/changes.md
# lists them, and each scan records this file's SHA-256.

title = "Vericairn secret scan"
minVersion = "v8.25.0"

'''
text = header + rules + '\n# Vericairn\'s allowances.\n\n' + '\n'.join(allow)
parsed = tomllib.loads(text)
assert len(parsed['rules']) == len(c['rules'])
for a, b in zip(c['rules'], parsed['rules']):
    a = {k: v for k, v in a.items() if k != 'allowlists'}; assert a == b, a['id']
assert all('allowlists' not in r for r in parsed['rules']) and 'allowlist' not in parsed
assert all('paths' not in a for a in parsed['allowlists'])
open(dst, 'w', encoding='utf-8', newline='\n').write(text)
print(len(parsed['rules']), 'rules;', len(parsed['allowlists']), 'allowances;', len(text.encode()), 'bytes')
