#!/usr/bin/env python2
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
