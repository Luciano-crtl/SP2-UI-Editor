# Tries every kind of live edit on the Armor view button of the designer (the game must be there, with "Claude may edit" on) and undoes it all.
#   python Tests/live_test.py <folder for the pictures>
import json, os, sys, time, urllib.request
out = sys.argv[1]
S = json.load(open(os.path.join(os.environ['LOCALAPPDATA'], 'SP2UIEditor', 'session.json')))

def api(path, raw=False, **b):
    q = urllib.request.Request('http://127.0.0.1:%d/api/%s' % (S['port'], path), json.dumps(b).encode(),
                               {'X-SP2UI-Key': S['key'], 'X-SP2UI-Client': 'claude', 'Content-Type': 'application/json'})
    try:
        d = urllib.request.urlopen(q, timeout=12).read()
    except urllib.error.HTTPError as e:
        d = e.read()
    return d if raw else json.loads(d)

def shot(name):
    time.sleep(0.6)
    open(os.path.join(out, name + '.jpg'), 'wb').write(api('shot', raw=True, w=1366, clean=True))

def look(h):
    time.sleep(0.4)
    d = api('get', h=h)
    return {'rect': {k: round(v, 4) for k, v in d['rect'].items()}, 'hidden': d.get('hidden'), 'classes': d['classes'], 'index': d['index'],
            'siblings': d['siblings'], 'own': {a['name']: a['value'] for a in d['attributes'] if a.get('own')}}

bad = 0
def check(good, what, more=''):
    global bad
    bad += not good
    print('ok  ' if good else 'FAIL', what, more)

st = api('state')
print(st['scene'], 'claudeMayEdit', st['claudeMayEdit'], 'changes', st['done'])
if not st['claudeMayEdit']:
    sys.exit('not allowed to edit')
start = st['done']
f = [x for x in api('find', text='button-armor-view') if x.get('id') == 'button-armor-view']
if not f:
    sys.exit('the button is not on the screen')
h = f[0]['h']
d = api('get', h=h)
parent = d['ancestors'][-1]['h']
print('src', d.get('src'))
first, pfirst = look(h), look(parent)
shot('0_before')

def undo_all():
    while api('state')['done'] > start:
        r = api('undo')
        if 'error' in r:
            print('     undo problem:', r)

def trial(name, steps, picture=True):
    before, pb = look(h), look(parent)
    n = 0
    for path, args in steps:
        r = api(path, **args)
        if 'error' in r:
            check(False, name, r['error'])
        else:
            n += 1
    after, pa = look(h), look(parent)
    check(after != before or pa != pb, name + ': something changed')
    if picture:
        shot(name)
    for _ in range(n):
        api('undo')
    back, pback = look(h), look(parent)
    check(back == before and pback == pb, name + ': undone exactly', '' if back == before and pback == pb else '\n  %s\n  %s\n  %s\n  %s' % (before, back, pb, pback))
    for _ in range(n):
        api('redo')
    again, pagain = look(h), look(parent)
    check(again == after and pagain == pa, name + ': redone', '' if again == after and pagain == pa else '\n  %s\n  %s' % (after, again))
    for _ in range(n):
        api('undo')
    back, pback = look(h), look(parent)
    check(back == before and pback == pb, name + ': undone again')

try:
    trial('1_tooltip', [('set', dict(h=h, name='tooltip', value='Teste do editor'))], False)
    trial('2_color', [('set', dict(h=h, name='color', value='#e8503a'))])
    trial('3_size', [('set', dict(h=h, name='width', value='90')), ('set', dict(h=h, name='preferredWidth', value='90'))])
    trial('4_hide', [('delete', dict(h=h))])
    trial('5_move', [('move', dict(h=h, index=first['siblings'] - 1))])
    trial('6_opacity', [('set', dict(h=h, name='opacity', value='0.3'))])
    trial('7_class', [('class', dict(h=h, name='btn-danger', on=True))])
    trial('8_add', [('add', dict(parent=parent, index=0, xml='<Button class="btn btn-primary" width="120" height="50"><Text text="Novo"/></Button>'))])
    trial('9_text', [('add', dict(parent=parent, index=0, xml='<Text text="Um texto" width="120" height="30"/>'))])
    r = api('add', parent=parent, xml='<Text text="x" width="50" height="30"/>')
    r2 = api('delete', h=r['h'])
    check('ok' in r2 and api('get', h=r['h']).get('error'), 'added element really removed')
    api('undo')
    check('error' not in api('get', h=r['h']), 'undoing the removal brings it back with the same number')
    api('undo')
    for name, v in (('color', 'banana'), ('width', 'abc'), ('anchor', 'Nowhere'), ('nothing', '1'), ('onClick', 'Quit'), ('script', 'X'), ('tooltip', '{x}')):
        before = look(h)
        r = api('set', h=h, name=name, value=v)
        check('error' in r and look(h) == before, 'refused: %s=%s' % (name, v), r.get('error', 'ACCEPTED')[:90])
        if 'error' not in r:
            api('undo')
    for x in ('<Canvas/>', '<Button onClick="Quit"/>', '<Instance templateId="none"/>', '<oops'):
        check('error' in api('add', parent=parent, xml=x), 'refused: ' + x)
finally:
    undo_all()
check(look(h) == first and look(parent) == pfirst, 'everything as it was')
shot('z_after')
t = []
for p, b in (('screens', {}), ('find', dict(text='zzz')), ('get', dict(h=h)), ('tree', dict(h=api('screens')[0]['h'], depth=50)), ('shot', dict(w=1280))):
    a = time.time(); api(p, raw=True, **b); t.append('%s %.0f ms' % (p, (time.time() - a) * 1000))
print(', '.join(t))
print('FAILED %d' % bad if bad else 'ALL GOOD')
