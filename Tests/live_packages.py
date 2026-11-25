# Packages, in the designer, with "Claude may edit" on. Uses only a package named "Teste_Claude".
#   python Tests/live_packages.py make    edits the Armor view button and saves; then reload (F6), reopen the designer or restart the game
#   python Tests/live_packages.py check   are the edits back? Then off, on and off again
import json, os, sys, time, urllib.request
S = json.load(open(os.path.join(os.environ['LOCALAPPDATA'], 'SP2UIEditor', 'session.json')))
NAME = 'Teste_Claude'

def api(path, **b):
    q = urllib.request.Request('http://127.0.0.1:%d/api/%s' % (S['port'], path), json.dumps(b).encode(),
                               {'X-SP2UI-Key': S['key'], 'X-SP2UI-Client': 'claude', 'Content-Type': 'application/json'})
    try:
        return json.loads(urllib.request.urlopen(q, timeout=12).read())
    except urllib.error.HTTPError as e:
        return json.loads(e.read())

def button():
    return [x for x in api('find', text='button-armor-view', visible=False) if x.get('id') == 'button-armor-view'][0]['h']

def look():
    d = api('get', h=button())
    v = {a['name']: a.get('value') for a in d['attributes']}
    return dict(tooltip=v.get('tooltip'), color=v.get('color'), identity=d['identity'], new=[x['text'] for x in api('find', text='Pacote teste')], siblings=d['siblings'])

st = api('state')
print(st['scene'], 'edit allowed:', st['claudeMayEdit'])
if sys.argv[1] == 'make':
    print('before', look())
    if any(p['name'] == NAME for p in api('packages')['packages']):
        print('use', api('package_use', name=NAME))
    else:
        print('new', api('package_new', name=NAME))
    h = button()
    parent = api('get', h=h)['ancestors'][-1]['h']
    print(api('set', h=h, name='tooltip', value='Dica salva'), api('set', h=h, name='color', value='#2f9e5b'))
    print(api('add', parent=parent, index=0, xml='<Button class="btn btn-primary" width="150" height="50"><Text text="Pacote teste"/></Button>'))
    print('save', api('save'))
    print('after', look())
else:
    print(json.dumps([dict(name=p['name'], on=p['on'], ops=[(o['text'], o['state'], o['why']) for o in p['ops']]) for p in api('packages')['packages']], indent=1, ensure_ascii=False))
    print('now', look())
    for on in (False, True, False):
        print('on' if on else 'off', api('package_enable', name=NAME, on=on))
        time.sleep(0.8)
        print('   ', look())
