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
    await expect(page.locator('#tools .tool')).toHaveCount(6)
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

  test('no console errors on load', async ({ page }) => {
    const errors = []
    page.on('console', (m) => m.type() === 'error' && errors.push(m.text()))
    page.on('pageerror', (e) => errors.push(e.message))
    await page.goto('/')
    await page.waitForLoadState('networkidle')
    expect(errors).toEqual([])
  })
})
