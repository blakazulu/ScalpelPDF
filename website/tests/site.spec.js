import { test, expect } from '@playwright/test'

test.describe('Scalpel marketing site', () => {
  test('hero offers all three download options', async ({ page }) => {
    await page.goto('/')
    const downloads = page.locator('.downloads .dl')
    await expect(downloads).toHaveCount(3)
    await expect(page.locator('[data-dl="portable"]')).toBeVisible()
    await expect(page.locator('[data-dl="installer"]')).toBeVisible()
    await expect(page.locator('[data-dl="store"]')).toBeVisible()

    // Store option links out to the Microsoft Store.
    await expect(page.locator('[data-dl="store"]')).toHaveAttribute('href', /apps\.microsoft\.com/)
  })

  test('headline and primary CTAs are present', async ({ page }) => {
    await page.goto('/')
    await expect(page.locator('#hero-title')).toBeVisible()
    // Final get-it section repeats the three options.
    await expect(page.locator('#get .option')).toHaveCount(3)
    await expect(page.locator('.option--primary')).toBeVisible()
  })

  test('language toggle switches to Hebrew and flips direction to RTL', async ({ page }) => {
    await page.goto('/')
    const html = page.locator('html')
    await expect(html).toHaveAttribute('lang', 'en')
    await expect(html).toHaveAttribute('dir', 'ltr')

    await page.locator('[data-lang-toggle]').click()
    await expect(html).toHaveAttribute('lang', 'he')
    await expect(html).toHaveAttribute('dir', 'rtl')

    // Hebrew copy is actually rendered.
    await expect(page.locator('#privacy-title')).toContainText('המסמכים')

    // Persists across reload.
    await page.reload()
    await expect(html).toHaveAttribute('lang', 'he')
    await expect(html).toHaveAttribute('dir', 'rtl')
  })

  test('theme toggle switches light <-> dark and persists', async ({ page }) => {
    await page.goto('/')
    const html = page.locator('html')
    const initial = await html.getAttribute('data-theme')
    await page.locator('[data-theme-toggle]').click()
    const toggled = await html.getAttribute('data-theme')
    expect(toggled).not.toBe(initial)
    await page.reload()
    await expect(html).toHaveAttribute('data-theme', toggled)
  })

  test('sections and footer render', async ({ page }) => {
    await page.goto('/')
    for (const id of ['#features', '#tools', '#privacy', '#get']) {
      await expect(page.locator(id)).toBeVisible()
    }
    await expect(page.locator('.footer')).toBeVisible()
    await expect(page.locator('#tools .tool')).toHaveCount(9)
  })

  test('hero Lottie animation mounts and renders geometry', async ({ page }) => {
    await page.goto('/')
    // Lazy-mounted on intersection; the hero is above the fold so it loads.
    await page.waitForFunction(() => !!window.__heroAnim, null, { timeout: 15000 })
    const total = await page.evaluate(() => window.__heroAnim.totalFrames)
    expect(total).toBeGreaterThan(100)
    // Pin to mid-cut and confirm the SVG has actual drawn paths (not empty).
    await page.evaluate(() => window.__heroAnim.goToAndStop(50, true))
    await page.waitForTimeout(200) // let lottie-web repaint the frame
    const withGeometry = await page.evaluate(() => {
      const svg = document.querySelector('[data-lottie] svg')
      return [...svg.querySelectorAll('path')].filter((p) => (p.getAttribute('d') || '').length > 2).length
    })
    // 9 shapes total; assert the bulk render (incision + document + tip) — robust to a 1-frame settle.
    expect(withGeometry).toBeGreaterThanOrEqual(8)
  })

  test('accent phrase gets per-line underlines that draw from the start edge', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.cut-line')
    const ltr = await page.evaluate(() => {
      const bars = [...document.querySelectorAll('.cut-line')]
      return { count: bars.length, origin: bars[0]?.style.transformOrigin }
    })
    expect(ltr.count).toBeGreaterThanOrEqual(1) // one bar per wrapped line
    expect(ltr.origin).toContain('left') // LTR draws from the left

    // Switching to Hebrew flips the draw to start from the right.
    await page.locator('[data-lang-toggle]').click()
    await page.waitForFunction(
      () => {
        const b = document.querySelector('.cut-line')
        return b && b.style.transformOrigin.includes('right')
      },
      null,
      { timeout: 5000 }
    )
  })

  test('social + SEO meta and structured data are present', async ({ page }) => {
    await page.goto('/')
    // Open Graph image must be an absolute PNG (SVG og:images don't render on
    // WhatsApp/LinkedIn/iMessage), with declared dimensions.
    const ogImg = page.locator('meta[property="og:image"]')
    await expect(ogImg).toHaveAttribute('content', /^https:\/\/.+\/og-image\.png$/)
    await expect(page.locator('meta[property="og:image:width"]')).toHaveAttribute('content', '1200')
    await expect(page.locator('meta[property="og:image:height"]')).toHaveAttribute('content', '630')
    await expect(page.locator('meta[name="twitter:card"]')).toHaveAttribute('content', 'summary_large_image')
    await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', /scalpel-pdf\.netlify\.app/)
    await expect(page.locator('link[rel="alternate"][hreflang="he"]')).toHaveCount(1)

    // JSON-LD parses and declares the app + FAQ for SEO/AEO/GEO.
    const ld = JSON.parse(await page.locator('script[type="application/ld+json"]').textContent())
    const types = ld['@graph'].map((n) => n['@type'])
    expect(types).toContain('SoftwareApplication')
    expect(types).toContain('FAQPage')
    const faq = ld['@graph'].find((n) => n['@type'] === 'FAQPage')
    expect(faq.mainEntity.length).toBeGreaterThanOrEqual(6)
  })

  test('FAQ section renders eight crawlable Q&A items', async ({ page }) => {
    await page.goto('/')
    await expect(page.locator('#faq')).toBeVisible()
    await expect(page.locator('#faq .faqitem')).toHaveCount(8)
    // Answers are in the DOM (crawlable) even while collapsed.
    await expect(page.locator('#faq .faqitem p').first()).not.toBeEmpty()
  })

  test('SEO/GEO infra files are served', async ({ request }) => {
    for (const [path, needle] of [
      ['/robots.txt', 'Sitemap:'],
      ['/sitemap.xml', '<loc>'],
      ['/llms.txt', '# Scalpel'],
      ['/site.webmanifest', 'Scalpel'],
    ]) {
      const res = await request.get(path)
      expect(res.ok(), `${path} should be 200`).toBeTruthy()
      expect(await res.text()).toContain(needle)
    }
    const og = await request.get('/og-image.png')
    expect(og.ok()).toBeTruthy()
    expect(og.headers()['content-type']).toContain('image/png')
  })

  test('no console errors on load', async ({ page }) => {
    const errors = []
    page.on('console', (m) => m.type() === 'error' && errors.push(m.text()))
    page.on('pageerror', (e) => errors.push(e.message))
    await page.goto('/')
    await page.waitForLoadState('networkidle')
    expect(errors).toEqual([])
  })
})
