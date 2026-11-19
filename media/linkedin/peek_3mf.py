import re
import zipfile

path = r"C:\Users\stjep\Desktop\Projekti\Impetus\designs\PERF-KERO-A\engine.3mf"
with zipfile.ZipFile(path) as zf:
    data = zf.read("3D/3dmodel.model")
print("bytes", len(data))
for m in re.finditer(br'<object id="(\d+)" type="model" name="([^"]+)"', data):
    print(m.group(1).decode(), m.group(2).decode(), "at", m.start())
