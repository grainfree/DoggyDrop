const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
class Element {
    constructor(tag) { this.tag = tag; this.children = []; this.attributes = {}; this.dataset = {}; this.events = {}; this.text = ''; this.complete = false; }
    set className(value) { this.attributes.class = value; }
    get className() { return this.attributes.class || ''; }
    get classList() { return { add: name => { this.className += ' ' + name; }, remove: name => { this.className = this.className.split(' ').filter(x => x !== name).join(' '); }, contains: name => this.className.split(' ').includes(name) }; }
    set textContent(value) { this.text = String(value); this.children = []; }
    get textContent() { return this.text + this.children.map(c => c.textContent).join(''); }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    append(...nodes) { for (const node of nodes) { node.parentElement = this; this.children.push(node); } }
    addEventListener(name, handler) { this.events[name] = handler; }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    querySelectorAll(selector) {
        const matches = node => selector.startsWith('.') ? node.classList.contains(selector.slice(1)) : node.tag === selector;
        return this.children.flatMap(node => [...(matches(node) ? [node] : []), ...node.querySelectorAll(selector)]);
    }
    get outerHTML() { return `<${this.tag}${Object.entries(this.attributes).map(([k,v]) => ` ${k}="${escape(v)}"`).join('')}>${escape(this.text)}${this.children.map(c => c.outerHTML).join('')}</${this.tag}>`; }
}
for (const key of ['src', 'href', 'alt', 'type', 'title', 'decoding', 'referrerPolicy'])
    Object.defineProperty(Element.prototype, key, { get() { return this.attributes[key.toLowerCase()]; }, set(value) { this.setAttribute(key.toLowerCase(), value); } });
function loadPopup(L = { divIcon: options => options }) {
    const document = { createElement: tag => new Element(tag), getElementById: () => null };
    const window = { L };
    for (const script of ['place-marker.js', 'place-popup.js']) vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../../DoggyDrop/wwwroot/js', script), 'utf8'), { window, document, URL });
    return { document, popup: window.DoggyDropPlacePopup, marker: window.DoggyDropPlaceMarker };
}
module.exports = { loadPopup };
