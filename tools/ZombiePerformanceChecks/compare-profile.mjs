import fs from 'node:fs';
const before = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const after = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const changed = [];
function diff(a,b,p) {
  if (Object.is(a,b)) return;
  if (a===null || b===null || typeof a!=='object' || typeof b!=='object' || Array.isArray(a)!==Array.isArray(b)) { changed.push(p); return; }
  for (const k of new Set([...Object.keys(a),...Object.keys(b)])) diff(a[k],b[k],p?`${p}.${k}`:k);
}
diff(before,after,'');
// Values are intentionally omitted: profile data is private and belongs in ignored artifacts.
console.log(JSON.stringify({ changedPaths: changed, count: changed.length },null,2));
