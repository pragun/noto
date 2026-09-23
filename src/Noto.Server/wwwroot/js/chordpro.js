window.notoChordPro = window.notoChordPro || {};

let _libsLoaded = null;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = src;
        s.onload = resolve;
        s.onerror = reject;
        document.head.appendChild(s);
    });
}

async function loadLibs() {
    if (_libsLoaded) return _libsLoaded;
    _libsLoaded = (async () => {
        if (!window.ChordSheetJS) {
            await loadScript('/lib/js/chordsheetjs.min.js');
        }
        if (!window.svguitar) {
            await loadScript('/lib/js/svguitar.umd.js');
        }
    })();
    return _libsLoaded;
}

// Parse {columns: N} from source. ChordSheetJS does not handle it natively.
// Layout separators ({column_break}, {new_page}, {hr}) are handled in renderBody.
function extractLayoutDirectives(source) {
    const cols = source.match(/\{columns?:\s*(\d+)\}/i);
    const columnCount = cols ? parseInt(cols[1], 10) : 1;
    return { columnCount };
}

function extractDefines(song) {
    // Walk through song to find chord_definition tags
    const defs = {};
    if (!song.lines) return defs;
    for (const line of song.lines) {
        for (const item of (line.items || [])) {
            if (item.name === 'define' || item.name === 'chord') {
                // ChordSheetJS stores definitions in song.chordDefinitions
            }
        }
    }
    // Newer ChordSheetJS exposes via song.chordDefinitions
    if (song.chordDefinitions && typeof song.chordDefinitions.forEach === 'function') {
        song.chordDefinitions.forEach((def, name) => { defs[name] = def; });
    }
    return defs;
}

function renderDiagram(container, name, definition) {
    if (!window.svguitar) return;
    try {
        const chord = new window.svguitar.SVGuitarChord(container)
            .configure({
                strings: 6,
                frets: 5,
                position: definition.baseFret || 1,
                fretLabelPosition: 'left',
                color: '#2e2e2e',
                strokeWidth: 1.2,
                nutWidth: 4,
                fontFamily: 'JetBrains Mono, monospace',
                fingerSize: 0.6,
                style: 'normal',
                title: name,
                titleFontSize: 12,
            });

        const fingers = [];
        const barres = [];

        if (definition.frets && Array.isArray(definition.frets)) {
            // frets is array of 6 values, can be 'x', '0', or fret number
            // string index in svguitar: 1 = high E (top), 6 = low E (bottom)
            // but ChordPro convention: string 1 = low E... actually frets are usually listed low to high
            // {define: C frets x 3 2 0 1 0} = low E muted, A at 3rd, D at 2nd, G open, B 1st, E open
            // svguitar wants [stringIdx (1=high E), fret]
            for (let i = 0; i < definition.frets.length; i++) {
                const fret = definition.frets[i];
                // Convert: ChordPro is 6 strings low-to-high (E A D G B e)
                // svguitar: 1 = high e, 6 = low E — so reverse
                const svgString = 6 - i;
                if (fret === 'x' || fret === -1) {
                    fingers.push([svgString, 'x']);
                } else if (fret === 0 || fret === '0') {
                    // open — explicit "0" not always needed in svguitar
                    fingers.push([svgString, 0]);
                } else {
                    const f = parseInt(fret, 10);
                    if (!isNaN(f) && f > 0) fingers.push([svgString, f]);
                }
            }
        }

        chord.chord({ fingers, barres }).draw();
    } catch (e) {
        console.warn('Failed to render diagram for', name, e);
        container.textContent = name;
    }
}

// Per-target state: source + semitones
const _state = new Map();

