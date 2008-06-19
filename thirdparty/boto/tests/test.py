#!/usr/bin/env python
# Copyright (c) 2006,2007 Mitch Garnaat http://garnaat.org/
#
# Permission is hereby granted, free of charge, to any person obtaining a
# copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including
# without limitation the rights to use, copy, modify, merge, publish, dis-
# tribute, sublicense, and/or sell copies of the Software, and to permit
# persons to whom the Software is furnished to do so, subject to the fol-
# lowing conditions:
#
# The above copyright notice and this permission notice shall be included
# in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
# OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABIL-
# ITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
# SHALL THE AUTHOR BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
# IN THE SOFTWARE.

"""
do the unit tests!
"""

import sys, os, unittest
import getopt, sys
import boto

from boto.tests.test_sqsconnection import SQSConnectionTest
from boto.tests.test_s3connection import S3ConnectionTest
from boto.tests.test_ec2connection import EC2ConnectionTest
from boto.tests.test_sdbconnection import SDBConnectionTest

def usage():
    print 'test.py  [-t testsuite] [-v verbosity]'
    print '    -t   run specific testsuite (s3|sqs|ec2|sdb|all)'
    print '    -v   verbosity (0|1|2)'
  
def main():
    try:
        opts, args = getopt.getopt(sys.argv[1:], 'ht:v:',
                                   ['help', 'testsuite', 'verbosity'])
    except:
        usage()
        sys.exit(2)
    testsuite = 'all'
    verbosity = 1
    for o, a in opts:
        if o in ('-h', '--help'):
            usage()
            sys.exit()
        if o in ('-t', '--testsuite'):
            testsuite = a
        if o in ('-v', '--verbosity'):
            verbosity = int(a)
    if len(args) != 0:
        usage()
        sys.exit()
    suite = unittest.TestSuite()
    if testsuite == 'all':
        suite.addTest(unittest.makeSuite(SQSConnectionTest))
        suite.addTest(unittest.makeSuite(S3ConnectionTest))
        suite.addTest(unittest.makeSuite(EC2ConnectionTest))
        suite.addTest(unittest.makeSuite(SDBConnectionTest))
    elif testsuite == 's3':
        suite.addTest(unittest.makeSuite(S3ConnectionTest))
    elif testsuite == 'sqs':
        suite.addTest(unittest.makeSuite(SQSConnectionTest))
    elif testsuite == 'ec2':
        suite.addTest(unittest.makeSuite(EC2ConnectionTest))
    elif testsuite == 'sdb':
        suite.addTest(unittest.makeSuite(SDBConnectionTest))
    else:
        usage()
        sys.exit()
    unittest.TextTestRunner(verbosity=verbosity).run(suite)

if __name__ == "__main__":
    main()
