# Scalpel — marketing website

The marketing site for **Scalpel**, the local-only, portable Windows PDF editor.
A single static page that mirrors the app's **"Clinical"** design language (steel chrome,
surgical-red accent) and drives downloads through three channels: **Portable**, **Installer**,
and the **Microsoft Store**.

Built with plain **Vite** (no framework) so it stays a fast, static bundle for Netlify.

## Highlights

- **Three-option hero** — the portable EXE, the self-installer, and the Store link, side by side.
- **Bilingual, EN + HE, with full LTR/RTL mirroring** — every layout uses CSS logical properties,
  so Hebrew flips the whole page. Strings live in `src/i18n/{en,he}.js`; the toggle is in the chrome bar.
- **Light + dark themes** — tokens in `src/styles/tokens.css` are derived 1:1 from
  `../docs/system-design.md`. Light (Clinical) is the brand default; the OS preference is honoured on
  first visit, then the user's choice is persisted.
- **Lottie hero animation** — "The Incision": a surgical-red cut drawn across a document by a scalpel
  tip, then healing and looping. Authored in the official `text-to-lottie` player; embedded via
  `lottie-web` (lazy-mounted on scroll, paused offscreen, skipped under `prefers-reduced-motion`).
- **Accessible & resilient** — skip link, visible focus, reduced-motion support, and content that is
  fully visible without JS (the reveal animation is gated behind `html.js`).

## Develop

```bash
npm install
npm run dev        # http://localhost:5173
npm run build      # -> dist/
npm run preview    # serve the production build on :4173
```

## Test

```bash
npm run test:install   # one-time: install Playwright's Chromium
npm run test           # builds, serves, runs the e2e suite (desktop + mobile)
```

`tests/site.spec.js` covers the three download options, the EN↔HE/LTR↔RTL toggle (with persistence),
the theme toggle, every section + footer, the Lottie mounting and rendering geometry, and a
no-console-errors gate.

## Deploy (Netlify)

`netlify.toml` is preconfigured. Connect the repo with **base directory = `website`**; Netlify runs
`npm run build` and publishes `dist/`.

## Editing content

- **Copy / translations** — `src/i18n/en.js` and `src/i18n/he.js` (matching keys; `[data-i18n]`
  attributes in `index.html` bind to them).
- **Download links** — the `href`s on the `.dl` cards and `.option`/footer buttons in `index.html`
  (currently point at the GitHub releases page and a Store search; swap for the final URLs).
- **Colors / type** — `src/styles/tokens.css`.
- **Hero animation** — `public/lottie/hero.json`. To re-author, edit the slotted source in the
  `text-to-lottie` player, then re-run the inline transform (resolves slot colors → literals and
  drops the transparent bg layer, which `lottie-web` can't express via color alpha).

## Layout

```
website/
├── index.html              # full page markup (data-i18n bound)
├── src/
│   ├── main.js             # bootstraps theme, i18n, reveal, marquee, lottie
│   ├── theme.js            # light/dark, persisted
│   ├── lottie.js           # lazy lottie-web mount
│   ├── icons.js            # inline Tabler-style SVG set
│   ├── i18n/{index,en,he}.js
│   └── styles/{tokens,base,app}.css
├── public/                 # favicon, brand SVGs, lottie/hero.json, robots.txt
├── tests/site.spec.js      # Playwright e2e
├── netlify.toml
└── playwright.config.js
```