// ── Layout separators ───────────────────────────────────────────────────────
// ChordSheetJS has no concept of a column break, a page break or a rule, and it
// discards runs of blank lines. Rather than guess at the formatter's markup and
// post-process it, the source is split on these separators, each segment is
// rendered independently, and the separator markup is emitted between them.
//
//   {column_break} {colb} {cb}   start the next column
//   {new_page} {np} {page_break} start the next page when printing
//   {hr} {rule}                  horizontal line
//   {line_gap: N} {gap: N}       N blank lines of vertical space
//
// Note that gaps are *space*, not pagination: they only push content onto the
// next page if the page actually overflows. To put a section on a fresh page
// regardless, use {np}.
//
// Runs of three or more newlines also become space, one blank line per extra
// newline, so deliberate spacing in the source survives.
const _SEPARATOR_RE = /^[ \t]*\{(column_break|colb|cb|new_page|np|page_break|hr|rule)\}[ \t]*$|^[ \t]*\{(?:line_gap|gap):[ \t]*(\d+(?:\.\d+)?)\}[ \t]*$|\n{3,}/gim;

function _gapHtml(lines) {
    const n = Math.max(0, Math.min(parseFloat(lines) || 0, 40));
    return '<div class="cp-spacer" style="height:' + (n * 1.8) + 'em"></div>';
}

function _separatorHtml(token) {
    const name = (token || '').toLowerCase();
    if (name === 'hr' || name === 'rule') return '<hr class="cp-hr">';
    if (name === 'new_page' || name === 'np' || name === 'page_break') return '<div class="cp-pagebreak"></div>';
    return '<div class="cp-colbreak"></div>';
}

// Exported so the splitting logic can be unit tested without a DOM.
notoChordPro.splitLayout = function(source) {
    const parts = [];
    let last = 0;
    let m;
    _SEPARATOR_RE.lastIndex = 0;
    while ((m = _SEPARATOR_RE.exec(source)) !== null) {
        if (m.index > last) parts.push({ text: source.slice(last, m.index) });
        if (m[1]) {
            parts.push({ sep: _separatorHtml(m[1]) });
        } else if (m[2] !== undefined) {
            parts.push({ sep: _gapHtml(m[2]) });
        } else {
            const extra = Math.min(m[0].length - 2, 8);
            parts.push({ sep: _gapHtml(extra * 0.5) });
        }
        last = m.index + m[0].length;
        if (_SEPARATOR_RE.lastIndex === m.index) _SEPARATOR_RE.lastIndex++;
    }
    if (last < source.length) parts.push({ text: source.slice(last) });
    return parts;
};

notoChordPro.renderBody = function(source, parser, semitones) {
    const formatter = new window.ChordSheetJS.HtmlDivFormatter();
    const sem = (typeof semitones === 'number') ? semitones : 0;
    return notoChordPro.splitLayout(source).map(function(part) {
        if (part.sep !== undefined) return part.sep;
        if (!part.text.trim()) return '';
        let song = parser.parse(part.text);
        if (sem !== 0) {
            try {
                if (typeof song.transpose === 'function') song = song.transpose(sem) || song;
            } catch {}
        }
        return formatter.format(song);
    }).join('');
};

