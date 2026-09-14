/**
 * GAME PING BOOSTER — CLIENT SCRIPT & TRACKING ENGINE
 * Captures Facebook referrals, UTM parameters, device info & download events.
 */

(function () {
  'use strict';

  // 1. Parse URL parameters and referrer
  const urlParams = new URLSearchParams(window.location.search);
  const fbclid = urlParams.get('fbclid') || '';
  const utmSource = urlParams.get('utm_source') || '';
  const utmMedium = urlParams.get('utm_medium') || '';
  const utmCampaign = urlParams.get('utm_campaign') || '';
  const referrer = document.referrer || '';

  // Determine if visitor came from Facebook
  const isFromFacebook = !!fbclid ||
    utmSource.toLowerCase().includes('fb') ||
    utmSource.toLowerCase().includes('facebook') ||
    referrer.includes('facebook.com') ||
    referrer.includes('fb.com');

  // Device & browser detection
  const ua = navigator.userAgent;
  let deviceType = 'Desktop';
  if (/Mobi|Android|iPhone|iPod/i.test(ua)) {
    deviceType = 'Mobile';
  } else if (/iPad|Tablet/i.test(ua)) {
    deviceType = 'Tablet';
  }

  let os = 'Unknown OS';
  if (ua.indexOf('Win') !== -1) os = 'Windows';
  else if (ua.indexOf('Mac') !== -1) os = 'macOS';
  else if (ua.indexOf('Linux') !== -1) os = 'Linux';
  else if (ua.indexOf('Android') !== -1) os = 'Android';
  else if (ua.indexOf('like Mac') !== -1) os = 'iOS';

  // Unique session ID for deduplication
  let sessionId = sessionStorage.getItem('gpb_sid');
  if (!sessionId) {
    sessionId = 'sid_' + Math.random().toString(36).substring(2, 11) + '_' + Date.now();
    sessionStorage.setItem('gpb_sid', sessionId);
  }

  // 2. Function to transmit tracking data
  function sendEvent(eventType, extraData = {}) {
    const payload = {
      eventType: eventType, // 'pageview' | 'download_click'
      sessionId: sessionId,
      isFromFacebook: isFromFacebook,
      fbclid: fbclid,
      utmSource: utmSource || (isFromFacebook ? 'facebook' : 'direct'),
      utmMedium: utmMedium,
      utmCampaign: utmCampaign,
      referrer: referrer,
      url: window.location.href,
      deviceType: deviceType,
      os: os,
      screen: `${window.screen.width}x${window.screen.height}`,
      language: navigator.language || '',
      timestamp: new Date().toISOString(),
      ...extraData
    };

    // Also store locally in localStorage for offline/client admin viewing
    saveLocalEvent(payload);

    // Send to Vercel Serverless Function
    try {
      const dataStr = JSON.stringify(payload);
      if (navigator.sendBeacon) {
        navigator.sendBeacon('/api/track', dataStr);
      } else {
        fetch('/api/track', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: dataStr,
          keepalive: true
        }).catch(function () { /* ignore network error */ });
      }
    } catch (e) {
      console.warn('Tracking beacon skipped:', e);
    }
  }

  // Store in browser local storage so Admin page can read stats even without remote DB
  function saveLocalEvent(event) {
    try {
      const key = 'gpb_tracking_events';
      const events = JSON.parse(localStorage.getItem(key) || '[]');
      events.unshift(event);
      // Keep latest 200 events
      if (events.length > 200) events.length = 200;
      localStorage.setItem(key, JSON.stringify(events));
    } catch (e) {
      // quota or private mode
    }
  }

  // 3. Fire pageview on initial load
  sendEvent('pageview');

  // 4. Download click listeners
  const downloadBtns = document.querySelectorAll('.btn-download');
  downloadBtns.forEach(function (btn) {
    btn.addEventListener('click', function (e) {
      const fileName = 'GamePingBooster-Setup.exe';
      sendEvent('download_click', { fileName: fileName });
      showToast('ĐANG TẢI XUỐNG ' + fileName + '... VUI LÒNG MỞ FILE VÀ CÀI ĐẶT.');
    });
  });

  // 5. Toast Notification
  function showToast(msg) {
    const toast = document.getElementById('toast');
    const toastMsg = document.getElementById('toast-message');
    if (!toast || !toastMsg) return;
    toastMsg.textContent = msg;
    toast.style.display = 'block';
    setTimeout(function () {
      toast.style.display = 'none';
    }, 4500);
  }

  // 6. Copy Facebook sharing link helper
  const copyFbBtn = document.getElementById('link-copy-fb');
  if (copyFbBtn) {
    copyFbBtn.addEventListener('click', function (e) {
      e.preventDefault();
      const origin = window.location.origin;
      const shareUrl = `${origin}/?utm_source=facebook&utm_medium=post&utm_campaign=share_fb`;
      if (navigator.clipboard) {
        navigator.clipboard.writeText(shareUrl).then(function () {
          showToast('ĐÃ COPY LINK FACEBOOK: ' + shareUrl);
        });
      } else {
        prompt('Copy link chia sẻ Facebook dưới đây:', shareUrl);
      }
    });
  }

  // 7. FAQ Accordion Logic
  const faqQuestions = document.querySelectorAll('.faq-question');
  faqQuestions.forEach(function (btn) {
    btn.addEventListener('click', function () {
      const item = this.parentElement;
      const isActive = item.classList.contains('active');

      // Collapse other items
      document.querySelectorAll('.faq-item').forEach(function (other) {
        other.classList.remove('active');
        const icon = other.querySelector('.faq-icon');
        if (icon) icon.textContent = '+';
      });

      if (!isActive) {
        item.classList.add('active');
        const icon = item.querySelector('.faq-icon');
        if (icon) icon.textContent = '−';
      }
    });
  });

})();
