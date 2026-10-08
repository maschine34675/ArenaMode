// Amplify an already converted PCM16 WAV. Refuse clipping; leave its layout intact.
// Usage: node amplify.mjs input.wav output.wav gainDb
import {readFileSync,writeFileSync} from 'node:fs';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
export function amplify(input, gainDb) {
  assert(Number.isFinite(gainDb) && gainDb >= 0 && gainDb <= 24);
  const wav=Buffer.from(input), gain=10 ** (gainDb/20);
  assert.equal(wav.toString('ascii',0,4),'RIFF');
  assert.equal(wav.toString('ascii',8,12),'WAVE');
  let format=false, samples=0;
  for(let at=12;at<wav.length;){
    const n=wav.readUInt32LE(at+4),tag=wav.toString('ascii',at,at+4);
    assert(at+8+n<=wav.length);
    if(tag==='fmt '){ assert.equal(wav.readUInt16LE(at+8),1);assert.equal(wav.readUInt16LE(at+22),16);format=true; }
    if(tag==='data'){
      assert(format && n%2===0);
      for(let i=at+8;i<at+8+n;i+=2){
        const value=Math.round(wav.readInt16LE(i)*gain);
        assert(Math.abs(value)<32767,'Gain would clip; output not written');
        wav.writeInt16LE(value,i);samples++;
      }
    }
    at+=8+n+(n&1);
  }
  assert(samples>0);
  return wav;
}
if(process.argv[1] && fileURLToPath(import.meta.url).toLowerCase()===resolve(process.argv[1]).toLowerCase()){
  const [, ,input,output,db]=process.argv;
  assert(input && output && db,'input.wav output.wav gainDb required');
  writeFileSync(output,amplify(readFileSync(input),Number(db)));
}
