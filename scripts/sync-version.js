#!/usr/bin/env node
/**
 * Single source of truth: version.json + git commit count.
 * Produces version.generated.json and patches native app + server metadata.
 *
 * Version format (git): MAJOR.MINOR.COMMIT_COUNT+shortHash  (e.g. 1.0.42+a1b2c3d)
 * Shown in the handheld app as AppConfig.AppVersion; server uses version.generated.json.
 * .NET AssemblyVersion: MAJOR.MINOR.COMMIT_COUNT.0
 */
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const repoRoot = path.join(__dirname, '..');

function runGit(args, fallback) {
    try {
        return execSync(`git ${args}`, { cwd: repoRoot, encoding: 'utf8' }).trim();
    } catch {
        return fallback;
    }
}

function readJson(filePath) {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function writeJson(filePath, data) {
    fs.writeFileSync(filePath, JSON.stringify(data, null, 2) + '\n', 'utf8');
}

function patchFile(filePath, replacements) {
    let text = fs.readFileSync(filePath, 'utf8');
    let changed = false;
    for (const [pattern, replacement] of replacements) {
        const next = text.replace(pattern, replacement);
        if (next !== text) {
            text = next;
            changed = true;
        }
    }
    if (changed) {
        fs.writeFileSync(filePath, text, 'utf8');
    }
    return changed;
}

const base = readJson(path.join(repoRoot, 'version.json'));
const commitCount = parseInt(runGit('rev-list --count HEAD', '0'), 10) || 0;
const gitHash = runGit('rev-parse --short HEAD', 'dev');
const version = `${base.major}.${base.minor}.${commitCount}+${gitHash}`;
const assemblyVersion = `${base.major}.${base.minor}.${commitCount}.0`;

const generated = {
    name: base.name,
    version,
    assemblyVersion,
    major: base.major,
    minor: base.minor,
    patch: commitCount,
    gitCommit: gitHash,
    gitCommitCount: commitCount,
    builtAt: new Date().toISOString(),
};

const outPath = path.join(repoRoot, 'version.generated.json');
writeJson(outPath, generated);
writeJson(path.join(repoRoot, 'public', 'version.generated.json'), generated);

const appConfigPath = path.join(repoRoot, 'handheld-ce', 'MerlinInventoryTest', 'AppConfig.cs');
patchFile(appConfigPath, [
    [/public const string AppVersion = "[^"]*";/, `public const string AppVersion = "${version}";`],
]);

const assemblyPath = path.join(
    repoRoot,
    'handheld-ce',
    'MerlinInventoryTest',
    'Properties',
    'AssemblyInfo.cs'
);
patchFile(assemblyPath, [
    [/\[assembly: AssemblyVersion\("[^"]*"\)\]/, `[assembly: AssemblyVersion("${assemblyVersion}")]`],
]);

console.log(`Version synced: ${version} (assembly ${assemblyVersion})`);
console.log(`  → ${path.relative(repoRoot, outPath)}`);

module.exports = generated;
