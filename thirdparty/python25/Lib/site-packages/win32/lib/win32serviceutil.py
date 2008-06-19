# General purpose service utilities, both for standard Python scripts,
# and for for Python programs which run as services...
#
# Note that most utility functions here will raise win32api.error's
# (which is == win32service.error, pywintypes.error, etc)
# when things go wrong - eg, not enough permissions to hit the
# registry etc.

import win32service, win32api, win32con, winerror
import sys, string, pywintypes, os

error = "Python Service Utility Error"

def LocatePythonServiceExe(exeName = None):
    if not exeName and hasattr(sys, "frozen"):
        # If py2exe etc calls this with no exeName, default is current exe.
        return sys.executable

    # Try and find the specified EXE somewhere.  If specifically registered,
    # use it.  Otherwise look down sys.path, and the global PATH environment.
    if exeName is None:
        if os.path.splitext(win32service.__file__)[0].endswith("_d"):
            exeName = "PythonService_d.exe"
        else:
            exeName = "PythonService.exe"
    # See if it exists as specified
    if os.path.isfile(exeName): return win32api.GetFullPathName(exeName)
    baseName = os.path.splitext(os.path.basename(exeName))[0]
    try:
        exeName = win32api.RegQueryValue(win32con.HKEY_LOCAL_MACHINE,
                                         "Software\\Python\\%s\\%s" % (baseName, sys.winver))
        if os.path.isfile(exeName):
            return exeName
        raise RuntimeError, "The executable '%s' is registered as the Python " \
                            "service exe, but it does not exist as specified" \
                            % exeName
    except win32api.error:
        # OK - not there - lets go a-searchin'
        for path in [sys.prefix] + sys.path:
            look = os.path.join(path, exeName)
            if os.path.isfile(look):
                return win32api.GetFullPathName(look)
        # Try the global Path.
        try:
            return win32api.SearchPath(None, exeName)[0]
        except win32api.error:
            msg = "%s is not correctly registered\nPlease locate and run %s, and it will self-register\nThen run this service registration process again." % (exeName, exeName)
            raise error, msg

def _GetServiceShortName(longName):
    # looks up a services name
    # from the display name
    # Thanks to Andy McKay for this code.
    access = win32con.KEY_READ | win32con.KEY_ENUMERATE_SUB_KEYS | win32con.KEY_QUERY_VALUE
    hkey = win32api.RegOpenKey(win32con.HKEY_LOCAL_MACHINE, "SYSTEM\\CurrentControlSet\\Services", 0, access)
    num = win32api.RegQueryInfoKey(hkey)[0]
    longName = longName.lower()
    # loop through number of subkeys
    for x in range(0, num):
    # find service name, open subkey
        svc = win32api.RegEnumKey(hkey, x)
        skey = win32api.RegOpenKey(hkey, svc, 0, access)
        try:
            # find display name
            thisName = str(win32api.RegQueryValueEx(skey, "DisplayName")[0])
            if thisName.lower() == longName:
                return svc
        except win32api.error:
            # in case there is no key called DisplayName
            pass
    return None

# Open a service given either it's long or short name.
def SmartOpenService(hscm, name, access):
    try:
        return win32service.OpenService(hscm, name, access)
    except win32api.error, details:
        if details[0] not in [winerror.ERROR_SERVICE_DOES_NOT_EXIST,
                              winerror.ERROR_INVALID_NAME]:
            raise
        name = _GetServiceShortName(name)
        if name is None:
            raise
        return win32service.OpenService(hscm, name, access)

def LocateSpecificServiceExe(serviceName):
    # Given the name of a specific service, return the .EXE name _it_ uses
    # (which may or may not be the Python Service EXE
    hkey = win32api.RegOpenKey(win32con.HKEY_LOCAL_MACHINE, "SYSTEM\\CurrentControlSet\\Services\\%s" % (serviceName), 0, win32con.KEY_ALL_ACCESS)
    try:
        return win32api.RegQueryValueEx(hkey, "ImagePath")[0]
    finally:
        hkey.Close()