notoChordPro.render = async function(targetSelector, source, semitones, fallbackTitle) {
    await loadLibs();
    const target = document.querySelector(targetSelector);
    if (!target) return;

    const prev = _state.get(targetSelector) || { source: source, semitones: 0, fallbackTitle: fallbackTitle };
    if (source !== undefined && source !== null) prev.source = source;
    if (semitones !== undefined) prev.semitones = semitones;
    if (fallbackTitle !== undefined && fallbackTitle !== null) prev.fallbackTitle = fallbackTitle;
    _state.set(targetSelector, prev);

    source = prev.source;
    semitones = prev.semitones;
    fallbackTitle = prev.fallbackTitle;

    const { columnCount } = extractLayoutDirectives(source);
    let cleanSource = source.replace(/\{columns?:\s*\d+\}\s*\n?/gi, '');
    // Chord-progression-only lines (intro/interlude/outro): a line containing
    // only [chords], pipes, and whitespace. ChordSheetJS would render each
    // chord as a column with empty lyric — collapsing the inter-chord spacing
    // and putting pipes on a separate row. Instead, strip the brackets so the
    // line renders as plain monospace text and the source spacing is preserved.
    cleanSource = cleanSource.split('\n').map(line => {
        const stripped = line.replace(/\[[^\]]+\]/g, '');
        // If what remains is only whitespace and pipe characters, treat as chord-only line.
        if (line.includes('[') && /^[\s|]*$/.test(stripped)) {
            // Replace # with a sentinel so ChordSheetJS doesn't treat it as a comment.
            return line.replace(/\[([^\]]+)\]/g, '$1').replace(/#/g, '♯');
        }
        return line;
    }).join('\n');

    const parser = new window.ChordSheetJS.ChordProParser();
    let song;
    try {
        song = parser.parse(cleanSource);
        const sem = (typeof semitones === 'number') ? semitones : 0;
        if (sem !== 0) {
            try {
                if (typeof song.transpose === 'function') {
                    song = song.transpose(sem) || song;
                } else if (typeof song.changeKey === 'function' && song.key) {
                    const newKey = transposeKeyName(song.key, sem);
                    if (newKey) song = song.changeKey(newKey) || song;
                }
            } catch (te) {
                console.error('[chordpro] transpose failed', te);
            }
        }
    } catch (e) {
        target.innerHTML = '<pre style="color:#b04040;">ChordPro parse error: ' + e.message + '</pre>';
        return;
    }

    // Build the rendered output
    const html = [];
    html.push('<div class="cp-sheet">');

    // Header (title, key, capo). Fall back to noto's entity title if {title:} not in source.
    const title = song.title || fallbackTitle || '';
    const subtitle = song.subtitle || song.artist || '';
    const key = song.key || '';
    const capo = song.capo || '';
    const tempo = song.tempo || '';
    const time = song.time || '';

    // Set document title for clean browser print header
    if (title) {
        document.title = title;
    }

    html.push('<div class="cp-header">');
    if (title) html.push('<div class="cp-title">' + esc(title) + '</div>');
    if (subtitle) html.push('<div class="cp-subtitle">' + esc(subtitle) + '</div>');
    const meta = [];
    if (key) meta.push('key: ' + esc(key));
    if (capo) meta.push('capo ' + esc(capo));
    if (tempo) meta.push(esc(tempo) + ' bpm');
    if (time) meta.push(esc(time));
    const semNum = (typeof semitones === 'number') ? semitones : 0;
    if (semNum !== 0) meta.push('transposed ' + (semNum > 0 ? '+' : '') + semNum);
    if (meta.length) html.push('<div class="cp-meta">' + meta.join(' · ') + '</div>');

    // Controls (hidden in print) — wire up via addEventListener after render
    html.push('<div class="cp-controls">');
    html.push('<button data-cp-action="down">−1</button>');
    html.push('<button data-cp-action="reset">reset</button>');
    html.push('<button data-cp-action="up">+1</button>');
    html.push('<span style="width:14px;"></span>');
    html.push('<button data-cp-action="font-down" title="smaller text">A−</button>');
    html.push('<button data-cp-action="font-reset" title="reset text size">A</button>');
    html.push('<button data-cp-action="font-up" title="larger text">A+</button>');
    html.push('<span style="flex:1;"></span>');
    html.push('<button data-cp-action="print">print</button>');
    html.push('</div>');
    html.push('</div>');

    // Chord diagrams (only for {define:}'d chords)
    const defines = extractDefines(song);
    const defNames = Object.keys(defines);
    if (defNames.length > 0) {
        html.push('<div class="cp-diagrams" id="cp-diagrams"></div>');
    }

    // Body — render via formatter, but strip meta directives so they don't duplicate the header
    let body;
    try {
        const bodyOnlySource = cleanSource.replace(
            /^\{(title|t|subtitle|st|artist|composer|key|k|capo|tempo|time|meta):[^}]*\}\s*$/gm,
            ''
        );
        body = notoChordPro.renderBody(bodyOnlySource, parser, semitones);
    } catch (e) {
        body = '<pre>' + esc(cleanSource) + '</pre>';
    }

    html.push('<div class="cp-body cp-cols-' + columnCount + '">');
    html.push(body);
    html.push('</div>');

    html.push('</div>');
    target.innerHTML = html.join('');

    // Apply persisted font size (per-entity, from localStorage)
    notoChordPro.applyFontSize(targetSelector);

    // Wire up control buttons
    target.querySelectorAll('button[data-cp-action]').forEach(function(btn) {
        const action = btn.getAttribute('data-cp-action');
        btn.addEventListener('click', function() {
            if (action === 'down') notoChordPro.bump(targetSelector, -1);
            else if (action === 'up') notoChordPro.bump(targetSelector, 1);
            else if (action === 'reset') notoChordPro.bump(targetSelector, 0, true);
            else if (action === 'print') notoChordPro.print();
            else if (action === 'font-down') notoChordPro.bumpFont(targetSelector, -1);
            else if (action === 'font-up') notoChordPro.bumpFont(targetSelector, 1);
            else if (action === 'font-reset') notoChordPro.bumpFont(targetSelector, 0, true);
        });
    });

    // Render chord diagrams after DOM is in place
    if (defNames.length > 0) {
        const diagramContainer = target.querySelector('#cp-diagrams');
        for (const name of defNames) {
            const wrap = document.createElement('div');
            wrap.className = 'cp-diagram';
            diagramContainer.appendChild(wrap);
            renderDiagram(wrap, name, defines[name]);
        }
    }
};

