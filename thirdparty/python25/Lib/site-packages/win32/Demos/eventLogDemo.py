import win32evtlog, traceback
import win32api, win32con
import win32security # To translate NT Sids to account names.

from win32evtlogutil import *

def ReadLog(computer, logType="Application", dumpEachRecord = 0):
    # read the entire log back.
    h=win32evtlog.OpenEventLog(computer, logType)
    numRecords = win32evtlog.GetNumberOfEventLogRecords(h)
#       print "There are %d records" % numRecords

    num=0
    while 1:
        objects = win32evtlog.ReadEventLog(h, win32evtlog.EVENTLOG_BACKWARDS_READ|win32evtlog.EVENTLOG_SEQUENTIAL_READ, 0)
        if not objects:
            break
        for object in objects:
            # get it for testing purposes, but dont print it.
            msg = SafeFormatMessage(object, logType).encode("mbcs")
            if object.Sid is not None:
                try:
                    domain, user, typ = win32security.LookupAccountSid(computer, object.Sid)
                    sidDesc = "%s/%s" % (domain, user)
                except win32security.error:
                    sidDesc = str(object.Sid)
                user_desc = "Event associated with user %s" % (sidDesc,)
            else:
                user_desc = None
            if dumpEachRecord:
                if user_desc:
                    print user_desc
                print msg
        num = num + len(objects)

    if numRecords == num:
        print "Successfully read all", numRecords, "records"
    else:
        print "Couldn't get all records - reported %d, but found %d" % (numRecords, num)
        print "(Note that some other app may have written records while we were running!)"
    win32evtlog.CloseEventLog(h)

def Usage():
    print "Writes an event to the event log."
    print "-w : Dont write any test records."
    print "-r : Dont read the event log"
    print "-c : computerName : Process the log on the specified computer"
    print "-v : Verbose"
    print "-t : LogType - Use the specified log - default = 'Application'"


def test():
    # check if running on Windows NT, if not, display notice and terminate
    if win32api.GetVersion() & 0x80000000:
        print "This sample only runs on NT"
        return

    import sys, getopt
    opts, args = getopt.getopt(sys.argv[1:], "rwh?c:t:v")
    computer = None
    do_read = do_write = 1

    logType = "Application"
    verbose = 0

    if len(args)>0:
        print "Invalid args"
        usage()
        return 1
    for opt, val in opts:
        if opt == '-t':
            logType = val
        if opt == '-c':
            computer = val
        if opt in ['-h', '-?']:
            Usage()
            return
        if opt=='-r':
            do_read = 0
        if opt=='-w':
            do_write = 0
        if opt=='-v':
            verbose = verbose + 1
    if do_write:
        ReportEvent(logType, 2, strings=["The message text for event 2"], data = "Raw\0Data")
        ReportEvent(logType, 1, eventType=win32evtlog.EVENTLOG_WARNING_TYPE, strings=["A warning"], data = "Raw\0Data")
        ReportEvent(logType, 1, eventType=win32evtlog.EVENTLOG_INFORMATION_TYPE, strings=["An info"], data = "Raw\0Data")
        print "Successfully wrote 3 records to the log"

    if do_read:
        ReadLog(computer, logType, verbose > 0)

if __name__=='__main__':
    test()