def InstallPerfmonForService(serviceName, iniName, dllName = None):
    # If no DLL name, look it up in the INI file name
    if not dllName: # May be empty string!
        dllName = win32api.GetProfileVal("Python", "dll", "", iniName)
    # Still not found - look for the standard one in the same dir as win32service.pyd
    if not dllName:
        try:
            tryName = os.path.join(os.path.split(win32service.__file__)[0], "perfmondata.dll")
            if os.path.isfile(tryName):
                dllName = tryName
        except AttributeError:
            # Frozen app? - anyway, can't find it!
            pass
    if not dllName:
        raise ValueError, "The name of the performance DLL must be available"
    dllName = win32api.GetFullPathName(dllName)
    # Now setup all the required "Performance" entries.
    hkey = win32api.RegOpenKey(win32con.HKEY_LOCAL_MACHINE, "SYSTEM\\CurrentControlSet\\Services\\%s" % (serviceName), 0, win32con.KEY_ALL_ACCESS)
    try:
        subKey = win32api.RegCreateKey(hkey, "Performance")
        try:
            win32api.RegSetValueEx(subKey, "Library", 0, win32con.REG_SZ, dllName)
            win32api.RegSetValueEx(subKey, "Open", 0, win32con.REG_SZ, "OpenPerformanceData")
            win32api.RegSetValueEx(subKey, "Close", 0, win32con.REG_SZ, "ClosePerformanceData")
            win32api.RegSetValueEx(subKey, "Collect", 0, win32con.REG_SZ, "CollectPerformanceData")
        finally:
            win32api.RegCloseKey(subKey)
    finally:
        win32api.RegCloseKey(hkey)
    # Now do the "Lodctr" thang...

    try:
        import perfmon
        path, fname = os.path.split(iniName)
        oldPath = os.getcwd()
        if path:
            os.chdir(path)
        try:
            perfmon.LoadPerfCounterTextStrings("python.exe " + fname)
        finally:
            os.chdir(oldPath)
    except win32api.error, details:
        print "The service was installed OK, but the performance monitor"
        print "data could not be loaded.", details

def _GetCommandLine(exeName, exeArgs):
    if exeArgs is not None:
        return exeName + " " + exeArgs
    else:
        return exeName

def InstallService(pythonClassString, serviceName, displayName, startType = None, errorControl = None, bRunInteractive = 0, serviceDeps = None, userName = None, password = None, exeName = None, perfMonIni = None, perfMonDll = None, exeArgs = None, description = None):
    # Handle the default arguments.
    if startType is None:
        startType = win32service.SERVICE_DEMAND_START
    serviceType = win32service.SERVICE_WIN32_OWN_PROCESS
    if bRunInteractive:
        serviceType = serviceType | win32service.SERVICE_INTERACTIVE_PROCESS
    if errorControl is None:
        errorControl = win32service.SERVICE_ERROR_NORMAL

    exeName = '"%s"' % LocatePythonServiceExe(exeName) # None here means use default PythonService.exe
    commandLine = _GetCommandLine(exeName, exeArgs)
    hscm = win32service.OpenSCManager(None,None,win32service.SC_MANAGER_ALL_ACCESS)
    try:
        hs = win32service.CreateService(hscm,
                                serviceName,
                                displayName,
                                win32service.SERVICE_ALL_ACCESS,         # desired access
                    serviceType,        # service type
                    startType,
                    errorControl,       # error control type
                    commandLine,
                    None,
                    0,
                    serviceDeps,
                    userName,
                    password)
        if description is not None:
            try:
                win32service.ChangeServiceConfig2(hs,win32service.SERVICE_CONFIG_DESCRIPTION,description)
            except NotImplementedError:
                pass    ## ChangeServiceConfig2 and description do not exist on NT
        win32service.CloseServiceHandle(hs)
    finally:
        win32service.CloseServiceHandle(hscm)
    InstallPythonClassString(pythonClassString, serviceName)
    # If I have performance monitor info to install, do that.
    if perfMonIni is not None:
        InstallPerfmonForService(serviceName, perfMonIni, perfMonDll)

