import re
import io

f = open('Duplicati.sln')
c = f.read()
f.close()

p = re.compile(r"\{[0-9a-fA-F\-]*\}")

tags = p.findall(c)

d = {}

for n in tags:
    d[n] = 0

for n in tags:
    d[n] = d[n] + 1

for n in d:
    if d[n] > 7:
        print n
