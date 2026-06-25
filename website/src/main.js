import '@fontsource-variable/geist'
import '@fontsource-variable/geist-mono'
import '@fontsource-variable/heebo'
import './styles/tokens.css'
import './styles/base.css'
import './styles/app.css'

import { icons } from './icons.js'
import { applyTheme, detectTheme, toggleTheme } from './theme.js'
import { applyLang, detectLang, nextLang, locales } from './i18n/index.js'
import { mountLottie } from './lottie.js'
import { initCutUnderline } from './cutUnderline.js'

let redrawCutUnderline = () => {}

// ---- Icons: replace every [data-icon] placeholder with its SVG ----
function injectIcons() {
  for (const el of document.querySelectorAll('[data-icon]')) {
    const name = el.getAttribute('data-icon')
    if (icons[name]) el.innerHTML = icons[name]
  }
}

// ---- Language toggle (cycles through locales) ----
function setupLang() {
  let lang = applyLang(detectLang())
  const btn = document.querySelector('[data-lang-toggle]')
  const label = btn?.querySelector('[data-lang-label]')
  const syncLabel = () => {
    if (label) label.textContent = locales.find((l) => l.code === nextLang(lang)).label
  }
  syncLabel()
  btn?.addEventListener('click', () => {
    lang = applyLang(nextLang(lang))
    syncLabel()
    // Text + direction changed — redraw the per-line underlines for the new layout.
    requestAnimationFrame(() => redrawCutUnderline(true))
  })
}

// ---- Theme toggle ----
function setupTheme() {
  applyTheme(detectTheme())
  document.querySelector('[data-theme-toggle]')?.addEventListener('click', () => toggleTheme())
}

// ---- Scroll reveal ----
function setupReveal() {
  const els = document.querySelectorAll('.reveal')
  if (!els.length) return
  const io = new IntersectionObserver(
    (entries, obs) => {
      for (const e of entries) {
        if (e.isIntersecting) {
          e.target.classList.add('is-in')
          obs.unobserve(e.target)
        }
      }
    },
    { rootMargin: '0px 0px -8% 0px', threshold: 0.08 }
  )
  els.forEach((el) => io.observe(el))
}

// ---- Marquee: duplicate the track so the loop is seamless ----
function setupMarquee() {
  const track = document.querySelector('.marquee__track')
  if (track) track.append(...[...track.children].map((c) => c.cloneNode(true)))
}

function init() {
  injectIcons()
  setupTheme()
  setupLang()
  setupReveal()
  setupMarquee()
  redrawCutUnderline = initCutUnderline()
  mountLottie(document.querySelector('[data-lottie]'), '/lottie/hero.json')
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init)
} else {
  init()
}
