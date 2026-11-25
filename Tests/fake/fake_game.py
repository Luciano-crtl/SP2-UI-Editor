# A stand-in for the plugin's server, to try the page and the connector without the game:  python Tests/fake/fake_game.py
import json, os, re, sys, io
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
here = os.path.dirname(os.path.abspath(__file__))
KEY = 'test'
N = {}
def node(h, type, kids=(), **k):
    N[h] = dict(h=h, type=type, kids=list(kids), attrs=dict(k.pop('attrs', {})), classes=k.pop('classes', []), **k)
    return h
node(1, 'Empty', [2, 3], id='root', file='Xml/Flight/FlightUI')
node(2, 'VerticalLayout', [4, 5], id='main-menu')
node(3, 'Text', id='status', attrs={'text': 'Throttle 50%'})
node(4, 'Button', [6], id='btn-a', classes=['btn', 'btn-default'], attrs={'color': '#E8903A', 'tooltip': 'Tune it'})
node(5, 'Button', [7], id='btn-b', classes=['btn'])
node(6, 'Text', attrs={'text': 'Armor Fragments'})
node(7, 'Text', attrs={'text': 'Quit <b>"x"</b>'})
S = dict(sel=0, serial=0, version=0, picking=False, allow=False, log=[], undone=[], next=100)
def parent(h): return next((n for n in N.values() if h in n['kids']), None)
def text(n): return n['attrs'].get('text') or next((text(N[k]) for k in n['kids'] if N[k]['type'] == 'Text'), None)
def out(n, depth):
    o = dict(h=n['h'], type=n['type'], id=n.get('id'), text=text(n) if n['type'] in ('Text', 'Button') else None, file=n.get('file'), added=n['h'] >= 100, hidden=n['attrs'].get('visible') == 'false')
    if n['kids']:
        o['n'] = len(n['kids'])
        if depth: o['kids'] = [out(N[k], depth - 1) for k in n['kids']]
    return {k: v for k, v in o.items() if v not in (None, False)}
def state(): return dict(plugin='fake', game='0.0', scene='Flight', picking=S['picking'], selected=S['sel'], selectionSerial=S['serial'], version=S['version'], done=len(S['log']), undone=len(S['undone']), claudeMayEdit=S['allow'], package=S.get('pkg'), unsaved=S.get('uns', True), screen=dict(w=1920, h=1080))
def change(by, what, undo, redo):
    redo(); S['log'].append((by, what, undo, redo)); S['undone'].clear(); S['version'] += 1; return dict(ok=True, version=S['version'])
