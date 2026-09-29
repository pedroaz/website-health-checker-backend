import http from 'node:http';
http.createServer((req, res) => {
  const path = new URL(req.url, 'http://demo-target:8080').pathname;
  if (path === '/slow') { const timer = setTimeout(() => res.end('slow'), 15000); res.on('close', () => clearTimeout(timer)); return; }
  if (path === '/redirect') { res.writeHead(302, { Location: '/healthy' }); res.end(); return; }
  if (path === '/redirect-private') { res.writeHead(302, { Location: 'http://127.0.0.1:8080/health/live' }); res.end(); return; }
  if (path === '/loop') { res.writeHead(302, { Location: '/loop' }); res.end(); return; }
  res.writeHead(path === '/healthy' ? 200 : 503, { 'Content-Type': 'text/plain' });
  res.end(path === '/healthy' ? 'healthy' : 'unhealthy');
}).listen(8080, '0.0.0.0');