def ChangeServiceConfig(pythonClassString, serviceName, startType = None, errorControl = None, bRunInteractive = 0, serviceDeps = None, userName = None, password = None, exeName = None, displayName = None, perfMonIni = None, perfMonDll = None, exeArgs = None, description = None):
    # Before doing anything, remove any perfmon counters.
    try:
        import perfmon
        perfmon.UnloadPerfCounterTextStrings("python.exe "+serviceName)
    except (ImportError, win32api.error):
        pass

    # The EXE location may have changed
    exeName = '"%s"' % LocatePythonServiceExe(exeName)

    # Handle the default arguments.
    if startType is None: startType = win32service.SERVICE_NO_CHANGE
    if errorControl is None: errorControl = win32service.SERVICE_NO_CHANGE

    hscm = win32service.OpenSCManager(None,None,win32service.SC_MANAGER_ALL_ACCESS)
    serviceType = win32service.SERVICE_WIN32_OWN_PROCESS
    if bRunInteractive:
        serviceType = serviceType | win32service.SERVICE_INTERACTIVE_PROCESS
    commandLine = _GetCommandLine(exeName, exeArgs)
    try:
        hs = SmartOpenService(hscm, serviceName, win32service.SERVICE_ALL_ACCESS)
        try:

            win32service.ChangeServiceConfig(hs,
                serviceType,  # service type
                startType,
                errorControl,       # error control type
                commandLine,
                None,
                0,
                serviceDeps,
                userName,
                password,
                displayName)
            if description is not None:
                try:
                    win32service.ChangeServiceConfig2(hs,win32service.SERVICE_CONFIG_DESCRIPTION,description)
                except NotImplementedError:
                    pass    ## ChangeServiceConfig2 and description do not exist on NT

        finally:
            win32service.CloseServiceHandle(hs)
    finally:
        win32service.CloseServiceHandle(hscm)
    InstallPythonClassString(pythonClassString, serviceName)
    # If I have performance monitor info to install, do that.
    if perfMonIni is not None:
        InstallPerfmonForService(serviceName, perfMonIni, perfMonDll)

def InstallPythonClassString(pythonClassString, serviceName):
    # Now setup our Python specific entries.
    if pythonClassString:
        key = win32api.RegCreateKey(win32con.HKEY_LOCAL_MACHINE, "System\\CurrentControlSet\\Services\\%s\\PythonClass" % serviceName)
        try:
            win32api.RegSetValue(key, None, win32con.REG_SZ, pythonClassString);
        finally:
            win32api.RegCloseKey(key)

# Utility functions for Services, to allow persistant properties.
def SetServiceCustomOption(serviceName, option, value):
    try:
        serviceName = serviceName._svc_name_
    except AttributeError:
        pass
    key = win32api.RegCreateKey(win32con.HKEY_LOCAL_MACHINE, "System\\CurrentControlSet\\Services\\%s\\Parameters" % serviceName)
    try:
        if type(value)==type(0):
            win32api.RegSetValueEx(key, option, 0, win32con.REG_DWORD, value);
        else:
            win32api.RegSetValueEx(key, option, 0, win32con.REG_SZ, value);
    finally:
        win32api.RegCloseKey(key)

def GetServiceCustomOption(serviceName, option, defaultValue = None):
    # First param may also be a service class/instance.
    # This allows services to pass "self"
    try:
        serviceName = serviceName._svc_name_
    except AttributeError:
        pass
    key = win32api.RegCreateKey(win32con.HKEY_LOCAL_MACHINE, "System\\CurrentControlSet\\Services\\%s\\Parameters" % serviceName)
    try:
        try:
            return win32api.RegQueryValueEx(key, option)[0]
        except win32api.error:  # No value.
            return defaultValue
    finally:
        win32api.RegCloseKey(key)