def answer(path, b, by):
    if path in ('set', 'class', 'add', 'move', 'delete', 'undo', 'redo') and by == 'claude' and not S['allow']: return dict(error='the user has not allowed Claude to edit')
    if path == 'state': return state()
    if path == 'screens': return [out(N[1], 0)]
    if path == 'tree': return out(N[b['h']], b.get('depth', 3))
    if path == 'find': return [dict(out(n, 0), path='x') for n in N.values() if b['text'].lower() in (str(text(n)) + str(n.get('id'))).lower()]
    if path == 'select': S['sel'] = b.get('h') or 0; S['serial'] += 1; return state()
    if path == 'pick':
        if 'x' in b: S['sel'] = 4; S['serial'] += 1
        else: S['picking'] = b.get('on', True)
        return state()
    if path == 'allow':
        if by == 'claude': return dict(error='only the user')
        S['allow'] = b['on']; return state()
    if path == 'places': return [dict(name='flightMenu', h=2)] + [dict(name='panel:' + p['title'], h=3) for p in S.setdefault('panels', [])]
    if path == 'panels': return S.setdefault('panels', [])
    if path == 'panel_new': S.setdefault('panels', []).append(dict(uid='p%d' % len(S['panels']), title=b['title'], open=True)); S['sel'] = 2; S['serial'] += 1; return dict(ok=True, uid='p0', h=2)
    if path == 'configs': return [dict(plugin='a.b', mod='Armor Fragments', section='Debug', key='DrawRays', kind='bool', value='true', text='Draws <b>rays</b>'), dict(plugin='a.b', mod='Armor Fragments', section='Energy', key='Resistance', kind='number', value='600', text='J')]
    if path == 'methods': return [dict(root='flight', of='FlightUIScript', method='Turn(float a)', args=['a'])]
    if path == 'styles': return dict(styles=[dict([('class', 'btn'), ('attributes', [dict(name='color', value=S.get('st', '#6c6c6c'), color=True), dict(name='height', value='48')])])], constants=[dict(name='ColorPrimary', value='#28599e', color=True)])
    if path == 'style': S['st'] = b['value']; return change(by, 'style', lambda: None, lambda: None)
    if path == 'export': return dict(ok=True, folder='C:/x', files=['a.cs'], left=['toggle (config)'])
    if path == 'mod_actions': return []
    if path == 'action':
        n = N[b['h']]; old = n.get('action'); new = dict(kind=b['kind'], args=b.get('args') or {})
        return change(by, 'action ' + b['kind'], lambda: n.update(action=old), lambda: n.update(action=new if b['kind'] != 'none' else None))
    if path == 'packages': return dict(folder='C:/x', current=S.get('pkg'), unsaved=True, conflicts=['B replaces A: color'], problems=['bad.sp2ui: broken'], packages=[dict(name=p, on=p not in S.get('off', []), ops=[dict(text='"Quit": color = <b>', state='applied'), dict(text='x', state='orphan', why='gone')]) for p in S.setdefault('pkgs', [])])
    if path == 'package_new': S['pkgs'].append(b['name']); S['pkg'] = b['name']; return dict(ok=True)
    if path == 'package_use': S['pkg'] = b['name']; return dict(ok=True)
    if path == 'package_enable': (S.setdefault('off', []).remove if b['on'] else S.setdefault('off', []).append)(b['name']); return dict(ok=True)
    if path in ('save', 'package_drop', 'package_order'): S['uns'] = False; return dict(ok=True)
    if path == 'changes': return [dict(n=i, by=l[0], text=l[1], exact=True) for i, l in enumerate(S['log'])]
    if path == 'palette': return dict(templates=[dict(id=x, attributes={}) for x in ('control-header', 'control-text', 'control-toggle', 'flyout')], classes=['btn'])
    if path in ('undo', 'redo'):
        a, z = (S['log'], S['undone']) if path == 'undo' else (S['undone'], S['log'])
        if not a: return dict(error='nothing to ' + path)
        l = a.pop(); l[2 if path == 'undo' else 3](); z.append(l); S['version'] += 1; return dict(ok=True, step=l[1])
    n = N.get(b.get('h') or b.get('parent'))
    if not n: return dict(error='no such element')
    if path == 'get':
        anc = []; p = parent(n['h'])
        while p: anc.insert(0, dict(h=p['h'], name=p.get('id') or p['type'])); p = parent(p['h'])
        kinds = [('text', 'string'), ('tooltip', 'string'), ('visible', 'bool'), ('color', 'color'), ('width', 'float'), ('anchor', 'enum'), ('script', 'string'), ('raycastTarget', 'bool'), ('hoverClass', 'string')]
        at = [dict(name=k, kind=t, **({'value': n['attrs'][k], 'own': True} if k in n['attrs'] else {}), **({'options': ['TopLeft', 'Stretch']} if t == 'enum' else {}), **({'locked': True} if k == 'script' else {})) for k, t in kinds]
        sib = parent(n['h'])['kids'] if parent(n['h']) else [n['h']]
        return dict(out(n, 0), **({'action': n['action']} if n.get('action') else {}), path='p', ancestors=anc, fullText=text(n), classes=n['classes'], index=sib.index(n['h']), siblings=len(sib), src='Xml/Flight/FlightUI#1.0', source='<%s id="x" />' % n['type'], attributes=at, rect=dict(x=.1, y=.1 * (n['h'] % 7), w=.2, h=.08))
    if path == 'set':
        k, v, old = b['name'], b['value'], n['attrs'].get(b['name'])
        return change(by, '%s = %s' % (k, v), lambda: n['attrs'].update({k: old}) if old is not None else n['attrs'].pop(k), lambda: n['attrs'].update({k: v}))
    if path == 'class': return change(by, 'class', lambda: n['classes'].remove(b['name']), lambda: n['classes'].append(b['name'])) if b.get('on', True) else change(by, 'class', lambda: n['classes'].append(b['name']), lambda: n['classes'].remove(b['name']))
    if path == 'move':
        q, p = parent(n['h']), N[b.get('parent') or parent(n['h'])['h']]; old = q['kids'].index(n['h'])
        def put(f, t, i): f['kids'].remove(n['h']); t['kids'].insert(i, n['h'])
        return change(by, 'moved', lambda: put(p, q, old), lambda: put(q, p, b['index']))
    if path == 'delete': return answer('set', dict(h=n['h'], name='visible', value='false'), by)
    if path == 'add':
        h = S['next']; S['next'] += 1; m = re.match(r'<(\w+)', b['xml']); t = re.search(r'(?:text|title|label)="([^"]*)"', b['xml'])
        node(h, m.group(1), attrs={'text': t.group(1)} if t else {}); i = b.get('index', -1); i = len(n['kids']) if i < 0 else i
        r = change(by, 'added ' + m.group(1), lambda: n['kids'].remove(h), lambda: n['kids'].insert(i, h)); S['sel'] = h; S['serial'] += 1; return dict(r, h=h)
    return dict(error='unknown')
class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def send(self, code, kind, data):
        self.send_response(code); self.send_header('Content-Type', kind); self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_GET(self): self.send(200, 'text/html; charset=utf-8', open(os.path.join(here, '..', '..', 'Plugin', 'page.html'), encoding='utf8').read().replace('/*HELP*/', open(os.path.join(here, '..', '..', 'Plugin', 'help.txt'), encoding='utf8').read()).encode())
    def do_POST(self):
        body = self.rfile.read(int(self.headers['Content-Length'] or 0))
        if self.headers.get('X-SP2UI-Key') != KEY: return self.send(401, 'application/json', b'{"error":"wrong key"}')
        path = self.path[5:]
        if path == 'shot':
            svg = '<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="#345"/></svg>'
            return self.send(200, 'image/svg+xml', svg.encode())
        r = answer(path, json.loads(body or b'{}'), 'claude' if self.headers.get('X-SP2UI-Client') == 'claude' else 'user')
        self.send(400 if isinstance(r, dict) and 'error' in r else 200, 'application/json', json.dumps(r).encode())
ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1]) if len(sys.argv) > 1 else 47899), H).serve_forever()
