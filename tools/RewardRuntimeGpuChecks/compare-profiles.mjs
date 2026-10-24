// Reports semantic differences without printing profile values. Both inputs are
// local acceptance snapshots; this command never modifies an installed profile.
import fs from 'node:fs';
const [before, after] = process.argv.slice(2);
if (!before || !after) throw new Error('Usage: node compare-profiles.mjs <before.json> <after.json>');
const parse = file => JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));
const differences=[];
function compare(a,b,path='') {
    if (Object.is(a,b)) return;
    if (a && b && typeof a==='object' && typeof b==='object' && Array.isArray(a)===Array.isArray(b)) {
        for(const key of new Set([...Object.keys(a),...Object.keys(b)])) compare(a[key],b[key],`${path}/${key.replaceAll('~','~0').replaceAll('/','~1')}`);
    } else differences.push({path,change:a===undefined?'added':b===undefined?'removed':'changed'});
}
compare(parse(before),parse(after));
console.log(JSON.stringify({equal:differences.length===0,differenceCount:differences.length,differences},null,2));
