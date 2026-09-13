// Self-contained Node verification: no graphics packages, network, writes or deploy.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateSync } from 'node:zlib';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../ArenaMode.Client/Assets/MapPreviews');
const manifest = JSON.parse(await readFile(resolve(root, 'MAP-SOURCES.json'), 'utf8'));
const sourcePath = 'Source/Customs-Ground_Level.svg';
const licensePath = 'Source/Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md';
const texturePath = 'customs-ground.png';
const paths = [sourcePath, licensePath, texturePath];
const files = new Map(await Promise.all(paths.map(async path => [path, await readFile(resolve(root, path))])));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase();

function inspectPng(bytes) {
  assert(bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), 'PNG signature');
  assert.equal(bytes.toString('ascii', 12, 16), 'IHDR', 'PNG IHDR');
  assert.equal(bytes.readUInt32BE(8), 13, 'PNG IHDR length');
  assert.equal(bytes.readUInt32BE(16), 2125, 'PNG width');
  assert.equal(bytes.readUInt32BE(20), 1070, 'PNG height');
  assert.equal(bytes[24], 8, 'PNG bit depth');
  assert.equal(bytes[25], 2, 'PNG must be opaque RGB');
  assert.equal(bytes[26], 0, 'PNG compression');
  assert.equal(bytes[27], 0, 'PNG filter method');
  assert.equal(bytes[28], 0, 'PNG interlace');
  const data = [];
  let end = false;
  for (let offset = 8; offset < bytes.length;) {
    assert(offset + 12 <= bytes.length, 'PNG chunk header');
    const length = bytes.readUInt32BE(offset);
    assert(offset + length + 12 <= bytes.length, 'PNG chunk length');
    const type = bytes.toString('ascii', offset + 4, offset + 8);
    if (type === 'IDAT') data.push(bytes.subarray(offset + 8, offset + 8 + length));
    if (type === 'IEND') {
      assert.equal(length, 0, 'PNG IEND length');
      assert.equal(offset + 12, bytes.length, 'No appended PNG payload');
      end = true;
    }
    offset += length + 12;
  }
  assert(end && data.length > 0, 'PNG must contain image data and IEND');
  const width = 2125;
  const height = 1070;
  const stride = width * 3;
  const raw = inflateSync(Buffer.concat(data), { maxOutputLength: (stride + 1) * height });
  assert.equal(raw.length, (stride + 1) * height, 'PNG decompressed scanline length');
  const pixels = Buffer.alloc(stride * height);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    assert(filter <= 4, 'PNG row filter');
    for (let x = 0; x < stride; x++) {
      const i = y * stride + x;
      const a = x >= 3 ? pixels[i - 3] : 0;
      const b = y > 0 ? pixels[i - stride] : 0;
      const c = x >= 3 && y > 0 ? pixels[i - stride - 3] : 0;
      let predictor = 0;
      if (filter === 1) predictor = a;
      if (filter === 2) predictor = b;
      if (filter === 3) predictor = Math.floor((a + b) / 2);
      if (filter === 4) {
        const p = a + b - c;
        const pa = Math.abs(p - a);
        const pb = Math.abs(p - b);
        const pc = Math.abs(p - c);
        predictor = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      pixels[i] = raw[y * (stride + 1) + 1 + x] + predictor;
    }
  }
  inspectPixels(pixels);
}

function inspectPixels(pixels) {
  let minimum = 255;
  let maximum = 0;
  let varied = 0;
  for (let offset = 0; offset < pixels.length; offset += 3) {
    const gray = pixels[offset];
    assert(gray === pixels[offset + 1] && gray === pixels[offset + 2], 'PNG pixels must be grayscale');
    minimum = Math.min(minimum, gray);
    maximum = Math.max(maximum, gray);
    if (gray > 32 && gray < 200) varied++;
  }
  assert.equal(pixels[0], 20, 'PNG dark background');
  // librsvg's alpha edges may flatten one level below the #141414 background.
  assert(minimum >= 19, 'PNG dark edge range');
  assert(maximum >= 150 && maximum <= 200, 'PNG legible geometry range');
  assert(varied > 100000, 'PNG cannot be an empty dark rectangle');
}

