// Inline SVG icons (Tabler-style: 24px grid, 1.7 stroke, currentColor).
// Returned as markup strings so they can be dropped straight into innerHTML.

const svg = (paths, extra = '') =>
  `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" ` +
  `stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" ${extra}>${paths}</svg>`

export const icons = {
  download: svg('<path d="M12 3v12"/><path d="m7 11 5 5 5-5"/><path d="M4 19h16"/>'),
  package: svg('<path d="M12 3 4 7v10l8 4 8-4V7z"/><path d="M4 7l8 4 8-4"/><path d="M12 11v10"/>'),
  install: svg('<path d="M12 3v9"/><path d="m8.5 9.5 3.5 3 3.5-3"/><rect x="3" y="14" width="18" height="7" rx="2"/><path d="M7 17.5h.01"/>'),
  store: svg('<path d="M4 4h16v4l-2 2-2-2-2 2-2-2-2 2-2-2-2 2-2-2z"/><path d="M5 10v10h14V10"/><path d="M10 20v-5h4v5"/>'),
  arrow: svg('<path d="M5 12h14"/><path d="m13 6 6 6-6 6"/>', 'class="arrow"'),
  shield: svg('<path d="M12 3 5 6v5c0 4 3 7 7 9 4-2 7-5 7-9V6z"/><path d="m9 12 2 2 4-4"/>'),
  lock: svg('<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>'),
  wifi_off: svg('<path d="M3 3l18 18"/><path d="M9 17a4 4 0 0 1 5.5-.3"/><path d="M6 13a8 8 0 0 1 3-1.8"/><path d="M15 11a8 8 0 0 1 3 2"/><path d="M3.5 9a13 13 0 0 1 4-2.4"/><path d="M12 6c3 0 5.8 1.1 8 3"/><path d="M12 21h.01"/>'),
  feather: svg('<path d="M20 4C11 4 4 11 4 20"/><path d="M4 20 14 10"/><path d="M14 7h3v3"/><path d="M9 15h4"/>'),
  scalpel: svg('<path d="M14 4 20 9 9 20H4v-5z"/><path d="m9 14 5-5"/>'),
  pen: svg('<path d="M4 20l4-1L19 8a2.1 2.1 0 0 0-3-3L5 16z"/><path d="M14 6l3 3"/>'),
  layers: svg('<path d="m12 3 9 5-9 5-9-5z"/><path d="m3 13 9 5 9-5"/>'),
  signature: svg('<path d="M3 17c3 0 3-9 6-9s2 7 4 7 2-4 4-4"/><path d="M3 21h18"/>'),
  form: svg('<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 8h8"/><path d="M8 12h8"/><path d="M8 16h5"/>'),
  printer: svg('<path d="M7 8V3h10v5"/><rect x="4" y="8" width="16" height="8" rx="2"/><path d="M7 16h10v5H7z"/>'),
  flatten: svg('<path d="m12 3 9 5-9 5-9-5z"/><path d="m3 12 9 5 9-5"/>'),
  hash: svg('<path d="M5 9h14"/><path d="M5 15h14"/><path d="M10 4 8 20"/><path d="M16 4l-2 16"/>'),
  compress: svg('<path d="M9 4v4H5"/><path d="M15 4v4h4"/><path d="M9 20v-4H5"/><path d="M15 20v-4h4"/>'),
  ocr: svg('<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M7 9v6"/><path d="M11 15V9l3 6V9"/><path d="M17 9h.01"/>'),
  redact: svg('<rect x="3" y="6" width="18" height="3" rx="1"/><rect x="3" y="12" width="11" height="3" rx="1"/><path d="M3 18h7"/>'),
  eraser: svg('<path d="M4 16 13 7l5 5-7 7H7z"/><path d="M9 21h11"/>'),
  bolt: svg('<path d="M13 3 4 14h6l-1 7 9-11h-6z"/>'),
  feathers: svg('<path d="M4 20 20 4"/><path d="M14 4h6v6"/><path d="M9 20H4v-5"/>'),
  check: svg('<path d="m5 12 5 5L20 7"/>'),
  spark: svg('<path d="M12 3v4"/><path d="M12 17v4"/><path d="M3 12h4"/><path d="M17 12h4"/><path d="m6 6 2.5 2.5"/><path d="m15.5 15.5 2.5 2.5"/><path d="m18 6-2.5 2.5"/><path d="m8.5 15.5-2.5 2.5"/>'),
  github: svg('<path d="M9 19c-4 1.5-4-2-6-2.5"/><path d="M15 21v-3.5c0-1 .2-1.5-.5-2.2 2.3-.3 4.5-1.2 4.5-5a3.9 3.9 0 0 0-1-2.7 3.6 3.6 0 0 0-.1-2.7s-.9-.3-3 1a12 12 0 0 0-6 0c-2.1-1.3-3-1-3-1a3.6 3.6 0 0 0-.1 2.7A3.9 3.9 0 0 0 5 10c0 3.8 2.2 4.7 4.5 5-.5.5-.6 1-.6 1.7V21"/>'),
  sun: svg('<circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4 4 1.5 1.5"/><path d="m18.5 18.5 1.5 1.5"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m4 20 1.5-1.5"/><path d="m18.5 5.5 1.5-1.5"/>'),
  moon: svg('<path d="M20 14a8 8 0 1 1-9.5-9.7A6 6 0 0 0 20 14"/>'),
  doc: svg('<path d="M6 3h8l5 5v13H6z"/><path d="M14 3v5h5"/>'),
}
