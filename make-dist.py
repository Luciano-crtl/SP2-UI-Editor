"""Builds what is given to other people: dist/SP2-UI-Editor-<version>.zip (plugin, editor and connector as single files, guides).
    python make-dist.py [game folder]
Stops if a test fails, or if a file would carry this computer's user name."""
import getpass, hashlib, os, re, shutil, subprocess, sys, tempfile, zipfile
R = os.path.dirname(os.path.abspath(__file__))
GAME = sys.argv[1] if len(sys.argv) > 1 else r'C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2'
os.chdir(R)
version = re.search(r'Version = "([\d.]+)"', open('Plugin/Plugin.cs', encoding='utf8').read()).group(1)
# in a folder of its own: the editor may be open from dist, and nothing there is touched before all is done
D = tempfile.mkdtemp(prefix='sp2ui-')
B = os.path.join(D, 'SP2-UI-Editor')
for p in ('plugin', 'editor', 'mcp'):
    os.makedirs(os.path.join(B, p))

def run(*a):
    print('>', ' '.join(a)[:150])
    if subprocess.run(a).returncode:
        sys.exit('FAILED')

# no path of this computer inside the files
clean = ['-p:PathMap=%s=/_' % R, '-p:ContinuousIntegrationBuild=true', '-p:Deterministic=true', '-nologo', '-v', 'q']
run('dotnet', 'run', '-c', 'Release', '--project', 'Tests', '--', os.path.join(GAME, 'SimplePlanes 2_Data', 'resources.assets'))
run('dotnet', 'build', 'Plugin', '-c', 'Release', '--no-incremental', '-p:GameDir=' + GAME, '-o', os.path.join(D, 'p'), *clean)
shutil.copy(os.path.join(D, 'p', 'SP2UIEditor.dll'), os.path.join(B, 'plugin'))
single = ['-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
          '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none']
for project, folder, exe in (('Desktop', 'editor', 'SP2UIEditor.exe'), ('Mcp', 'mcp', 'SP2UI.Mcp.exe')):
    run('dotnet', 'publish', project, *single, '-o', os.path.join(D, folder), *clean)
    shutil.copy(os.path.join(D, folder, exe), os.path.join(B, folder))
for g in ('GUIA.md', 'GUIDE.md', 'LICENSE'):
    shutil.copy(g, B)

user = getpass.getuser().lower().encode()
for folder, _, names in os.walk(B):
    for n in names:
        data = open(os.path.join(folder, n), 'rb').read().lower()
        for needle in (b'\\users\\' + user, b'/users/' + user, ('\\users\\' + user.decode()).encode('utf-16le')):
            if needle in data:
                sys.exit('%s carries the user name' % n)
os.makedirs(os.path.join(R, 'dist'), exist_ok=True)
zpath = os.path.join(R, 'dist', 'SP2-UI-Editor-%s.zip' % version)
with zipfile.ZipFile(zpath, 'w', zipfile.ZIP_DEFLATED) as z:
    for folder, _, names in os.walk(B):
        for n in names:
            f = os.path.join(folder, n)
            z.write(f, os.path.relpath(f, D))
            print('%9d  %s' % (os.path.getsize(f), os.path.relpath(f, B)))
for folder, _, names in os.walk(B):
    for n in names:
        f = os.path.join(folder, n)
        to = os.path.join(R, 'dist', os.path.relpath(f, D))
        os.makedirs(os.path.dirname(to), exist_ok=True)
        try:
            shutil.copy(f, to)
        except OSError:
            print('in use, left as it was:', to)
print(zpath, os.path.getsize(zpath) // 1024, 'KB, plugin', hashlib.sha256(open(os.path.join(B, 'plugin', 'SP2UIEditor.dll'), 'rb').read()).hexdigest()[:12])
shutil.rmtree(D, ignore_errors=True)
