import { test, expect } from '@playwright/test'

// Exercise production-host gating against the local built bundle.
test('optional analytics require consent, mask inputs, and stop after withdrawal', async ({ page, context }) => {
  const tracking = []
  await page.route('https://scalpel-pdf.netlify.app/**', async route => {
    const url = new URL(route.request().url())
    const response = await context.request.get(`http://localhost:4173${url.pathname}${url.search}`)
    await route.fulfill({ response })
  })
  await page.route(/https:\/\/.*(googletagmanager\.com|google-analytics\.com|clarity\.ms)\//, async route => {
    tracking.push(route.request().url())
    await route.fulfill({ status: 200, contentType: 'application/javascript', body: '' })
  })
  await page.goto('https://scalpel-pdf.netlify.app/')
  await expect(page.locator('.sp-consent')).toBeVisible()
  expect(tracking).toEqual([])
  expect(await context.cookies()).toEqual([])
  await page.locator('[data-choice="denied"]').click()
  await page.reload()
  await expect(page.locator('.sp-consent')).toBeHidden()
  expect(tracking).toEqual([])
  await page.locator('.sp-consent-settings').click()
  await page.locator('[data-choice="granted"]').click()
  await expect.poll(() => tracking.length).toBe(2)
  expect(tracking.some(url => url.includes('G-R0T70Y3E1N'))).toBe(true)
  expect(tracking.some(url => url.includes('yqlz27igm4'))).toBe(true)
  const config = await page.evaluate(() => window.dataLayer.map(args => Array.from(args)))
  expect(config[0][2]).toMatchObject({ ad_storage: 'denied', ad_user_data: 'denied', ad_personalization: 'denied' })
  expect(config[2][2]).toMatchObject({ allow_google_signals: false, allow_ad_personalization_signals: false })
  await page.evaluate(() => {
    const form = document.createElement('form')
    form.innerHTML = '<input name="private">'
    document.body.append(form)
    document.cookie = '_ga=test;path=/'
    document.cookie = '_clck=test;path=/'
  })
  await expect(page.locator('input[name="private"]')).toHaveAttribute('data-clarity-mask', 'true')
  await expect(page.locator('form')).toHaveAttribute('data-clarity-mask', 'true')
  await page.locator('.sp-consent-settings').click()
  await page.locator('[data-choice="denied"]').click()
  await page.waitForLoadState('load')
  await expect(page.locator('.sp-consent-settings')).toBeVisible()
  tracking.length = 0
  await page.reload()
  await expect(page.locator('.sp-consent')).toBeHidden()
  expect(tracking).toEqual([])
  expect(await context.cookies()).toEqual([])
})
