# Phase 4 in the game, with "Claude may edit" on: a panel with a section, a line, a toggle and a slider tied to Armor Fragments settings, and a
# button that opens it. Nothing is saved; everything is undone at the end (pass "keep" to leave it).   python Tests/live_panel.py <folder> [keep]
import base64, json, os, sys, time, urllib.request
S = json.load(open(os.path.join(os.environ['LOCALAPPDATA'], 'SP2UIEditor', 'session.json')))
out, keep = sys.argv[1], 'keep' in sys.argv

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
    print('ok  ' if good else 'FAIL', what, more if not good or more else '')

def shot(name):
    time.sleep(0.8)
    open(os.path.join(out, name + '.jpg'), 'wb').write(api('shot', raw=True, w=1366, clean=True))

st = api('state')
print(st['scene'], 'edit', st['claudeMayEdit'], 'changes', st['done'], 'package', st['package'])
if not st['claudeMayEdit']:
    sys.exit('not allowed to edit')
start = st['done']
places = api('places')
print('places', places)
cfg = api('configs')
print(len(cfg), 'settings of', sorted({c['mod'] for c in cfg}))
arm = [c for c in cfg if c['mod'] == 'Armor Fragments']
flag = next((c for c in arm if c['key'] == 'DrawRays'), next((c for c in arm if c['kind'] == 'bool'), None))
number = next((c for c in arm if c['key'] == 'RayWidthScale'), next((c for c in arm if c['kind'] == 'number'), None))
try:
    r = api('panel_new', title='Painel de teste')
    check('ok' in r, 'new panel', r)
    uid, content = r['uid'], r.get('h')
    time.sleep(0.6)
    check(bool(content), 'its content is there')
    def add(xml, texts=None):
        r = api('add', parent=content, xml=xml, texts=texts)
        check('ok' in r, 'add ' + xml[:60], r if 'ok' not in r else '')
        return r.get('h')
    add('<Instance templateId="control-header" title="Minha secao"/>')
    line = add('<Instance templateId="control-text"/>', {'label-text': 'Valor', 'value-text': '...'})
    tog = add('<Instance templateId="control-toggle" label="Desenhar raios"/>')
    sli = add('<Instance templateId="control-slider" label="Escala"/>')
    btn = add('<Button class="btn btn-primary" height="48" minHeight="48"><Text text="Fechar painel"/></Button>')
    par = add('<Text text="Um paragrafo de explicacao." fontSize="18" wordWrapping="true" alignment="TopLeft"/>')
    A = lambda c: dict(plugin=c['plugin'], section=c['section'], key=c['key'])
    if flag and number:
        for h, a in ((tog, A(flag)), (sli, A(number)), (line, A(number))):
            r = api('action', h=h, kind='config', args=a)
            check('ok' in r, 'tied to ' + a['key'], r if 'ok' not in r else '')
        check('error' in api('action', h=tog, kind='config', args=A(number)), 'a toggle refuses a number')
        check('error' in api('action', h=sli, kind='config', args=A(flag)), 'a slider refuses an on/off')
    check('error' in api('action', h=par, kind='panel', args=dict(panel=uid)), 'a text refuses an action')
    check('ok' in api('action', h=btn, kind='panel', args=dict(panel=uid)), 'button opens and closes the panel')
    check('error' in api('action', h=btn, kind='link', args=dict(url='file:///c:/x')), 'a file link is refused')
    check('ok' in api('action', h=btn, kind='element', args=dict(h=str(par), how='toggle')), 'button shows and hides an element')
    check('ok' in api('action', h=btn, kind='panel', args=dict(panel=uid)), 'back to the panel')
    time.sleep(0.8)
    tree = api('tree', h=line, depth=3)
    print('     line shows', [k.get('text') for k in tree.get('kids', [])], 'setting is', number and number['value'])
    place = next((p for p in places if p['name'] in ('flightMenu', 'designerBar')), None)
    if place:
        r = api('add', parent=place['h'], index=0, xml='<Button class="btn btn-primary" width="150" height="50" minHeight="40"><Text text="Meu painel"/></Button>')
        check('ok' in r and 'ok' in api('action', h=r['h'], kind='panel', args=dict(panel=uid)), 'a button outside opens it')
    shot('panel_open')
    api('panel_close', uid=uid)
    time.sleep(0.8)
    r = api('panel_open', uid=uid)
    time.sleep(1.0)
    n = api('tree', h=r['h'], depth=1)
    check(n.get('n') == 6, 'opened again with everything in it', n.get('n'))
    acts = [api('get', h=k['h']).get('action', {}).get('kind') for k in n.get('kids', [])]
    check(acts == [None, 'config', 'config', 'config', 'panel', None] or not flag, 'actions are back', acts)
    shot('panel_again')
finally:
    if not keep:
        while api('state')['done'] > start:
            r = api('undo')
            if 'error' in r:
                print('     undo:', r)
time.sleep(0.6)
if not keep:
    check(not any(p['title'] == 'Painel de teste' for p in api('panels')), 'the panel is gone')
    check(api('find', text='Meu painel') == [], 'the button is gone')
    shot('panel_after')
print('FAILED %d' % bad if bad else 'ALL GOOD')