def RemoveService(serviceName):
    try:
        import perfmon
        perfmon.UnloadPerfCounterTextStrings("python.exe "+serviceName)
    except (ImportError, win32api.error):
        pass

    hscm = win32service.OpenSCManager(None,None,win32service.SC_MANAGER_ALL_ACCESS)
    try:
        hs = SmartOpenService(hscm, serviceName, win32service.SERVICE_ALL_ACCESS)
        win32service.DeleteService(hs)
        win32service.CloseServiceHandle(hs)
    finally:
        win32service.CloseServiceHandle(hscm)

    import win32evtlogutil
    try:
        win32evtlogutil.RemoveSourceFromRegistry(serviceName)
    except win32api.error:
        pass

def ControlService(serviceName, code, machine = None):
    hscm = win32service.OpenSCManager(machine,None,win32service.SC_MANAGER_ALL_ACCESS)
    try:

        hs = SmartOpenService(hscm, serviceName, win32service.SERVICE_ALL_ACCESS)
        try:
            status = win32service.ControlService(hs, code)
        finally:
            win32service.CloseServiceHandle(hs)
    finally:
        win32service.CloseServiceHandle(hscm)
    return status

def __FindSvcDeps(findName):
    if type(findName) is pywintypes.UnicodeType: findName = str(findName)
    dict = {}
    k = win32api.RegOpenKey(win32con.HKEY_LOCAL_MACHINE, "SYSTEM\\CurrentControlSet\\Services")
    num = 0
    while 1:
        try:
            svc = win32api.RegEnumKey(k, num)
        except win32api.error:
            break
        num = num + 1
        sk = win32api.RegOpenKey(k, svc)
        try:
            deps, typ = win32api.RegQueryValueEx(sk, "DependOnService")
        except win32api.error:
            deps = ()
        for dep in deps:
            dep = string.lower(dep)
            dep_on = dict.get(dep, [])
            dep_on.append(svc)
            dict[dep]=dep_on

    return __ResolveDeps(findName, dict)


def __ResolveDeps(findName, dict):
    items = dict.get(string.lower(findName), [])
    retList = []
    for svc in items:
        retList.insert(0, svc)
        retList = __ResolveDeps(svc, dict) + retList
    return retList

def WaitForServiceStatus(serviceName, status, waitSecs, machine=None):
    """Waits for the service to return the specified status.  You
    should have already requested the service to enter that state"""
    for i in range(waitSecs*4):
        now_status = QueryServiceStatus(serviceName, machine)[1]
        if now_status == status:
            break
        win32api.Sleep(250)
    else:
        raise pywintypes.error, (winerror.ERROR_SERVICE_REQUEST_TIMEOUT, "QueryServiceStatus", win32api.FormatMessage(winerror.ERROR_SERVICE_REQUEST_TIMEOUT)[:-2])
    
def __StopServiceWithTimeout(hs, waitSecs = 30):
    try:
        status = win32service.ControlService(hs, win32service.SERVICE_CONTROL_STOP)
    except pywintypes.error, (hr, name, msg):
        if hr!=winerror.ERROR_SERVICE_NOT_ACTIVE:
            raise win32service.error, (hr, name, msg)
    for i in range(waitSecs):
        status = win32service.QueryServiceStatus(hs)
        if status[1] == win32service.SERVICE_STOPPED:
            break
        win32api.Sleep(1000)
    else:
        raise pywintypes.error, (winerror.ERROR_SERVICE_REQUEST_TIMEOUT, "ControlService", win32api.FormatMessage(winerror.ERROR_SERVICE_REQUEST_TIMEOUT)[:-2])


