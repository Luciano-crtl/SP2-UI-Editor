# Phase 5 in the game (flight or designer), with "Claude may edit" on. Nothing is saved; all is undone.   python Tests/live_phase5.py <folder>
import json, os, sys, time, urllib.request
S = json.load(open(os.path.join(os.environ['LOCALAPPDATA'], 'SP2UIEditor', 'session.json')))
out = sys.argv[1]

def api(path, raw=False, **b):
    q = urllib.request.Request('http://127.0.0.1:%d/api/%s' % (S['port'], path), json.dumps(b).encode(),
                               {'X-SP2UI-Key': S['key'], 'X-SP2UI-Client': 'claude', 'Content-Type': 'application/json'})
    try:
        d = urllib.request.urlopen(q, timeout=12).read()
    except urllib.error.HTTPError as e:
        d = e.read()
    return d if raw else json.loads(d)

bad = 0
def check(good, what, more=''):
    global bad
    bad += not good
    print('ok  ' if good else 'FAIL', what, more)

def shot(n):
    time.sleep(0.8)
    open(os.path.join(out, n + '.jpg'), 'wb').write(api('shot', raw=True, w=1366, clean=True))

st = api('state')
print(st['scene'], 'edit', st['claudeMayEdit'], 'changes', st['done'])
if not st['claudeMayEdit']:
    sys.exit('not allowed to edit')
start = st['done']
place = next(p for p in api('places') if p['name'] in ('flightMenu', 'designerBar'))
h = place['h']

def styles():
    time.sleep(0.5)
    d = api('styles', h=btn)
    return {s['class'] + ' ' + a['name']: a['value'] for s in d['styles'] for a in s['attributes']}, {c['name']: c['value'] for c in d['constants']}

try:
    btn = api('add', parent=h, xml='<Button class="btn btn-primary" width="150" height="40" minHeight="40"><Text text="Fase 5"/></Button>')['h']
    s0, c0 = styles()
    print(len(s0), 'style values,', len(c0), 'constants;', {k: v for k, v in s0.items() if 'olor' in k})
    shot('f5_0')
    r = api('style', h=btn, target='style:btn-primary', name='color', value='#2f9e5b')
    s1, _ = styles()
    check('ok' in r and s1.get('btn-primary color', '').lower() == '#2f9e5b', 'style changed', r)
    shot('f5_style')
    api('undo')
    s2, _ = styles()
    check(s2 == s0, 'style undone exactly')
    r = api('style', h=btn, target='const:ColorPrimary', value='#8a2be2')
    s3, c3 = styles()
    check('ok' in r and c3.get('ColorPrimary') == '#8a2be2', 'constant changed', r)
    check(any(s3[k] != s0[k] for k in s0), 'styles that use it followed', {k: (s0[k], s3[k]) for k in s0 if s3[k] != s0[k]})
    shot('f5_const')
    api('undo')
    s4, c4 = styles()
    check(s4 == s0 and c4 == c0, 'constant undone exactly', {k: (s0[k], s4[k]) for k in s0 if s4.get(k) != s0[k]})
    shot('f5_back')
    for t, n, v in (('style:btn-primary', 'color', 'banana'), ('style:btn-primary', 'nothing', '1'), ('style:none', 'color', '#fff000'), ('const:Nope', None, '1'), ('style:btn', 'onClick', 'x')):
        check('error' in api('style', h=btn, target=t, name=n, value=v), 'refused %s %s=%s' % (t, n, v))
    m = api('methods', h=btn)
    print(len(m), 'methods of', sorted({x['of'] for x in m}))
    safe = next((x for x in m if x['method'].startswith('ToggleWindSettings')), m[0] if m else None)
    check(bool(safe), 'methods are listed', safe)
    check('ok' in api('action', h=btn, kind='method', args=dict(root=safe['root'], method=safe['method'], **{'a%d' % i: '1' for i in range(len(safe['args']))})) if safe else False, 'a method is bound (not called)')
    for sig in ('QuitGame()', 'SaveCraft(string x)', 'Nothing()'):
        check('error' in api('action', h=btn, kind='method', args=dict(root='game', method=sig)), 'refused method ' + sig)
finally:
    while api('state')['done'] > start:
        r = api('undo')
        if 'error' in r:
            print('     undo:', r)
pk = [p for p in api('packages')['packages'] if p['ops']]
if pk:
    r = api('export', name=pk[0]['name'])
    check('ok' in r and r['files'], 'export', r)
print('FAILED %d' % bad if bad else 'ALL GOOD')