function esc(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
function escAttr(s) {
    return String(s).replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/\n/g, '\\n').replace(/\r/g, '');
}

const _SHARP_KEYS = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];
const _FLAT_TO_SHARP = { 'Db':'C#', 'Eb':'D#', 'Gb':'F#', 'Ab':'G#', 'Bb':'A#' };
function transposeKeyName(key, delta) {
    if (!key) return null;
    const minor = /m$/.test(key) && !/maj$/i.test(key);
    let root = key.replace(/m$/, '');
    let idx = _SHARP_KEYS.indexOf(root);
    if (idx === -1) {
        idx = _SHARP_KEYS.indexOf(_FLAT_TO_SHARP[root] || root);
    }
    if (idx === -1) return null;
    const newIdx = ((idx + delta) % 12 + 12) % 12;
    return _SHARP_KEYS[newIdx] + (minor ? 'm' : '');
}

notoChordPro.bump = function(targetSelector, delta, reset) {
    const prev = _state.get(targetSelector);
    if (!prev) return;
    const cur = (typeof prev.semitones === 'number') ? prev.semitones : 0;
    const next = reset ? 0 : cur + delta;
    notoChordPro.render(targetSelector, null, next);
};

notoChordPro.print = function() {
    window.print();
};

// Per-entity persisted font size for the chord sheet (controls both screen + print).
// Stored in localStorage keyed by the entity UUID parsed from /entity/{id}.
// 0 = default (no override). Steps of 1px on screen, 1pt on print.
notoChordPro._fontStepPx = 1;
notoChordPro._fontStepPt = 1;
notoChordPro._fontDefaultPx = 12;
notoChordPro._fontDefaultPt = 10;

function _entityIdFromUrl() {
    const m = location.pathname.match(/\/entity\/([0-9a-fA-F-]{36})/);
    return m ? m[1] : null;
}
function _fontKey() {
    const id = _entityIdFromUrl();
    return id ? 'noto-cp-fontstep-' + id : null;
}
function _getFontStep() {
    const k = _fontKey();
    if (!k) return 0;
    const v = parseInt(localStorage.getItem(k) || '0', 10);
    return isNaN(v) ? 0 : Math.max(-4, Math.min(8, v));
}
function _setFontStep(step) {
    const k = _fontKey();
    if (!k) return;
    if (step === 0) localStorage.removeItem(k);
    else localStorage.setItem(k, String(step));
}

notoChordPro.applyFontSize = function(targetSelector) {
    const target = document.querySelector(targetSelector);
    if (!target) return;
    const step = _getFontStep();
    const px = notoChordPro._fontDefaultPx + step * notoChordPro._fontStepPx;
    const pt = notoChordPro._fontDefaultPt + step * notoChordPro._fontStepPt;
    target.style.setProperty('--cp-font-pt', px + 'px');
    target.style.setProperty('--cp-print-pt', pt + 'pt');
};

notoChordPro.bumpFont = function(targetSelector, delta, reset) {
    const cur = _getFontStep();
    const next = reset ? 0 : cur + delta;
    _setFontStep(next);
    notoChordPro.applyFontSize(targetSelector);
};