def StopServiceWithDeps(serviceName, machine = None, waitSecs = 30):
    # Stop a service recursively looking for dependant services
    hscm = win32service.OpenSCManager(machine,None,win32service.SC_MANAGER_ALL_ACCESS)
    try:
        deps = __FindSvcDeps(serviceName)
        for dep in deps:
            hs = win32service.OpenService(hscm, dep, win32service.SERVICE_ALL_ACCESS)
            try:
                __StopServiceWithTimeout(hs, waitSecs)
            finally:
                win32service.CloseServiceHandle(hs)
        # Now my service!
        hs = win32service.OpenService(hscm, serviceName, win32service.SERVICE_ALL_ACCESS)
        try:
            __StopServiceWithTimeout(hs, waitSecs)
        finally:
            win32service.CloseServiceHandle(hs)

    finally:
        win32service.CloseServiceHandle(hscm)


def StopService(serviceName, machine = None):
    return ControlService(serviceName, win32service.SERVICE_CONTROL_STOP, machine)

def StartService(serviceName, args = None, machine = None):
    hscm = win32service.OpenSCManager(machine,None,win32service.SC_MANAGER_ALL_ACCESS)
    try:

        hs = SmartOpenService(hscm, serviceName, win32service.SERVICE_ALL_ACCESS)
        try:
            win32service.StartService(hs, args)
        finally:
            win32service.CloseServiceHandle(hs)
    finally:
        win32service.CloseServiceHandle(hscm)

def RestartService(serviceName, args = None, waitSeconds = 30, machine = None):
    "Stop the service, and then start it again (with some tolerance for allowing it to stop.)"
    try:
        StopService(serviceName, machine)
    except pywintypes.error, (hr, name, msg):
        # Allow only "service not running" error
        if hr!=winerror.ERROR_SERVICE_NOT_ACTIVE:
            raise win32service.error, (hr, name, msg)
    # Give it a few goes, as the service may take time to stop
    for i in range(waitSeconds):
        try:
            StartService(serviceName, args, machine)
            break
        except pywintypes.error, (hr, name, msg):
            if hr!=winerror.ERROR_SERVICE_ALREADY_RUNNING:
                raise
            win32api.Sleep(1000)
    else:
        print "Gave up waiting for the old service to stop!"

def _DebugCtrlHandler(evt):
    if evt in (win32con.CTRL_C_EVENT, win32con.CTRL_BREAK_EVENT):
        assert g_debugService
        print "Stopping debug service."
        g_debugService.SvcStop()
        return True
    return False

def DebugService(cls, argv = []):
    # Run a service in "debug" mode.  Re-implements what pythonservice.exe
    # does when it sees a "-debug" param.
    # Currently only used by "frozen" (ie, py2exe) programs (but later may
    # end up being used for all services should we ever remove
    # pythonservice.exe)
    import servicemanager
    global g_debugService

    print "Debugging service %s - press Ctrl+C to stop." % (cls._svc_name_,)
    servicemanager.Debugging(True)
    servicemanager.PrepareToHostSingle(cls)
    g_debugService = cls(argv)
    # Setup a ctrl+c handler to simulate a "stop"
    win32api.SetConsoleCtrlHandler(_DebugCtrlHandler, True)
    try:
        g_debugService.SvcRun()
    finally:
        win32api.SetConsoleCtrlHandler(_DebugCtrlHandler, False)
        servicemanager.Debugging(False)
        g_debugService = None

def GetServiceClassString(cls, argv = None):
    if argv is None:
        argv = sys.argv
    import pickle, os
    modName = pickle.whichmodule(cls, cls.__name__)
    if modName == '__main__':
        try:
            fname = win32api.GetFullPathName(argv[0])
            path = os.path.split(fname)[0]
            # Eaaaahhhh - sometimes this will be a short filename, which causes
            # problems with 1.5.1 and the silly filename case rule.
            # Get the long name
            fname = os.path.join(path, win32api.FindFiles(fname)[0][8])
        except win32api.error:
            raise error, "Could not resolve the path name '%s' to a full path" % (argv[0])
        modName = os.path.splitext(fname)[0]
    return modName + "." + cls.__name__

