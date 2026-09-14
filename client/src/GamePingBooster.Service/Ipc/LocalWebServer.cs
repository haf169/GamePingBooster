using System.Net;
using System.Text;
using System.Text.Json;
using GamePingBooster.Core.Ipc;
using GamePingBooster.Service.Tunnel;

namespace GamePingBooster.Service.Ipc;

/// <summary>
/// Embedded HTTP server listening on http://127.0.0.1:51821/ and http://localhost:51821/.
/// Enables web-based browser activation and status monitoring without requiring an IDE or Avalonia UI.
/// Supports CORS so that both local browser sessions and cloud web portals (e.g. Vercel) can control the booster.
/// </summary>
internal sealed class LocalWebServer
{
    public const int DefaultPort = 51821;
    private readonly TunnelEngine _engine;
    private readonly Action<string> _log;

    public LocalWebServer(TunnelEngine engine, Action<string> log)
    {
        _engine = engine;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://127.0.0.1:{DefaultPort}/");
            listener.Prefixes.Add($"http://localhost:{DefaultPort}/");
            listener.Start();
            _log($"Local Web Server listening on http://127.0.0.1:{DefaultPort}/ and http://localhost:{DefaultPort}/");
        }
        catch (Exception ex)
        {
            _log($"Could not start Local Web Server on port {DefaultPort} ({ex.Message}). Continuing without local web dashboard.");
            return;
        }

        using var reg = ct.Register(() =>
        {
            try { listener.Stop(); } catch { }
        });

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                _log($"Web server error: {ex.Message}");
                try
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var req = context.Request;
        var res = context.Response;

        // Apply CORS headers to allow cross-origin calls from the web landing page (e.g. on Vercel)
        res.Headers["Access-Control-Allow-Origin"] = "*";
        res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept, Authorization";

