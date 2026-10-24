// Read-only native acceptance evidence. Raw profiles stay in the ignored output
// folder; the summary deliberately omits identifiers, nicknames and signing keys.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const [runtime, profileId, output, label] = process.argv.slice(2);
if (!runtime || !/^[0-9a-f]{24}$/.test(profileId ?? '') || !output || !/^[a-z0-9-]+$/.test(label ?? ''))
    throw new Error('Usage: node capture-profile.mjs <runtime> <profile-id> <ignored-output> <label>');
const file = path.join(runtime, 'user', 'profiles', `${profileId}.json`);
const bytes = fs.readFileSync(file);
const profile = JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, ''));
const hash = value => crypto.createHash('sha256').update(value).digest('hex');
const canon = value => Array.isArray(value) ? value.map(canon)
    : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(k => [k, canon(value[k])])) : value;
const digest = value => hash(JSON.stringify(canon(value)));
const extension = profile['com.maschine.ArenaMode.rewards'];
const payments = extension?.payments ?? [];
const messages = profile.dialogues?.['59e7125688a45068a6249071']?.messages ?? [];
const rewardMail = messages.filter(message => payments.some(p => p.messageId === message._id));
const summary = {
    capturedUtc: new Date().toISOString(), label, bytes: bytes.length,
    fileSha256: hash(bytes), semanticSha256: digest(profile),
    sections: Object.fromEntries(Object.entries(profile).map(([key, value]) => [key, digest(value)])),
    enrolled: !!extension, payments: payments.map(p => ({roubles:p.roubles, sequence:p.sequence, receiptFingerprint:hash(String(p.receiptKey))})),
    paymentCount: payments.length, paidRoubles: payments.reduce((sum,p)=>sum+(p.roubles??0),0),
    inventoryRoubles: (profile.characters?.pmc?.Inventory?.items??[]).filter(i=>i._tpl==='5449016a4bdc2d6f028b456f').reduce((sum,i)=>sum+(i.upd?.StackObjectsCount??0),0),
    rewardMailCount: rewardMail.length,
    attachedRewardRoubles: rewardMail.reduce((sum,m)=>sum+(m.items?.data??[]).filter(i=>i._tpl==='5449016a4bdc2d6f028b456f').reduce((n,i)=>n+(i.upd?.StackObjectsCount??0),0),0)
};
fs.mkdirSync(output, {recursive:true});
fs.writeFileSync(path.join(output, `${label}.profile.json`), bytes, {flag:'wx'});
fs.writeFileSync(path.join(output, `${label}.summary.json`), JSON.stringify(summary,null,2), {flag:'wx'});
console.log(JSON.stringify({label,enrolled:summary.enrolled,paymentCount:summary.paymentCount,paidRoubles:summary.paidRoubles,inventoryRoubles:summary.inventoryRoubles,rewardMailCount:summary.rewardMailCount,attachedRewardRoubles:summary.attachedRewardRoubles,semanticSha256:summary.semanticSha256}));
