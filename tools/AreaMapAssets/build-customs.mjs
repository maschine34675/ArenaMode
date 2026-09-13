// Offline conversion only. Never reads or writes an installed SPT directory.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
// Pass an existing sharp package directory as --sharp <path> when it is not
// available in node_modules. No install, network, or deployment is performed.
const args = process.argv.slice(2);
assert(args.length === 0 || (args.length === 2 && args[0] === '--sharp'),
  'Usage: node build-customs.mjs [--sharp <installed sharp package directory>]');
const sharp = require(args[1] ? resolve(args[1]) : 'sharp');
assert.equal(sharp.versions.sharp, '0.35.4', 'Reviewed conversion uses sharp 0.35.4');
assert.equal(sharp.versions.rsvg, '2.62.91', 'Reviewed conversion uses librsvg 2.62.91');

const assetRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../ArenaMode.Client/Assets/MapPreviews');
const source = await readFile(resolve(assetRoot, 'Source/Customs-Ground_Level.svg'));
const hash = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase();
assert.equal(hash(source), '5BE31C29173854F5F832C3FEA072BFD8603E27EECA2052754F3798CBF1B6B6D9',
  'Source drift requires a fresh provenance/calibration review');
let svg = source.toString('utf8');
assert(svg.includes('viewBox="0 0 1062.48 535.17"'), 'Unexpected source viewBox');
assert(!/<(?:script|foreignObject|text|image)\b|(?:href|xlink:href)\s*=|url\s*\(/i.test(svg),
  'Only reviewed self-contained vector geometry is accepted');

// The source layer contains one pre-existing sniper hazard polygon. Keep the
// source unchanged, but omit that annotation from our marker-free base texture.
const sniper = /<g id="Sniper">\s*<path\b[^>]*\/>\s*<\/g>/g;
assert.equal([...svg.matchAll(sniper)].length, 1, 'Expected exactly one sniper annotation');
svg = svg.replace(sniper, '');

// Explicit palette keeps buildings, interior floors, roads and fences legible
// behind a blue area overlay. No geometry, rotation or crop is changed.
const palette = new Map(Object.entries({
  '#1f5054': '#303030', // ground
  '#144043': '#242424', // trees
  '#4a6b96': '#454545', // water
  '#946d3e': '#545454', // dirt roads
  '#c6c2c2': '#555555', // paved yards
  '#888': '#727272',    // asphalt roads
  '#914833': '#999999', // railway
  '#000': '#aaaaaa',    // fences and existing map limit
  '#c4e3c3': '#aaaaaa', // wire fences
  '#1a2632': '#969696', // building outline / walls
  '#dcd5b6': '#424242', // rocks, three source variants
  '#ddd6b6': '#424242',
  '#ded6b6': '#424242',
  '#ffce00': '#626262', // power lines
  '#37414c': '#494949', // interior floors
  '#70777f': '#686868', // interior partitions
  'gold': '#9c9c9c'     // stairs / ladders
}));
svg = svg.replace(/((?:fill|stroke):\s*)(#[0-9a-f]+|[a-z]+)(?=;)/gi, (_, property, color) => {
  if (color === 'none') return property + color;
  assert(palette.has(color.toLowerCase()), `Unreviewed SVG color ${color}`);
  return property + palette.get(color.toLowerCase());
});

// Integer pixel dimensions are rounded from a 2x render (2124.96 x 1070.34).
// Explicit full-viewBox rescaling avoids hidden letterbox/crop offsets: the
// documented UV projection addresses the complete PNG in both axes.
svg = svg.replace('viewBox="0 0 1062.48 535.17"',
  'viewBox="0 0 1062.48 535.17" width="2125" height="1070" preserveAspectRatio="none"');
const png = await sharp(Buffer.from(svg))
  .flatten({ background: '#141414' })
  .removeAlpha()
  .png({ compressionLevel: 9, adaptiveFiltering: false, palette: false })
  .toBuffer();
const metadata = await sharp(png).metadata();
assert.equal(metadata.width, 2125);
assert.equal(metadata.height, 1070);
assert.equal(metadata.hasAlpha, false);
const file = resolve(assetRoot, 'customs-ground.png');
await writeFile(file, png);
console.log(JSON.stringify({ file, bytes: png.length, sha256: hash(png), width: metadata.width,
  height: metadata.height, versions: sharp.versions }, null, 2));
