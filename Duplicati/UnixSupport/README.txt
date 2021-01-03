This project is not really 3rd party.

It was created solely for use with Duplicati, but it depends on the Mono.Posix.dll file, which is not found on the common Windows developer platform. By creating this mini project, it is possible to reference the functions from Mono.Posix during a Windows build without having access to the Mono.Posix.dll. As this dll is never invoked on Windows, it should have no interest to the Windows developer.
