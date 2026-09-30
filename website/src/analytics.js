/* Optional analytics. No GA4/Clarity requests before an affirmative choice. */
function initAnalytics() {
  'use strict';
  if (!['scalpel-pdf.netlify.app'].includes(location.hostname)) return;
  var key = 'scalpel-analytics-consent-v1', choice = null, started = false;
  try { choice = localStorage.getItem(key); if (!['granted','denied'].includes(choice)) choice = null; } catch (_) {}
  function script(src) { var s = document.createElement('script'); s.async = true; s.src = src; document.head.appendChild(s); }
  function maskInputs() { document.querySelectorAll('input,textarea,select,[contenteditable],form').forEach(function(el){el.setAttribute('data-clarity-mask','true');}); }
  function start() {
    if (started) return;
    started = true;
    maskInputs(); new MutationObserver(maskInputs).observe(document.body, {childList:true,subtree:true});
    window.dataLayer = window.dataLayer || [];
    window.gtag = function () { window.dataLayer.push(arguments); };
    gtag('consent', 'default', {analytics_storage:'granted', ad_storage:'denied', ad_user_data:'denied', ad_personalization:'denied'});
    gtag('js', new Date());
    var clean = new URL(location.origin + location.pathname);
    ['utm_source','utm_medium','utm_campaign','utm_content','utm_term'].forEach(function (name) { var value = new URLSearchParams(location.search).get(name); if (value) clean.searchParams.set(name, value); });
    window['ga-disable-G-R0T70Y3E1N'] = false;
    gtag('config', 'G-R0T70Y3E1N', {allow_google_signals:false, allow_ad_personalization_signals:false, page_location:clean.href, page_referrer:document.referrer ? document.referrer.split('?')[0].split('#')[0] : ''});
    script('https://www.googletagmanager.com/gtag/js?id=G-R0T70Y3E1N');
    window.clarity = window.clarity || function () { (window.clarity.q = window.clarity.q || []).push(arguments); };
    clarity('consentv2', {analytics_Storage:'granted', ad_Storage:'denied'});
    script('https://www.clarity.ms/tag/yqlz27igm4');
    var seen = new Set();
    window.addEventListener('scroll', function () {
      var total = document.documentElement.scrollHeight - innerHeight;
      if (total <= 0) return;
      var depth = Math.min(100, Math.round(scrollY / total * 100));
      [25,50,75,100].forEach(function (n) { if (depth >= n && !seen.has(n)) { seen.add(n); gtag('event','scroll_depth',{percent_scrolled:n}); } });
    }, {passive:true});
  }
  function remember(value) { try { localStorage.setItem(key,value); } catch (_) {} choice = value; }
  function clearCookies() {
    document.cookie.split(';').forEach(function (row) {
      var name = row.trim().split('=')[0];
      if (!/^(_ga|_gid|_gat|_clck|_clsk)/.test(name)) return;
      ['',location.hostname,'.'+location.hostname].forEach(function (domain) { document.cookie = name + '=; Max-Age=0; path=/; SameSite=Lax' + (domain ? '; domain='+domain : ''); });
    });
  }
  var box = document.createElement('section'); box.className = 'sp-consent'; box.setAttribute('aria-label','Website analytics choice');
  box.innerHTML = '<strong data-i18n="consent.title">Website analytics, only if you agree</strong><p data-i18n="consent.body">Google Analytics and Microsoft Clarity measure visits, clicks and scrolling, and record how this website is used. They use cookies; input fields are masked. This does not affect the offline desktop app. You can refuse or change your choice at any time.</p><div class="sp-consent-actions"><button type="button" data-choice="granted" data-i18n="consent.accept">Allow analytics and recordings</button><button type="button" data-choice="denied" data-i18n="consent.reject">Refuse analytics</button><a href="#website-privacy" data-i18n="consent.policy">Website privacy</a></div>';
  box.hidden = !!choice; document.body.appendChild(box);
  box.addEventListener('click', function (event) {
    var button = event.target.closest('button'); if (!button) return;
    var value = button.dataset.choice;
    if (!value) return;
    remember(value); box.hidden = true;
    if (value === 'granted') start();
    else {
      window['ga-disable-G-R0T70Y3E1N'] = true;
      if (window.gtag) gtag('consent','update',{analytics_storage:'denied',ad_storage:'denied',ad_user_data:'denied',ad_personalization:'denied'});
      if (window.clarity) clarity('consentv2',{analytics_Storage:'denied',ad_Storage:'denied'});
      clearCookies(); if (started) location.reload();
    }
  });
  var settings = document.createElement('button'); settings.type='button'; settings.className='sp-consent-settings'; settings.setAttribute('data-i18n','consent.settings'); settings.textContent='Privacy preferences'; settings.addEventListener('click',function(){box.hidden=false;box.querySelector('button').focus();}); document.body.appendChild(settings);
  if (choice === 'granted') start();
}

export { initAnalytics };

