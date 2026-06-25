// Lazy, motion-respecting Lottie mount. The animation only loads when the host
// scrolls into view and the user hasn't asked to reduce motion — otherwise the
// static SVG fallback inside the host stays visible.
import lottie from 'lottie-web'

const reduced = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

export function mountLottie(host, src) {
  if (!host || reduced()) return

  let anim = null
  const load = async () => {
    try {
      const res = await fetch(src)
      if (!res.ok) return
      const data = await res.json()
      const fallback = host.querySelector('.appframe__fallback')
      if (fallback) fallback.style.display = 'none'
      anim = lottie.loadAnimation({
        container: host,
        renderer: 'svg',
        loop: true,
        autoplay: true,
        animationData: data,
        rendererSettings: { progressiveLoad: true, preserveAspectRatio: 'xMidYMid meet' },
      })
      // Expose for debugging / e2e frame inspection.
      if (typeof window !== 'undefined') window.__heroAnim = anim
    } catch {
      /* keep the static fallback on any failure */
    }
  }

  const io = new IntersectionObserver(
    (entries, obs) => {
      for (const e of entries) {
        if (e.isIntersecting) {
          obs.disconnect()
          load()
        }
      }
    },
    { rootMargin: '200px' }
  )
  io.observe(host)

  // Pause when offscreen to save CPU.
  const vis = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!anim) continue
      e.isIntersecting ? anim.play() : anim.pause()
    }
  })
  vis.observe(host)
}
