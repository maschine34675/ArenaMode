// Read-only independent native evidence: verifies HMAC in memory and emits only
// bounded combat/proof totals. Delegation keys and participant IDs are never printed.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const [authorityDirectory, receiptDirectory, output] = process.argv.slice(2);
if (!authorityDirectory || !receiptDirectory || !output)
    throw new Error('Usage: node summarize-receipts.mjs <authority-dir> <receipt-dir> <output.json>');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const sha = value => crypto.createHash('sha256').update(value).digest('hex');
const name = (value, names) => typeof value==='number' ? names[value] ?? `Unknown(${value})` : value;
const rules=['KillsOnly','LocalTarget','FreeForAllScore','TeamScore','SoloElimination','TeamElimination'];
const sides=['Individual','Friendly','Opponent'];
const kinds=['Combat','Environment','Administrative','LocalForfeit'];
const summary = [];
for (const file of fs.existsSync(receiptDirectory) ? fs.readdirSync(receiptDirectory).filter(f=>/^[0-9a-f]{32}\.json$/.test(f)) : []) {
    const signed = read(path.join(receiptDirectory,file));
    if (!/^[0-9a-f]{32}$/.test(signed.delegationId)) throw new Error('Invalid delegation filename');
    const authority = read(path.join(authorityDirectory,'delegations',`${signed.delegationId}.json`));
    const actual = crypto.createHmac('sha256',Buffer.from(authority.key,'base64')).update(signed.payload,'utf8').digest();
    const expected = Buffer.from(signed.signature,'hex');
    if (actual.length !== expected.length || !crypto.timingSafeEqual(actual,expected)) throw new Error('Invalid receipt signature');
    const earned = JSON.parse(Buffer.from(signed.payload,'base64').toString('utf8'));
    const {authorization:a, report:r, restoration:p} = earned.evidence;
    const rounds = a.rounds.map((round,index)=> {
        const actors = new Map(round.actors.map(actor=>[actor.lifeId,actor]));
        const deaths = r.rounds[index].deaths;
        const localKills = deaths.filter(d=>name(d.kind,kinds)==='Combat' && actors.get(d.killerLifeId)?.participantId===a.localParticipantId
            && actors.get(d.victimLifeId)?.participantId!==a.localParticipantId
            && name(actors.get(d.victimLifeId)?.side,sides)!== 'Friendly').length;
        return {rule:name(round.rule,rules),observedDurationMilliseconds:round.observedDurationMilliseconds,timeLimitMilliseconds:round.timeLimitMilliseconds,
            killTarget:round.killTarget,allowIncomplete:round.allowIncomplete,actors:round.actors.length,deaths:deaths.length,localKills,
            localTargetReached:name(round.rule,rules)==='LocalTarget' && round.killTarget>0 && localKills>=round.killTarget};
    });
    summary.push({runFingerprint:sha(file),signatureValid:true,state:name(r.state,['Completed','Aborted','NoContest']),mode:a.mode,policy:a.policy,
        restorationState:name(p.state,['Pending','Persisted']),exactRestorationRevision:p.expectedProfileRevision===p.persistedProfileRevision
            &&p.persistedProfileRevision===a.cleanProfileRevision,rounds});
}
fs.writeFileSync(output,JSON.stringify(summary,null,2),{flag:'wx'});
console.log(JSON.stringify(summary));