        if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            res.StatusCode = 204;
            try { res.Close(); } catch { }
            return;
        }

        var path = req.Url?.AbsolutePath ?? "/";
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }

        try
        {
            switch (path.ToLowerInvariant())
            {
                case "":
                case "/":
                case "/index.html":
                case "/activate":
                case "/activate.html":
                    if (string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(req.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        await ServeDashboardHtmlAsync(res).ConfigureAwait(false);
                        return;
                    }
                    break;

                case "/styles.css":
                    if (string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(req.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        await ServeCssAsync(res).ConfigureAwait(false);
                        return;
                    }
                    break;

                case "/api/status":
                    if (string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        var status = _engine.Snapshot();
                        var json = JsonSerializer.Serialize(status, IpcJsonContext.Default.StatusMessage);
                        await SendResponseAsync(res, 200, "application/json; charset=utf-8", json).ConfigureAwait(false);
                        return;
                    }
                    break;

                case "/api/connect":
                    if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _engine.ConnectAsync(null, null, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _log($"Web connect failed: {ex.Message}");
                            }
                        }, ct);
                        await SendResponseAsync(res, 200, "application/json; charset=utf-8", "{\"ok\":true}").ConfigureAwait(false);
                        return;
                    }
                    break;

                case "/api/disconnect":
                    if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        await _engine.DisconnectAsync().ConfigureAwait(false);
                        await SendResponseAsync(res, 200, "application/json; charset=utf-8", "{\"ok\":true}").ConfigureAwait(false);
                        return;
                    }
                    break;

                case "/api/toggle":
                    if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        var current = _engine.Snapshot().State;
                        if (current is TunnelState.Connected or TunnelState.Connecting)
                        {
                            await _engine.DisconnectAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _engine.ConnectAsync(null, null, ct).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    _log($"Web toggle connect failed: {ex.Message}");
                                }
                            }, ct);
                        }
                        await SendResponseAsync(res, 200, "application/json; charset=utf-8", "{\"ok\":true}").ConfigureAwait(false);
                        return;
                    }
                    break;

                case "/api/reload-profile":
                    if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await _engine.LoadProfileAsync(ct).ConfigureAwait(false);
                            _log("Profile reloaded via web API.");
                            await SendResponseAsync(res, 200, "application/json; charset=utf-8", "{\"ok\":true}").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _log($"Reloading profile failed: {ex.Message}");
                            await SendResponseAsync(res, 500, "application/json; charset=utf-8", $"{{\"ok\":false,\"error\":\"{ex.Message.Replace("\"", "'")}\"}}").ConfigureAwait(false);
                        }
                        return;
                    }
                    break;
            }

            await SendResponseAsync(res, 404, "application/json; charset=utf-8", "{\"error\":\"Not Found\"}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log($"HTTP request processing error: {ex.Message}");
            try
            {
                await SendResponseAsync(res, 500, "application/json; charset=utf-8", "{\"error\":\"Internal Server Error\"}").ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static async Task ServeDashboardHtmlAsync(HttpListenerResponse res)
    {
        // If an activate.html file exists locally, use it; otherwise fallback to embedded UI
        string? localPath = null;
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "web", "activate.html"),
            Path.Combine(AppContext.BaseDirectory, "activate.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "web", "activate.html")
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                localPath = c;
                break;
            }
        }

        string html;
        if (localPath is not null)
        {
            html = await File.ReadAllTextAsync(localPath).ConfigureAwait(false);
        }
        else
        {
            html = EmbeddedDashboardHtml;
        }

        await SendResponseAsync(res, 200, "text/html; charset=utf-8", html).ConfigureAwait(false);
    }

    private static async Task ServeCssAsync(HttpListenerResponse res)
    {
        string? localPath = null;
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "web", "styles.css"),
            Path.Combine(AppContext.BaseDirectory, "styles.css"),
            Path.Combine(Directory.GetCurrentDirectory(), "web", "styles.css")
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                localPath = c;
                break;
            }
        }

        string css = localPath is not null ? await File.ReadAllTextAsync(localPath).ConfigureAwait(false) : "";
        await SendResponseAsync(res, 200, "text/css; charset=utf-8", css).ConfigureAwait(false);
    }

    private static async Task SendResponseAsync(HttpListenerResponse res, int statusCode, string contentType, string body)
    {
        try
        {
            res.StatusCode = statusCode;
            res.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(body);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch
        {
            // Client closed connection prematurely
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    private const string EmbeddedDashboardHtml = """
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Bảng Kích Hoạt — Game Ping Booster</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&family=Playfair+Display:ital,wght@0,600;0,800;1,400&family=Source+Serif+4:ital,opsz,wght@0,8..60,400;0,8..60,600;1,8..60,400&display=swap" rel="stylesheet">
  <style>
    :root {
      --bg: #000000;
      --text: #FFFFFF;
      --border: #FFFFFF;
      --muted: #888888;
      --font-display: 'Playfair Display', Georgia, serif;
      --font-body: 'Source Serif 4', Georgia, serif;
      --font-mono: 'JetBrains Mono', monospace;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background: var(--bg);
      color: var(--text);
      font-family: var(--font-body);
      font-size: 16px;
      line-height: 1.5;
      padding: 0;
      min-height: 100vh;
      display: flex;
      flex-direction: column;
    }
    header {
      border-bottom: 1px solid var(--border);
      padding: 1.5rem 2rem;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .logo {
      font-family: var(--font-display);
      font-size: 1.25rem;
      font-weight: 800;
      letter-spacing: -0.02em;
      text-transform: uppercase;
    }
    .status-pill {
      font-family: var(--font-mono);
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.1em;
      padding: 0.35rem 0.75rem;
      border: 1px solid var(--border);
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
    }
    .status-dot {
      width: 8px;
      height: 8px;
      background: var(--text);
      display: inline-block;
    }
    .status-dot.pulsing {
      animation: pulse 1s infinite alternate;
    }
    @keyframes pulse {
      0% { opacity: 0.2; }
      100% { opacity: 1; }
    }
    main {
      flex: 1;
      max-width: 1000px;
      width: 100%;
      margin: 0 auto;
      padding: 3rem 1.5rem;
      display: flex;
      flex-direction: column;
      gap: 2.5rem;
    }
    .hero-banner {
      border: 1px solid var(--border);
      padding: 2.5rem;
      text-align: center;
      background: #050505;
    }
    .hero-title {
      font-family: var(--font-display);
      font-size: 2.75rem;
      font-weight: 800;
      line-height: 1.1;
      margin-bottom: 1rem;
    }
    .hero-desc {
      color: var(--muted);
      max-width: 600px;
      margin: 0 auto 2rem;
      font-size: 1.1rem;
    }
    .toggle-btn {
      font-family: var(--font-mono);
      font-size: 1.1rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.1em;
      padding: 1.2rem 2.5rem;
      border: 2px solid var(--border);
      background: var(--text);
      color: var(--bg);
      cursor: pointer;
      border-radius: 0;
      transition: none;
      display: inline-block;
    }
    .toggle-btn:hover {
      background: var(--bg);
      color: var(--text);
    }
    .toggle-btn.active {
      background: var(--bg);
      color: var(--text);
      border-color: var(--border);
    }
    .toggle-btn.active:hover {
      background: var(--text);
      color: var(--bg);
    }
    .metrics-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      border-top: 1px solid var(--border);
      border-left: 1px solid var(--border);
    }
    .metric-card {
      border-right: 1px solid var(--border);
      border-bottom: 1px solid var(--border);
      padding: 1.75rem 1.5rem;
    }
    .metric-label {
      font-family: var(--font-mono);
      font-size: 0.75rem;
      text-transform: uppercase;
      color: var(--muted);
      letter-spacing: 0.08em;
      margin-bottom: 0.5rem;
    }
    .metric-val {
      font-family: var(--font-mono);
      font-size: 2.25rem;
      font-weight: 700;
      line-height: 1;
    }
    .metric-sub {
      font-size: 0.8rem;
      color: var(--muted);
      margin-top: 0.4rem;
    }
    .info-table {
      width: 100%;
      border-collapse: collapse;
      border: 1px solid var(--border);
      font-family: var(--font-mono);
      font-size: 0.85rem;
    }
    .info-table td {
      padding: 0.85rem 1.25rem;
      border-bottom: 1px solid #222222;
    }
    .info-table tr:last-child td {
      border-bottom: none;
    }
    .info-table td:first-child {
      color: var(--muted);
      width: 35%;
      border-right: 1px solid #222222;
    }
    .actions-row {
      display: flex;
      gap: 1rem;
      justify-content: flex-end;
    }
    .btn-secondary {
      font-family: var(--font-mono);
      font-size: 0.8rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      padding: 0.75rem 1.25rem;
      border: 1px solid var(--border);
      background: transparent;
      color: var(--text);
      cursor: pointer;
      border-radius: 0;
    }
    .btn-secondary:hover {
      background: var(--text);
      color: var(--bg);
    }
    footer {
      border-top: 1px solid var(--border);
      padding: 1.5rem 2rem;
      font-family: var(--font-mono);
      font-size: 0.75rem;
      color: var(--muted);
      display: flex;
      justify-content: space-between;
    }
    @media (max-width: 768px) {
      .hero-title { font-size: 2rem; }
      .metrics-grid { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <header>
    <div class="logo">Game Ping Booster</div>
    <div class="status-pill" id="global-status-pill">
      <span class="status-dot pulsing" id="status-dot"></span>
      <span id="status-text">ĐANG KẾT NỐI LOCALHOST...</span>
    </div>
  </header>

  <main>
    <section class="hero-banner">
      <div class="metric-label" style="margin-bottom: 0.75rem;">BẢNG ĐIỀU KHIỂN TRỰC TIẾP TRÌNH DUYỆT</div>
      <h1 class="hero-title" id="main-heading">Kích Hoạt Tối Ưu Mạng</h1>
      <p class="hero-desc" id="main-subtext">Hệ thống định tuyến gói tin qua Wintun TUN adapter trực tiếp trên máy của bạn. Không cần cài IDE hay terminal.</p>
      <button class="toggle-btn" id="toggle-btn" onclick="toggleBooster()">BẬT BOOSTER NGAY</button>
    </section>

    <div class="metrics-grid">
      <div class="metric-card">
        <div class="metric-label">Ping Game (Server)</div>
        <div class="metric-val" id="metric-game-ping">-- <span style="font-size: 1rem;">ms</span></div>
        <div class="metric-sub" id="metric-game-ping-sub">Đo trực tiếp tới server game</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">Ping Relay Tunnel</div>
        <div class="metric-val" id="metric-relay-ping">-- <span style="font-size: 1rem;">ms</span></div>
        <div class="metric-sub" id="metric-relay-ping-sub">RTT qua giao thức WireGuard</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">Packet Loss</div>
        <div class="metric-val" id="metric-loss">0.0<span style="font-size: 1rem;">%</span></div>
        <div class="metric-sub">Tỉ lệ rớt gói tin ước tính</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">Game Đang Chạy</div>
        <div class="metric-val" id="metric-game-name" style="font-size: 1.35rem; line-height: 1.2; word-break: break-word;">Chờ Game...</div>
        <div class="metric-sub" id="metric-game-sub">Tự động phát hiện tiến trình</div>
      </div>
    </div>

    <table class="info-table">
      <tr>
        <td>TRẠNG THÁI HỆ THỐNG</td>
        <td id="table-state">Đang kiểm tra...</td>
      </tr>
      <tr>
        <td>CHI TIẾT KẾT NỐI</td>
        <td id="table-detail">Khởi tạo giao tiếp IPC/HTTP...</td>
      </tr>
      <tr>
        <td>RELAY NODE</td>
        <td id="table-relay">--</td>
      </tr>
      <tr>
        <td>DẢI ROUTING HOẠT ĐỘNG</td>
        <td id="table-routes">0 dải IP trong bảng định tuyến Windows</td>
      </tr>
      <tr>
        <td>LƯU LƯỢNG GÓI TIN</td>
        <td id="table-packets">Gửi: 0 | Nhận: 0</td>
      </tr>
    </table>

    <div class="actions-row">
      <button class="btn-secondary" onclick="reloadProfile()">Nạp Lại Profile Cấu Hình</button>
      <button class="btn-secondary" onclick="fetchStatus()">Làm Mới Trạng Thái</button>
    </div>
  </main>

  <footer>
    <div>GAME PING BOOSTER — CỤC BỘ (PORT 51821)</div>
    <div>ZERO LICENCE • FULL FREEDOM</div>
  </footer>

  <script>
    const API_BASE = window.location.origin.includes(':51821') 
      ? window.location.origin 
      : 'http://127.0.0.1:51821';

    let isOperating = false;

    async function fetchStatus() {
      try {
        const res = await fetch(`${API_BASE}/api/status`, { cache: 'no-store' });
        if (!res.ok) throw new Error('Status request failed');
        const data = await res.json();
        updateUI(data);
      } catch (err) {
        document.getElementById('global-status-pill').innerHTML = '<span class="status-dot" style="background: #888;"></span> CHƯA KẾT NỐI DỊCH VỤ CỤC BỘ';
        document.getElementById('table-state').textContent = 'Chưa mở file gpb-service.exe hoặc dịch vụ chưa khởi chạy';
        document.getElementById('table-detail').textContent = 'Hãy chắc chắn bạn đã chạy file setup hoặc mở dịch vụ GamePingBooster.';
      }
    }

    function updateUI(data) {
      // TunnelState: 0=Disconnected, 1=Connecting, 2=Connected, 3=Reconnecting, 4=Faulted
      const states = ['CHƯA BẬT (DISCONNECTED)', 'ĐANG THIẾT LẬP (CONNECTING)', 'ĐANG TỐI ƯU (CONNECTED)', 'ĐANG KẾT NỐI LẠI (RECONNECTING)', 'LỖI (FAULTED)'];
      const stateName = states[data.state] || 'KHÔNG XÁC ĐỊNH';

      const dot = document.getElementById('status-dot');
      const pill = document.getElementById('status-text');
      const toggleBtn = document.getElementById('toggle-btn');

      pill.textContent = stateName;
      document.getElementById('table-state').textContent = stateName;
      document.getElementById('table-detail').textContent = data.detail || 'Sẵn sàng hoạt động.';

      if (data.state === 2) { // Connected
        dot.style.background = '#FFFFFF';
        dot.classList.remove('pulsing');
        toggleBtn.textContent = 'NGẮT KẾT NỐI BOOSTER';
        toggleBtn.className = 'toggle-btn active';
        document.getElementById('main-heading').textContent = 'Booster Đang Hoạt Động';
        document.getElementById('main-subtext').textContent = 'Đường truyền game đang được chuyển hướng qua mạng tối ưu tốc độ cao.';
      } else if (data.state === 1 || data.state === 3) { // Connecting / Reconnecting
        dot.style.background = '#FFFFFF';
        dot.classList.add('pulsing');
        toggleBtn.textContent = 'ĐANG KẾT NỐI...';
        toggleBtn.className = 'toggle-btn';
        document.getElementById('main-heading').textContent = 'Đang Thiết Lập Đường Truyền';
      } else { // Disconnected or Faulted
        dot.style.background = '#888888';
        dot.classList.remove('pulsing');
        toggleBtn.textContent = 'KÍCH HOẠT BOOSTER';
        toggleBtn.className = 'toggle-btn';
        document.getElementById('main-heading').textContent = 'Sẵn Sàng Kích Hoạt';
        document.getElementById('main-subtext').textContent = 'Nhấn nút bên dưới để tối ưu ping ngay lập tức cho các tựa game đã hỗ trợ.';
      }

      // Ping metrics
      if (data.gamePingMs !== null && data.gamePingMs !== undefined) {
        document.getElementById('metric-game-ping').innerHTML = `${Math.round(data.gamePingMs)} <span style="font-size: 1rem;">ms</span>`;
        document.getElementById('metric-game-ping-sub').textContent = data.gamePingDirect ? 'Đo trực tiếp tới server' : 'Ước tính qua landmark';
      } else {
        document.getElementById('metric-game-ping').innerHTML = `-- <span style="font-size: 1rem;">ms</span>`;
        document.getElementById('metric-game-ping-sub').textContent = 'Chưa có mẫu đo';
      }

      if (data.tunnelPingMs !== null && data.tunnelPingMs !== undefined) {
        document.getElementById('metric-relay-ping').innerHTML = `${Math.round(data.tunnelPingMs)} <span style="font-size: 1rem;">ms</span>`;
      } else {
        document.getElementById('metric-relay-ping').innerHTML = `-- <span style="font-size: 1rem;">ms</span>`;
      }

      if (data.lossRatio !== null && data.lossRatio !== undefined) {
        document.getElementById('metric-loss').innerHTML = `${(data.lossRatio * 100).toFixed(1)}<span style="font-size: 1rem;">%</span>`;
      } else {
        document.getElementById('metric-loss').innerHTML = `0.0<span style="font-size: 1rem;">%</span>`;
      }

      // Game & routes
      if (data.gameRunning && data.gameName) {
        document.getElementById('metric-game-name').textContent = data.gameName;
        document.getElementById('metric-game-sub').textContent = `Đang chạy • ${data.gameRegionName || 'Tối ưu tự động'}`;
      } else {
        document.getElementById('metric-game-name').textContent = 'Chờ vào game...';
        document.getElementById('metric-game-sub').textContent = 'Tự động bắt gói khi mở game';
      }

      document.getElementById('table-relay').textContent = data.relayName 
        ? `${data.relayName} (${data.relayAddress || 'Tự chọn theo ping'})` 
        : (data.relayEndpoints && data.relayEndpoints.length ? data.relayEndpoints.join(', ') : 'Mặc định (Auto)');

      document.getElementById('table-routes').textContent = `${data.activeRoutes || 0} dải IP đang được định tuyến qua Wintun`;
      document.getElementById('table-packets').textContent = `Gửi: ${(data.packetsSent || 0).toLocaleString()} | Nhận: ${(data.packetsReceived || 0).toLocaleString()}`;
    }

    async function toggleBooster() {
      if (isOperating) return;
      isOperating = true;
      try {
        await fetch(`${API_BASE}/api/toggle`, { method: 'POST' });
        await fetchStatus();
      } catch (err) {
        alert('Không thể gửi lệnh điều khiển đến dịch vụ cục bộ: ' + err.message);
      } finally {
        isOperating = false;
      }
    }

    async function reloadProfile() {
      try {
        const res = await fetch(`${API_BASE}/api/reload-profile`, { method: 'POST' });
        if (res.ok) {
          alert('Đã nạp lại danh sách profile dải game thành công!');
          await fetchStatus();
        } else {
          alert('Nạp lại profile thất bại.');
        }
      } catch (err) {
        alert('Lỗi: ' + err.message);
      }
    }

    // Auto poll status every 1.5 seconds
    fetchStatus();
    setInterval(fetchStatus, 1500);
  </script>
</body>
</html>
""";
}