def QueryServiceStatus(serviceName, machine=None):
    hscm = win32service.OpenSCManager(machine,None,win32service.SC_MANAGER_CONNECT)
    try:

        hs = SmartOpenService(hscm, serviceName, win32service.SERVICE_QUERY_STATUS)
        try:
            status = win32service.QueryServiceStatus(hs)
        finally:
            win32service.CloseServiceHandle(hs)
    finally:
        win32service.CloseServiceHandle(hscm)
    return status

def usage():
    try:
        fname = os.path.split(sys.argv[0])[1]
    except:
        fname = sys.argv[0]
    print "Usage: '%s [options] install|update|remove|start [...]|stop|restart [...]|debug [...]'" % fname
    print "Options for 'install' and 'update' commands only:"
    print " --username domain\username : The Username the service is to run under"
    print " --password password : The password for the username"
    print " --startup [manual|auto|disabled] : How the service starts, default = manual"
    print " --interactive : Allow the service to interact with the desktop."
    print " --perfmonini file: .ini file to use for registering performance monitor data"
    print " --perfmondll file: .dll file to use when querying the service for"
    print "   performance data, default = perfmondata.dll"
    print "Options for 'start' and 'stop' commands only:"
    print " --wait seconds: Wait for the service to actually start or stop."
    print "                 If you specify --wait with the 'stop' option, the service"
    print "                 and all dependent services will be stopped, each waiting"
    print "                 the specified period."
    sys.exit(1)