function verifyMetadata(record) {
  assert.equal(record.schemaVersion, 1);
  assert.equal(record.mapId, 'bigmap');
  assert.equal(record.source.file, sourcePath);
  assert.equal(record.license.file, licensePath);
  assert.equal(record.texture.file, texturePath);
  assert.equal(record.license.spdx, 'CC-BY-NC-SA-4.0', 'Artwork retains non-commercial share-alike license');
  assert.equal(record.license.url, 'https://creativecommons.org/licenses/by-nc-sa/4.0/');
  assert.deepEqual(record.license.appliesTo, [sourcePath, texturePath]);
  assert(record.attribution.includes('Shebuka') && record.attribution.includes('Tarkov.dev')
    && record.attribution.includes('acidphantasm') && record.attribution.includes('maschine'), 'Artwork attribution');
  assert.equal(record.source.preservedUnchanged, true);
  assert.deepEqual(record.source.viewBox, [0, 0, 1062.48, 535.17]);
  assert.equal(record.texture.width, 2125);
  assert.equal(record.texture.height, 1070);
  assert.equal(record.texture.channels, 'RGB');
  assert.equal(record.texture.grayscale, true);
  assert.equal(record.calibration.coordinateRotationDegrees, 180);
  assert.deepEqual(record.calibration.worldBounds, { minX: -372, maxX: 698, minZ: -306, maxZ: 235 });
  assert.deepEqual(record.calibration.svgViewBox, record.source.viewBox);
  assert.equal(record.calibration.worldToImageU, '(698 - worldX) / 1070', 'Calibration image U');
  assert.equal(record.calibration.worldToImageV, '(worldZ + 306) / 541', 'Calibration image V');
  assert.equal(record.calibration.worldToUnityTextureV, '1 - (worldZ + 306) / 541', 'Calibration texture V');
}

function verifyFiles(record, buffers) {
  verifyMetadata(record);
  for (const [name, entry] of [['source', record.source], ['license', record.license], ['texture', record.texture]]) {
    const bytes = buffers.get(entry.file);
    assert.equal(bytes.length, entry.bytes, `${name} byte length`);
    assert.equal(sha256(bytes), entry.sha256, `${name} SHA-256`);
  }
  // Pin intake identities independently of the manifest; a source/license update
  // is a deliberate review, not a metadata-only overwrite of a hash.
  assert.equal(record.source.sha256, '5BE31C29173854F5F832C3FEA072BFD8603E27EECA2052754F3798CBF1B6B6D9');
  assert.equal(record.license.sha256, '1349A4B6148492B44F629E64EED676612E234FE9A839E4F3B277C1482C8849F1');
  assert(buffers.get(licensePath).toString('utf8').includes('Attribution-NonCommercial-ShareAlike 4.0 International'));
  const svg = buffers.get(sourcePath).toString('utf8');
  assert(svg.includes('viewBox="0 0 1062.48 535.17"'));
  assert(!/<(?:script|foreignObject|text|image)\b|(?:href|xlink:href)\s*=|url\s*\(/i.test(svg), 'Self-contained vector source');
  inspectPng(buffers.get(texturePath));
}

verifyFiles(manifest, files);
let rejected = 0;
for (const [path, label] of [[sourcePath, 'source'], [licensePath, 'license'], [texturePath, 'texture']]) {
  const changed = new Map(files);
  const bytes = Buffer.from(changed.get(path));
  bytes[Math.floor(bytes.length / 2)] ^= 1;
  changed.set(path, bytes);
  assert.throws(() => verifyFiles(manifest, changed), new RegExp(`${label} SHA-256`));
  rejected++;
}
const wrongWidth = Buffer.from(files.get(texturePath));
wrongWidth.writeUInt32BE(2124, 16);
assert.throws(() => inspectPng(wrongWidth), /PNG width/);
rejected++;
const wrongProjection = structuredClone(manifest);
wrongProjection.calibration.worldToImageV = '(235 - worldZ) / 541';
assert.throws(() => verifyMetadata(wrongProjection), /Calibration image V/);
rejected++;
const coloredPixel = Buffer.from([40, 41, 40]);
assert.throws(() => inspectPixels(coloredPixel), /PNG pixels must be grayscale/);
rejected++;
console.log(`AREA MAP ASSETS PASSED: ${manifest.texture.width}x${manifest.texture.height} RGB, `
  + `${manifest.texture.bytes} bytes; source/license/texture hashes, full PNG decode, calibration and ${rejected} in-memory negative controls.`);
