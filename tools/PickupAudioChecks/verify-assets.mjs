import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
import {amplify} from './amplify.mjs';
const dir = new URL('../../ArenaMode.Client/Assets/ArenaMode/pickups/', import.meta.url);
const manifest=JSON.parse(readFileSync(new URL('SOURCES.json',dir)));
const wav=readFileSync(new URL(manifest.file,dir));
const hash=b=>createHash('sha256').update(b).digest('hex').toUpperCase();
assert.equal(hash(wav),manifest.sha256);
assert.equal(hash(readFileSync(new URL(manifest.licenseFile,dir))),manifest.licenseSha256);
assert.equal(manifest.license,'CC0-1.0');
assert.equal(wav.toString('ascii',0,4),'RIFF'); assert.equal(wav.readUInt32LE(4),wav.length-8);
assert.equal(wav.toString('ascii',8,12),'WAVE');
let fmt, pcm;
for(let i=12;i<wav.length;){ const n=wav.readUInt32LE(i+4);const tag=wav.toString('ascii',i,i+4);assert(i+8+n<=wav.length);if(tag==='fmt ')fmt=wav.subarray(i+8,i+8+n);if(tag==='data')pcm=wav.subarray(i+8,i+8+n);i+=8+n+(n&1);}
assert.equal(fmt.readUInt16LE(0),1);assert.equal(fmt.readUInt16LE(2),1);assert.equal(fmt.readUInt32LE(4),44100);assert.equal(fmt.readUInt16LE(14),16);
assert.equal(pcm.length/2,manifest.frames);assert(pcm.length/2<=44100);
let peak=0,squares=0;for(let i=0;i<pcm.length;i+=2){const x=pcm.readInt16LE(i);peak=Math.max(peak,Math.abs(x));squares+=x*x;}
assert(peak>500&&peak<32767);assert.equal(pcm.readInt16LE(pcm.length-2),0);
const rms=Math.sqrt(squares/(pcm.length/2));
assert.equal(manifest.gainDb,12);
assert.equal(peak,26207);assert(Math.abs(rms-1748.2071957095827)<.001);
// Reject the previous quiet asset independently of its manifest hash.
assert(20*Math.log10(rms/439.1295327814409)>11.99,'pickup must retain +12 dB over the converted source');
assert.deepEqual(amplify(wav,0),wav,'zero-gain preserves the full RIFF layout');
assert.throws(()=>amplify(wav,12),/clip/,'a second gain pass must refuse clipping');
console.log('PICKUP_AUDIO_ASSETS_PASSED');
