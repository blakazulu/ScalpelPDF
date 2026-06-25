// Per-line "incision" underlines for the hero headline's accent phrase.
//
// The phrase wraps into a variable number of lines depending on viewport width,
// font and text direction — so we measure the real rendered line boxes with the
// Range API and draw one bar under each line. Each bar grows from the start edge
// of the text (left in LTR, right in RTL) to the end, staggered line-by-line.

const STAGGER = 150 // ms between consecutive lines
const DUR = 420 // ms per line draw
const reduced = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

export function initCutUnderline() {
  const title = document.querySelector('.hero__title')
  const cut = title?.querySelector('.cut')
  if (!title || !cut || typeof document.createRange !== 'function') return () => {}

  title.classList.add('has-cut-lines')
  cut.classList.add('cut--js') // hand off from the CSS fallback underline

  let layer = title.querySelector('.cut-lines')
  if (!layer) {
    layer = document.createElement('span')
    layer.className = 'cut-lines'
    layer.setAttribute('aria-hidden', 'true')
    title.appendChild(layer)
  }

  function draw(animate) {
    layer.textContent = ''
    const fs = parseFloat(getComputedStyle(cut).fontSize) || 40
    const thickness = Math.max(2, fs * 0.07)
    // getClientRects gives the full line box (line-height), whose bottom sits in
    // the leading below the text. Lift the bar up so it rests just under the
    // baseline — a proper underline — instead of drifting onto the next line.
    const lift = fs * 0.09
    const rtl = getComputedStyle(title).direction === 'rtl'
    const base = title.getBoundingClientRect()

    const range = document.createRange()
    range.selectNodeContents(cut)
    const lines = [...range.getClientRects()].filter((r) => r.width > 1)

    lines.forEach((r, i) => {
      const bar = document.createElement('span')
      bar.className = 'cut-line'
      bar.style.top = `${r.bottom - base.top - lift}px`
      bar.style.left = `${r.left - base.left}px`
      bar.style.width = `${r.width}px`
      bar.style.height = `${thickness}px`
      bar.style.transformOrigin = rtl ? 'right center' : 'left center'
      layer.appendChild(bar)

      if (animate && !reduced()) {
        bar.animate(
          [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }],
          { duration: DUR, delay: i * STAGGER, easing: 'cubic-bezier(0.16, 1, 0.3, 1)', fill: 'both' }
        )
      }
    })
  }

  const redraw = (animate = true) => draw(animate)

  // Initial draw once fonts are ready (so line boxes are measured correctly),
  // sequenced just after the headline settles in.
  const start = () => setTimeout(() => redraw(true), 360)
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(start)
  else start()

  // Reflow → reposition without replaying the stagger.
  let t
  window.addEventListener('resize', () => {
    clearTimeout(t)
    t = setTimeout(() => redraw(false), 150)
  })

  return redraw
}
