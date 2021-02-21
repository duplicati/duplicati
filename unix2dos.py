#!/usr/bin/env python
import sys

for fname in sys.argv[1:]:
    with open(fname, 'rb') as infile:
        instr = infile.read()

    outstr = instr.replace( b"\r\n", b"\n" ).replace( b"\r", b"\n" ).replace( b"\n", b"\r\n" )

    if len(outstr) == len(instr):
        continue
    
    with open( fname, "wb" ) as outfile:
        outfile.write( outstr )