def HandleCommandLine(cls, serviceClassString = None, argv = None, customInstallOptions = "", customOptionHandler = None):
    """Utility function allowing services to process the command line.

    Allows standard commands such as 'start', 'stop', 'debug', 'install' etc.

    Install supports 'standard' command line options prefixed with '--', such as
    --username, --password, etc.  In addition,
    the function allows custom command line options to be handled by the calling function.
    """
    err = 0

    if argv is None: argv = sys.argv

    if len(argv)<=1:
        usage()

    serviceName = cls._svc_name_
    serviceDisplayName = cls._svc_display_name_
    if serviceClassString is None:
        serviceClassString = GetServiceClassString(cls)

    # Pull apart the command line
    import getopt
    try:
        opts, args = getopt.getopt(argv[1:], customInstallOptions,["password=","username=","startup=","perfmonini=", "perfmondll=", "interactive", "wait="])
    except getopt.error, details:
        print details
        usage()
    userName = None
    password = None
    perfMonIni = perfMonDll = None
    startup = None
    interactive = None
    waitSecs = 0
    for opt, val in opts:
        if opt=='--username':
            userName = val
        elif opt=='--password':
            password = val
        elif opt=='--perfmonini':
            perfMonIni = val
        elif opt=='--perfmondll':
            perfMonDll = val
        elif opt=='--interactive':
            interactive = 1
        elif opt=='--startup':
            map = {"manual": win32service.SERVICE_DEMAND_START, "auto" : win32service.SERVICE_AUTO_START, "disabled": win32service.SERVICE_DISABLED}
            try:
                startup = map[string.lower(val)]
            except KeyError:
                print "'%s' is not a valid startup option" % val
        elif opt=='--wait':
            try:
                waitSecs = int(val)
            except ValueError:
                print "--wait must specify an integer number of seconds."
                usage()

    arg=args[0]
    knownArg = 0
    # First we process all arguments which pass additional args on
    if arg=="start":
        knownArg = 1
        print "Starting service %s" % (serviceName)
        try:
            StartService(serviceName, args[1:])
            if waitSecs:
                WaitForServiceStatus(serviceName, win32service.SERVICE_RUNNING, waitSecs)
        except win32service.error, (hr, fn, msg):
            print "Error starting service: %s" % msg

    elif arg=="restart":
        knownArg = 1
        print "Restarting service %s" % (serviceName)
        RestartService(serviceName, args[1:])
        if waitSecs:
            WaitForServiceStatus(serviceName, win32service.SERVICE_RUNNING, waitSecs)

    elif arg=="debug":
        knownArg = 1
        if not hasattr(sys, "frozen"):
            # non-frozen services use pythonservice.exe which handles a
            # -debug option
            svcArgs = string.join(args[1:])
            exeName = LocateSpecificServiceExe(serviceName)
            try:
                os.system("%s -debug %s %s" % (exeName, serviceName, svcArgs))
            # ^C is used to kill the debug service.  Sometimes Python also gets
            # interrupted - ignore it...
            except KeyboardInterrupt:
                pass
        else:
            # py2exe services don't use pythonservice - so we simulate
            # debugging here.
            DebugService(cls, args)

    if not knownArg and len(args)<>1:
        usage() # the rest of the cmds don't take addn args

    if arg=="install":
        knownArg = 1
        try:
            serviceDeps = cls._svc_deps_
        except AttributeError:
            serviceDeps = None
        try:
            exeName = cls._exe_name_
        except AttributeError:
            exeName = None # Default to PythonService.exe
        try:
            exeArgs = cls._exe_args_
        except AttributeError:
            exeArgs = None
        try:
            description = cls._svc_description_
        except AttributeError:
            description = None
        print "Installing service %s" % (serviceName,)
        # Note that we install the service before calling the custom option
        # handler, so if the custom handler fails, we have an installed service (from NT's POV)
        # but is unlikely to work, as the Python code controlling it failed.  Therefore
        # we remove the service if the first bit works, but the second doesnt!
        try:
            InstallService(serviceClassString, serviceName, serviceDisplayName, serviceDeps = serviceDeps, startType=startup, bRunInteractive=interactive, userName=userName,password=password, exeName=exeName, perfMonIni=perfMonIni,perfMonDll=perfMonDll,exeArgs=exeArgs,description=description)
            if customOptionHandler:
                apply( customOptionHandler, (opts,) )
            print "Service installed"
        except win32service.error, (hr, fn, msg):
            if hr==winerror.ERROR_SERVICE_EXISTS:
                arg = "update" # Fall through to the "update" param!
            else:
                print "Error installing service: %s (%d)" % (msg, hr)
                err = hr
        except ValueError, msg: # Can be raised by custom option handler.
            print "Error installing service: %s" % str(msg)
            err = -1
            # xxx - maybe I should remove after _any_ failed install - however,
            # xxx - it may be useful to help debug to leave the service as it failed.
            # xxx - We really _must_ remove as per the comments above...
            # As we failed here, remove the service, so the next installation
            # attempt works.
            try:
                RemoveService(serviceName)
            except win32api.error:
                print "Warning - could not remove the partially installed service."

    if arg == "update":
        knownArg = 1
        try:
            serviceDeps = cls._svc_deps_
        except AttributeError:
            serviceDeps = None
        try:
            exeName = cls._exe_name_
        except AttributeError:
            exeName = None # Default to PythonService.exe
        try:
            exeArgs = cls._exe_args_
        except AttributeError:
            exeArgs = None
        try:
            description=cls._svc_description_
        except AttributeError:
            description=None
        print "Changing service configuration"
        try:
            ChangeServiceConfig(serviceClassString, serviceName, serviceDeps = serviceDeps, startType=startup, bRunInteractive=interactive, userName=userName,password=password, exeName=exeName, displayName = serviceDisplayName, perfMonIni=perfMonIni,perfMonDll=perfMonDll,exeArgs=exeArgs,description=description)
            if customOptionHandler:
                apply( customOptionHandler, (opts,) )
            print "Service updated"
        except win32service.error, (hr, fn, msg):
            print "Error changing service configuration: %s (%d)" % (msg,hr)
            err = hr

    elif arg=="remove":
        knownArg = 1
        print "Removing service %s" % (serviceName)
        try:
            RemoveService(serviceName)
            print "Service removed"
        except win32service.error, (hr, fn, msg):
            print "Error removing service: %s (%d)" % (msg,hr)
            err = hr
    elif arg=="stop":
        knownArg = 1
        print "Stopping service %s" % (serviceName)
        try:
            if waitSecs:
                StopServiceWithDeps(serviceName, waitSecs = waitSecs)
            else:
                StopService(serviceName)
        except win32service.error, (hr, fn, msg):
            print "Error stopping service: %s (%d)" % (msg,hr)
            err = hr
    if not knownArg:
        err = -1
        print "Unknown command - '%s'" % arg
        usage()
    return err

