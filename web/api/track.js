// Vercel Serverless Function: /api/track
// Captures visitors, Facebook referrals, and download events

// In-memory buffer for warm instances (persists during serverless lifecycle)
global.__gpb_events = global.__gpb_events || [];

export default async function handler(req, res) {
  // Allow CORS for flexible testing
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'POST, GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    return res.status(200).end();
  }

  try {
    let payload = {};
    if (req.method === 'POST') {
      if (typeof req.body === 'string') {
        try {
          payload = JSON.parse(req.body);
        } catch (e) {
          payload = { raw: req.body };
        }
      } else if (req.body) {
        payload = req.body;
      }
    } else if (req.method === 'GET') {
      payload = req.query;
    }

    // Extract geo-location and IP from Vercel Edge headers
    const forwardedFor = req.headers['x-forwarded-for'] || req.socket?.remoteAddress || 'Unknown IP';
    const clientIp = forwardedFor.split(',')[0].trim();
    const country = req.headers['x-vercel-ip-country'] || 'VN';
    const city = req.headers['x-vercel-ip-city'] ? decodeURIComponent(req.headers['x-vercel-ip-city']) : 'Vietnam';
    const region = req.headers['x-vercel-ip-country-region'] || '';

    const record = {
      id: 'ev_' + Date.now() + '_' + Math.random().toString(36).substring(2, 7),
      timestamp: new Date().toISOString(),
      timeFormatted: new Date().toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }),
      ip: clientIp,
      location: `${city}, ${country}`,
      eventType: payload.eventType || 'pageview',
      isFromFacebook: payload.isFromFacebook || false,
      fbclid: payload.fbclid || '',
      utmSource: payload.utmSource || (payload.isFromFacebook ? 'facebook' : 'direct'),
      utmMedium: payload.utmMedium || '',
      utmCampaign: payload.utmCampaign || '',
      referrer: payload.referrer || req.headers['referer'] || '',
      deviceType: payload.deviceType || 'Unknown',
      os: payload.os || 'Unknown',
      screen: payload.screen || '',
      userAgent: req.headers['user-agent'] || ''
    };

    // Store in global memory (keep latest 500 events)
    global.__gpb_events.unshift(record);
    if (global.__gpb_events.length > 500) {
      global.__gpb_events.length = 500;
    }

    // Optional Telegram notification if TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID are set in Vercel Env
    const botToken = process.env.TELEGRAM_BOT_TOKEN;
    const chatId = process.env.TELEGRAM_CHAT_ID;
    if (botToken && chatId) {
      const isDownload = record.eventType === 'download_click';
      const isFb = record.isFromFacebook || record.utmSource === 'facebook';

      // Send alert for downloads or Facebook visitors
      if (isDownload || isFb) {
        const title = isDownload ? '🔥 [GPB] CÓ LƯỢT TẢI FILE SETUP!' : '🎯 [GPB] KHÁCH VÀO TỪ FACEBOOK!';
        const msg = `${title}
• Thời gian: ${record.timeFormatted}
• Vị trí: ${record.location} (IP: ${record.ip})
• Thiết bị: ${record.deviceType} (${record.os})
• Nguồn: ${record.utmSource} ${record.fbclid ? '(Có FBCLID)' : ''}
• Link gốc: ${record.referrer || 'Trực tiếp'}`;

        fetch(`https://api.telegram.org/bot${botToken}/sendMessage`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ chat_id: chatId, text: msg })
        }).catch(() => {});
      }
    }

    return res.status(200).json({ status: 'ok', recorded: true, id: record.id });
  } catch (err) {
    console.error('Tracking error:', err);
    return res.status(200).json({ status: 'error', message: err.message });
  }
}
