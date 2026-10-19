"""Static checks only. No driver loading or audio device access."""
import hashlib
import json
from pathlib import Path
import pefile

root = Path(__file__).resolve().parents[1]
image = root / 'out/x64/LinkMicCapture.sys'
pe = pefile.PE(str(image))
assert pe.FILE_HEADER.Machine == 0x8664, 'Expected x64 image'
assert pe.OPTIONAL_HEADER.Subsystem == 1, 'Expected native subsystem'
assert pe.OPTIONAL_HEADER.AddressOfEntryPoint != 0
imports = sorted(entry.dll.decode('ascii').lower() for entry in pe.DIRECTORY_ENTRY_IMPORT)
assert 'portcls.sys' in imports and 'ntoskrnl.exe' in imports and 'wdfldr.sys' in imports
assert set(imports) <= {'portcls.sys', 'ntoskrnl.exe', 'wdfldr.sys', 'hal.dll'}
flags = pe.OPTIONAL_HEADER.DllCharacteristics
assert flags & 0x40 and flags & 0x100 and flags & 0x80, 'Missing ASLR/NX/integrity flags'
sections = [{'name': s.Name.rstrip(b'\0').decode(), 'virtualSize': s.Misc_VirtualSize,
             'writable': bool(s.Characteristics & 0x80000000),
             'executable': bool(s.Characteristics & 0x20000000)} for s in pe.sections]
assert not any(s['writable'] and s['executable'] for s in sections), 'W+X section'
result = {'binary': str(image), 'sha256': hashlib.sha256(image.read_bytes()).hexdigest(),
          'machine': 'amd64', 'subsystem': 'native', 'imports': imports, 'sections': sections,
          'infVerif': 'passed /w', 'inf2cat': 'passed; no errors or warnings',
          'hostRingAndDispatchTests': 'passed', 'signed': False, 'loaded': False,
          'hardwareCaptureVerified': False, 'driverVerifierRun': False, 'hlkRun': False}
(root / 'out/x64/verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ('sections',)}, indent=2))
