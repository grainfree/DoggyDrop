'use strict';

// Offline owner-review gate, never a runtime importer or a potability guarantee.
// Context/geometry/date review may demote A further; it must never promote B/C/D.
function classifyEvidence(tags, unresolvedDuplicate = false) {
    const reasons = [];
    let classification = 'A';
    const reject = reason => { classification = 'D'; reasons.push(reason); };
    if (['private', 'customers', 'no', 'restricted'].includes(tags.access) || tags.indoor === 'yes')
        reject('Explicit restricted/customer/private/indoor access');
    if (['no', 'boil', 'conditional'].includes(tags.drinking_water) ||
        tags['drinking_water:legal'] === 'no' || tags.potable === 'no' || tags.working === 'no')
        reject('Non-potable/conditional or explicitly not working');
    if (tags.drinking_water === 'unknown' && classification !== 'D') {
        classification = 'C'; reasons.push('Explicit potability unknown contradicts amenity inference');
    }
    const review = reason => { if (classification === 'A') classification = 'B'; reasons.push(reason); };
    if (tags.fountain === 'decorative')
        review('Decorative fountain subtype contradicts drinking-water use; owner verification required');
    if (unresolvedDuplicate) review('Unresolved nearby representation within 25m');
    if (tags.amenity !== 'drinking_water' && tags.drinking_water !== 'yes')
        review('No explicit drinking-water evidence; generic fountain alone is insufficient');
    return { classification, reasons };
}

module.exports = { classifyEvidence };
if (require.main === module) {
    const rows = JSON.parse(require('node:fs').readFileSync(0, 'utf8'));
    process.stdout.write(JSON.stringify(rows.map(row => classifyEvidence(row.tags, row.unresolvedDuplicate))));
}
