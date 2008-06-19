This directory contains stuff needed to install the Performance 
Monitoring counters for the test service.

This stuff is only needed at install time, and not used at runtime.

This sample code does attempt to install this information at runtime
when the sample service is installed.  However, if you use a setup
program for example this is not needed in the runtime environment.

The NT API's require a .h and a .ini.  These are not used by the Python
runtime at all.  It is your programs responsibility to create counters
in your Python code which correspond to the entries here.