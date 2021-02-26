#!/usr/bin/env python
from __future__ import print_function
import re
import io

with open('Duplicati.sln', 'r') as f:
    c = f.read()

p = re.compile(r"\{[0-9a-fA-F\-]*\}")

tags = p.findall(c)

d = {}

for n in tags:
    d[n] = 0

for n in tags:
    d[n] = d[n] + 1

for n in d:
    if d[n] > 7:
        print(n)
