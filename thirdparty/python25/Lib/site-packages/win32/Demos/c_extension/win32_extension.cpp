// Note: this sample does nothing useful other than to show you how
// your own Python extension can link with and use the functions from
// pywintypesxx.dll
#include "Python.h"
#include "PyWinTypes.h"

static struct PyMethodDef win32extension_functions[] = {
    0
};

extern "C" __declspec(dllexport)
void initwin32_extension(void)
{
  // Initialize PyWin32 globals (such as error objects etc)
  PyWinGlobals_Ensure();

  PyObject *module;
  module = Py_InitModule("win32_extension", win32extension_functions);
  if (!module)
    return;
}
