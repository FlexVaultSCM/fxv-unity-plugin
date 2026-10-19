const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');

// 1. Read package version from package.json
const packageJsonPath = path.join(repoRoot, 'package.json');
if (!fs.existsSync(packageJsonPath)) {
  console.error('Error: package.json not found at ' + packageJsonPath);
  process.exit(1);
}
const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
const pluginVersion = packageJson.version;
if (!pluginVersion) {
  console.error('Error: missing version in ' + packageJsonPath);
  process.exit(1);
}

// 2. Read CLI compatibility range from Editor/Core/FlexVaultVersionGuard.cs
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

const minParts = [parseInt(minVersionMatch[1], 10), parseInt(minVersionMatch[2], 10), parseInt(minVersionMatch[3], 10)];
const maxParts = [parseInt(maxVersionMatch[1], 10), parseInt(maxVersionMatch[2], 10), parseInt(maxVersionMatch[3], 10)];
const minNum = minParts[0] * 1000000 + minParts[1] * 1000 + minParts[2];
const maxNum = maxParts[0] * 1000000 + maxParts[1] * 1000 + maxParts[2];
if (minNum >= maxNum) {
  console.error(`Error: MinVersion (${cliMin}) must be strictly less than MaxVersion (${cliMax}).`);
  process.exit(1);
}

// 3. Resolve tag and clean version
let tag = process.env.RELEASE_TAG || `v${pluginVersion}`;
const tagArgIndex = process.argv.indexOf('--tag');
if (tagArgIndex !== -1 && tagArgIndex + 1 < process.argv.length) {
  tag = process.argv[tagArgIndex + 1];
}
const cleanVersion = tag.replace(/^v/, '');

const outputPath = path.join(repoRoot, 'version.json');
const manifest = {
  name: 'unity',
  plugin_version: cleanVersion,
  tag: tag,
  cli_compatibility: {
    min: cliMin,
    max: cliMax
  },
  assets: {
    package_tgz: `com.flexvault.vcs-${cleanVersion}.tgz`,
    package_tgz_latest: 'com.flexvault.vcs.tgz',
    plugin_zip: `com.flexvault.vcs-${cleanVersion}.zip`
  }
};

fs.writeFileSync(outputPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
console.log(`Generated ${outputPath}:`);
console.log(JSON.stringify(manifest, null, 2));
