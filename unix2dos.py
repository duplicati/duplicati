#!/usr/bin/env python
import sys

for fname in sys.argv[1:]:
    infile = open( fname, "rb" )
    instr = infile.read()
    infile.close()
    outstr = instr.replace( "\r\n", "\n" ).replace( "\r", "\n" ).replace( "\n", "\r\n" )

    if len(outstr) == len(instr):
        continue
    
    outfile = open( fname, "wb" )
    outfile.write( outstr )
    outfile.close()
