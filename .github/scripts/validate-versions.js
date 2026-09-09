const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');

// 1. Read package.json (single source of truth)
const packageJsonPath = path.join(repoRoot, 'package.json');
if (!fs.existsSync(packageJsonPath)) {
  console.error('Error: package.json not found at ' + packageJsonPath);
  process.exit(1);
}

const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
const pluginVersion = packageJson.version;
if (!pluginVersion) {
  console.error('Error: package.json missing "version" field.');
  process.exit(1);
}

const semverRegex = /^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/;
if (!semverRegex.test(pluginVersion)) {
  console.error(`Error: package.json version '${pluginVersion}' is not valid semantic version.`);
  process.exit(1);
}

console.log(`[Version Check] package.json version: ${pluginVersion}`);

// 2. Validate .github/release-please-manifest.json
const manifestPath = path.join(repoRoot, '.github', 'release-please-manifest.json');
if (fs.existsSync(manifestPath)) {
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  const manifestVersion = manifest['.'];
  if (manifestVersion && manifestVersion !== pluginVersion) {
    console.error(`Error: Release manifest version (${manifestVersion}) does not match package.json (${pluginVersion}).`);
    process.exit(1);
  }
  console.log(`[Version Check] release manifest version matches: ${manifestVersion}`);
}

// 3. If --tag argument is provided, validate git release tag matches package version
const tagIndex = process.argv.indexOf('--tag');
if (tagIndex !== -1 && tagIndex + 1 < process.argv.length) {
  const tag = process.argv[tagIndex + 1];
  const cleanTag = tag.replace(/^v/, '');
  if (cleanTag !== pluginVersion) {
    console.error(`Error: Release tag '${tag}' (${cleanTag}) does not match package.json version (${pluginVersion})!`);
    process.exit(1);
  }
  console.log(`[Version Check] Release tag matches package.json: ${tag} == ${pluginVersion}`);
}

console.log('All version checks passed successfully.');
