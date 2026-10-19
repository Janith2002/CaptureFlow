"""Private candidate build using native dotnet/ISCC commands; never launches the app.

No policy settings are changed. Script execution must itself be permitted by the
host. Public release acceptance is deliberately separate from package creation.
"""
from pathlib import Path
import argparse, hashlib, json, re, shutil, subprocess, uuid, zipfile

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--compiler', type=Path, required=True)
    parser.add_argument('--version', default='1.0.0')
    parser.add_argument('--numeric-version', default='1.0.0.0')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?', args.version):
        parser.error('Invalid version')
    if not re.fullmatch(r'\d+\.\d+\.\d+\.\d+', args.numeric_version):
        parser.error('Invalid numeric version')
    if not args.compiler.is_file():
        parser.error('Supply an existing verified Inno Setup compiler')
    root = Path(__file__).resolve().parent.parent
    candidate = root/'artifacts/installer/candidates'/uuid.uuid4().hex
    candidate.mkdir(parents=True, exist_ok=False)
    publish, output = candidate/'publish', candidate/'output'
    output.mkdir()
    command = ['dotnet', 'publish', str(root/'src/CaptureFlow.Desktop/CaptureFlow.Desktop.csproj'),
               '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
               '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
               '-p:DebugType=None', '-p:DebugSymbols=false',
               '-p:Version='+args.version, '-p:FileVersion='+args.numeric_version,
               '-p:AssemblyVersion='+args.numeric_version,
               '-p:CaptureFlowPublisher=Janith Akalanka', '-o', str(publish)]
    with (candidate/'publish.log').open('w', encoding='utf-8') as log:
        subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, check=True)
    for name in ['coreclr.dll', 'PresentationNative_cor3.dll', 'e_sqlite3.dll',
                 'WinRT.Runtime.dll', 'Microsoft.Windows.SDK.NET.dll', 'CaptureFlow.Desktop.exe',
                 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Licenses/DISTRIBUTION-PROVENANCE.json']:
        if not (publish/name).is_file():
            raise RuntimeError('Missing dependency: '+name)
    forbidden = {'.mp4', '.mp3', '.wav', '.cfedit', '.db', '.pdb', '.pfx', '.key'}
    for path in publish.rglob('*'):
        if path.is_file() and (path.suffix.lower() in forbidden or
                              path.name.lower() in {'ffmpeg.exe', 'ffprobe.exe'} or '.db-' in path.name):
            raise RuntimeError('Forbidden payload file: '+path.name)
    # Standard product apphost name; its managed assembly name and WPF pack URIs
    # are preserved. This apphost is never executed to work around a policy block.
    (publish/'CaptureFlow.Desktop.exe').rename(publish/'CaptureFlow.exe')
    for name in ['PORTABLE-README.txt', 'PREVIEW-NOTICE.txt']:
        shutil.copyfile(root/'installer'/name, publish/name)
    provenance = json.loads((publish/'Licenses/DISTRIBUTION-PROVENANCE.json').read_text())
    for entry in provenance:
        if hashlib.sha256((publish/'Licenses'/entry['file']).read_bytes()).hexdigest() != entry['sha256']:
            raise RuntimeError('License provenance mismatch: '+entry['file'])
    portable = output/f'CaptureFlow-Portable-{args.version}-win-x64.zip'
    with zipfile.ZipFile(portable, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(publish.rglob('*')):
            if path.is_file(): archive.write(path, path.relative_to(publish).as_posix())
    with (candidate/'installer.log').open('w', encoding='utf-8') as log:
        subprocess.run([str(args.compiler.resolve()), '/DPublishDir='+str(publish),
                        '/DOutputDir='+str(output), '/DPackageVersion='+args.version,
                        '/DNumericVersion='+args.numeric_version, str(root/'installer/CaptureFlow.iss')],
                       cwd=root, stdout=log, stderr=subprocess.STDOUT, check=True)
    installer = output/f'CaptureFlow-Setup-{args.version}-win-x64.exe'
    lines = [hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name for p in [installer, portable]]
    (output/'SHA256SUMS.txt').write_text('\n'.join(lines)+'\n', encoding='utf-8')
    result = {'candidate': str(candidate), 'installer': str(installer), 'portable': str(portable),
              'publicReleaseApproved': False}
    (candidate/'candidate.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))

if __name__ == '__main__':
    main()
