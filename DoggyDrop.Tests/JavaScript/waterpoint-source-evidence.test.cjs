const test = require('node:test'), assert = require('node:assert/strict');
const { classifyEvidence } = require('../../tools/waterpoint-source-evidence.cjs');
const drinking = { amenity: 'drinking_water' };
test('source drinking-water baseline can qualify without inventing access', () => {
    assert.equal(classifyEvidence(drinking).classification, 'A');
    assert.equal(classifyEvidence({ ...drinking, access: 'unknown' }).classification, 'A');
});
test('decorative subtype prevents A regardless of positive baseline or object identity', () => {
    for (const tags of [drinking, { amenity: 'fountain', drinking_water: 'yes' }])
        assert.equal(classifyEvidence({ ...tags, fountain: 'decorative' }).classification, 'B');
});
for (const [key, value] of [['drinking_water','no'],['drinking_water','boil'],['drinking_water','conditional'],['drinking_water:legal','no'],['potable','no'],['working','no']])
    test(`${key}=${value} overrides drinking-water and decorative evidence`, () => {
        assert.equal(classifyEvidence({ ...drinking, fountain:'decorative', [key]:value }).classification, 'D');
    });
for (const access of ['private','customers','restricted','no'])
    test(`access=${access} prevents A`, () => assert.equal(classifyEvidence({ ...drinking, access }).classification, 'D'));
test('indoor evidence and explicit potability uncertainty prevent A', () => {
    assert.equal(classifyEvidence({ ...drinking, indoor:'yes' }).classification, 'D');
    assert.equal(classifyEvidence({ ...drinking, drinking_water:'unknown' }).classification, 'C');
});
test('unresolved duplicate cannot become A or weaken a rejection', () => {
    assert.equal(classifyEvidence(drinking, true).classification, 'B');
    assert.equal(classifyEvidence({ ...drinking, drinking_water:'no' }, true).classification, 'D');
});
test('generic fountain requires separate drinking-water evidence', () => {
    assert.equal(classifyEvidence({ amenity:'fountain' }).classification, 'B');
    assert.equal(classifyEvidence({ amenity:'fountain', drinking_water:'yes' }).classification, 'A');
});
