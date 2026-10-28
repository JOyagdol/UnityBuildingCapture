"""Check the staged source-only package. Does not print matched secrets or data."""
import argparse
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
ALLOWED = re.compile(
    r'^(?:\.gitignore|\.gitattributes|README\.md(?:\.meta)?|package\.json(?:\.meta)?|'
    r'(?:Runtime|Editor)\.meta|(?:Runtime|Editor)/[A-Za-z0-9_.]+\.(?:cs|asmdef)(?:\.meta)?|'
    r'Tools~/audit_release\.py|Tests~/[A-Za-z0-9_]+\.cs)$'
)
BLOCKED_CONTENT = [
    ('site identifier', re.compile(r'Smart[ _]?City[ _]?Lab', re.IGNORECASE)),
    ('local dataset or user path', re.compile(r'[A-Za-z]:[\\/](?:Users|3dtiles)[\\/]', re.IGNORECASE)),
    ('private key', re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')),
    ('access token', re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,})')),
    ('serialized location', re.compile(r'^\s*(?:_latitude|_longitude|_ecefX|_ecefY|_ecefZ):\s*[-\d]', re.MULTILINE)),
]


def git(*args):
    return subprocess.run(['git', '-C', str(ROOT), *args], check=True, capture_output=True).stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--staged', action='store_true', required=True,
                        help='Inspect the index that will become the next commit.')
    parser.parse_args()
    entries = git('ls-files', '--stage', '-z').decode('utf-8').split('\0')
    errors, files, guids = [], {}, set()
    for entry in filter(None, entries):
        info, name = entry.split('\t', 1)
        mode, oid, stage = info.split()
        if mode not in ('100644', '100755') or stage != '0':
            errors.append(f'{name}: non-regular or unmerged file')
            continue
        if not ALLOWED.fullmatch(name):
            errors.append(f'{name}: outside source-only allowlist')
            continue
        payload = git('cat-file', 'blob', oid)
        if len(payload) > 1024 * 1024:
            errors.append(f'{name}: larger than 1 MiB source limit')
            continue
        try:
            text = payload.decode('utf-8-sig')
        except UnicodeDecodeError:
            errors.append(f'{name}: binary/non-UTF-8 content')
            continue
        if '\0' in text:
            errors.append(f'{name}: binary content')
        for label, pattern in BLOCKED_CONTENT:
            if pattern.search(text):
                errors.append(f'{name}: {label} detected (value withheld)')
        if name.endswith(('.json', '.asmdef')):
            try:
                json.loads(text)
            except ValueError:
                errors.append(f'{name}: invalid JSON')
        if name.endswith('.meta'):
            match = re.search(r'^guid: ([0-9a-f]{32})$', text, re.MULTILINE)
            if not match or match.group(1) in guids:
                errors.append(f'{name}: missing/duplicate Unity GUID')
            elif match:
                guids.add(match.group(1))
        files[name] = len(payload)
    for required in ('README.md', '.gitignore', '.gitattributes', 'package.json',
                     'Runtime/ResearchWalker.cs', 'Runtime/ResearchCaptureWriter.cs',
                     'Editor/ResearchCaptureSetup.cs'):
        if required not in files:
            errors.append(f'{required}: required file missing')
    for path in ('Assets/Scenes/private.unity', 'Runtime/private.asset', 'Runtime/model.fbx',
                 'Tiles/tileset.json', 'captures/images/private.png', 'ground_truth/private.json',
                 'Builds/Application.exe', 'Library/cache', '.env', 'private.log'):
        result = subprocess.run(['git', '-C', str(ROOT), 'check-ignore', '--no-index', '-q', path],
                                capture_output=True)
        if result.returncode != 0:
            errors.append(f'{path}: ignore rule does not block this data category')
    print(json.dumps({'status': 'fail' if errors else 'pass', 'staged_files': len(files),
                      'total_bytes': sum(files.values()), 'errors': errors}, ensure_ascii=False, indent=2))
    return bool(errors)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except subprocess.CalledProcessError:
        sys.exit('Git inspection failed. Run from an initialized repository with Git available.')
