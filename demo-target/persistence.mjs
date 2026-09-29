import fs from 'node:fs';
import assert from 'node:assert/strict';
const base = 'http://backend:8080/api/monitors';
async function api(path = '', method = 'GET', body) {
  const response = await fetch(base + path, { method, headers: {'Content-Type':'application/json'}, body: body ? JSON.stringify(body) : undefined });
  assert.ok(response.ok, `HTTP ${response.status}`);
  return response.status === 204 ? undefined : response.json();
}
if (process.argv[2] === 'prepare') {
  assert.equal((await api()).length, 0, 'Tests must clean up all monitors they created');
  const monitor = await api('', 'POST', { url:'http://demo-target:8080/healthy', email:'persistence@example.test' });
  await api(`/${monitor.id}`, 'PATCH', {paused:true});
  const checks = await api(`/${monitor.id}/checks`);
  fs.writeFileSync('/tmp/persistence.json', JSON.stringify({id:monitor.id, checkId:checks[0].id}));
  console.log('Prepared isolated persistence probe');
} else {
  const {id, checkId} = JSON.parse(fs.readFileSync('/tmp/persistence.json','utf8'));
  const monitor = await api(`/${id}`);
  assert.equal(monitor.paused, true);
  assert.equal((await api(`/${id}/checks`))[0].id, checkId);
  await api(`/${id}`, 'DELETE');
  console.log('PASS: monitor, paused state and history survived API/PostgreSQL restart');
}
