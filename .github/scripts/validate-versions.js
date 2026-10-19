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

// 4. Validate CLI compatibility bounds in Editor/Core/FlexVaultVersionGuard.cs
const guardPath = path.join(repoRoot, 'Editor', 'Core', 'FlexVaultVersionGuard.cs');
if (!fs.existsSync(guardPath)) {
  console.error('Error: FlexVaultVersionGuard.cs not found at ' + guardPath);
  process.exit(1);
}
const guardContent = fs.readFileSync(guardPath, 'utf8');

const minVersionMatch = guardContent.match(/MinVersion\s*=\s*new\s+FxvCliVersion\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)/);
const maxVersionMatch = guardContent.match(/MaxVersion\s*=\s*new\s+FxvCliVersion\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)/);

if (!minVersionMatch || !maxVersionMatch) {
  console.error('Error: Could not parse MinVersion or MaxVersion from ' + guardPath);
  process.exit(1);
}

const cliMin = `${minVersionMatch[1]}.${minVersionMatch[2]}.${minVersionMatch[3]}`;
const cliMax = `${maxVersionMatch[1]}.${maxVersionMatch[2]}.${maxVersionMatch[3]}`;
if (!semverRegex.test(cliMin) || !semverRegex.test(cliMax)) {
  console.error(`Error: Parsed CLI compatibility bounds ('${cliMin}', '${cliMax}') are not valid semver.`);
  process.exit(1);
}

const minParts = [parseInt(minVersionMatch[1], 10), parseInt(minVersionMatch[2], 10), parseInt(minVersionMatch[3], 10)];
const maxParts = [parseInt(maxVersionMatch[1], 10), parseInt(maxVersionMatch[2], 10), parseInt(maxVersionMatch[3], 10)];
const minNum = minParts[0] * 1000000 + minParts[1] * 1000 + minParts[2];
const maxNum = maxParts[0] * 1000000 + maxParts[1] * 1000 + maxParts[2];
if (minNum >= maxNum) {
  console.error(`Error: MinVersion (${cliMin}) must be strictly less than MaxVersion (${cliMax}).`);
  process.exit(1);
}

console.log(`[Version Check] FlexVaultVersionGuard CLI compatibility bounds: >= ${cliMin}, < ${cliMax}`);

console.log('All version checks passed successfully.');