#
# Useful base class to build services from.
#
class ServiceFramework:
    # Required Attributes:
    # _svc_name_ = The service name
    # _svc_display_name_ = The service display name

    # Optional Attributes:
    _svc_deps_ = None        # sequence of service names on which this depends
    _exe_name_ = None        # Default to PythonService.exe
    _exe_args_ = None        # Default to no arguments
    _svc_description_ = None # Only exists on Windows 2000 or later, ignored on windows NT

    def __init__(self, args):
        import servicemanager
        self.ssh = servicemanager.RegisterServiceCtrlHandler(args[0], self.ServiceCtrlHandler)
        servicemanager.SetEventSourceName(self._svc_name_)
        self.checkPoint = 0

    def GetAcceptedControls(self):
        # Setup the service controls we accept based on our attributes
        accepted = 0
        if hasattr(self, "SvcStop"): accepted = accepted | win32service.SERVICE_ACCEPT_STOP
        if hasattr(self, "SvcPause") and hasattr(self, "SvcContinue"):
            accepted = accepted | win32service.SERVICE_ACCEPT_PAUSE_CONTINUE
        if hasattr(self, "SvcShutdown"): accepted = accepted | win32service.SERVICE_ACCEPT_SHUTDOWN
        return accepted

    def ReportServiceStatus(self, serviceStatus, waitHint = 5000, win32ExitCode = 0, svcExitCode = 0):
        if self.ssh is None: # Debugging!
            return
        if serviceStatus == win32service.SERVICE_START_PENDING:
            accepted = 0
        else:
            accepted = self.GetAcceptedControls()

        if serviceStatus in [win32service.SERVICE_RUNNING,  win32service.SERVICE_STOPPED]:
            checkPoint = 0
        else:
            self.checkPoint = self.checkPoint + 1
            checkPoint = self.checkPoint

        # Now report the status to the control manager
        status = (win32service.SERVICE_WIN32_OWN_PROCESS,
                 serviceStatus,
                 accepted, # dwControlsAccepted,
                 win32ExitCode, # dwWin32ExitCode;
                 svcExitCode, # dwServiceSpecificExitCode;
                 checkPoint, # dwCheckPoint;
                 waitHint)
        win32service.SetServiceStatus( self.ssh, status)

    def SvcInterrogate(self):
        # Assume we are running, and everyone is happy.
        self.ReportServiceStatus(win32service.SERVICE_RUNNING)

    def SvcOther(self, control):
        print "Unknown control status - %d" % control

    def ServiceCtrlHandler(self, control):
        if control==win32service.SERVICE_CONTROL_STOP:
            self.SvcStop()
        elif control==win32service.SERVICE_CONTROL_PAUSE:
            self.SvcPause()
        elif control==win32service.SERVICE_CONTROL_CONTINUE:
            self.SvcContinue()
        elif control==win32service.SERVICE_CONTROL_INTERROGATE:
            self.SvcInterrogate()
        elif control==win32service.SERVICE_CONTROL_SHUTDOWN:
            self.SvcShutdown()
        else:
            self.SvcOther(control)

    def SvcRun(self):
        self.ReportServiceStatus(win32service.SERVICE_RUNNING)
        self.SvcDoRun()
        # Once SvcDoRun terminates, the service has stopped.
        # We tell the SCM the service is still stopping - the C framework
        # will automatically tell the SCM it has stopped when this returns.
        self.ReportServiceStatus(win32service.SERVICE_STOP_PENDING)
