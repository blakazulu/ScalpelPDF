import en from './en.js'
import he from './he.js'

const dicts = { en, he }
const RTL = new Set(['he', 'ar'])
const STORAGE_KEY = 'scalpel.lang'

export const locales = [
  { code: 'en', label: 'EN', name: 'English' },
  { code: 'he', label: 'עב', name: 'עברית' },
]

export function detectLang() {
  const saved = localStorage.getItem(STORAGE_KEY)
  if (saved && dicts[saved]) return saved
  const nav = (navigator.language || 'en').slice(0, 2).toLowerCase()
  return dicts[nav] ? nav : 'en'
}

export function t(lang, key) {
  return (dicts[lang] && dicts[lang][key]) ?? dicts.en[key] ?? key
}

/** Apply a language to the whole document: dir, lang, and every [data-i18n] node. */
export function applyLang(lang) {
  if (!dicts[lang]) lang = 'en'
  const dir = RTL.has(lang) ? 'rtl' : 'ltr'
  const html = document.documentElement
  html.setAttribute('lang', lang)
  html.setAttribute('dir', dir)
  localStorage.setItem(STORAGE_KEY, lang)

  document.title = t(lang, 'meta.title')
  setMeta('description', t(lang, 'meta.desc'))

  // Text content
  for (const el of document.querySelectorAll('[data-i18n]')) {
    el.textContent = t(lang, el.getAttribute('data-i18n'))
  }
  // Rich HTML content (trusted dictionary strings only)
  for (const el of document.querySelectorAll('[data-i18n-html]')) {
    el.innerHTML = t(lang, el.getAttribute('data-i18n-html'))
  }
  // Attribute translations: data-i18n-attr="aria-label:a11y.theme;title:a11y.theme"
  for (const el of document.querySelectorAll('[data-i18n-attr]')) {
    for (const pair of el.getAttribute('data-i18n-attr').split(';')) {
      const [attr, key] = pair.split(':')
      if (attr && key) el.setAttribute(attr.trim(), t(lang, key.trim()))
    }
  }
  return lang
}

function setMeta(name, content) {
  let m = document.querySelector(`meta[name="${name}"]`)
  if (!m) {
    m = document.createElement('meta')
    m.setAttribute('name', name)
    document.head.appendChild(m)
  }
  m.setAttribute('content', content)
}

export function nextLang(current) {
  const i = locales.findIndex((l) => l.code === current)
  return locales[(i + 1) % locales.length].code
}
