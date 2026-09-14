// Vercel Serverless Function: /api/stats
// Returns aggregated statistics and visitor logs for Admin Dashboard

export default async function handler(req, res) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

  if (req.method === 'OPTIONS') {
    return res.status(200).end();
  }

  const events = global.__gpb_events || [];

  // Compute aggregated metrics
  const totalVisits = events.filter(e => e.eventType === 'pageview').length;
  const fbVisits = events.filter(e => e.isFromFacebook || e.utmSource === 'facebook').length;
  const downloads = events.filter(e => e.eventType === 'download_click').length;
  const conversionRate = totalVisits > 0 ? ((downloads / totalVisits) * 100).toFixed(1) : 0;

  // Breakdown by OS & Device
  const devices = {};
  const locations = {};
  events.forEach(e => {
    const dev = e.deviceType || 'Desktop';
    devices[dev] = (devices[dev] || 0) + 1;

    const loc = e.location || 'Vietnam';
    locations[loc] = (locations[loc] || 0) + 1;
  });

  return res.status(200).json({
    status: 'ok',
    metrics: {
      totalVisits: totalVisits,
      fbVisits: fbVisits,
      downloads: downloads,
      conversionRate: `${conversionRate}%`
    },
    devices: devices,
    locations: locations,
    latestEvents: events.slice(0, 100)
  });
}
