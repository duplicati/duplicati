#include <unistd.h>
#include <sys/ioctl.h>

// TODO This C wrapper is required since P/Invoke cannot correctly pass variadic
// arguments to ioctl on non-Windows platforms.
// May be fixed in the future:
// https://github.com/dotnet/runtime/issues/10478
// https://github.com/dotnet/runtime/pull/112884
// https://github.com/dotnet/runtime/issues/48796

// Wrapper for ioctl that takes a pointer to uint32
int ioctl_uint32(int fd, unsigned long request, unsigned int *value)
{
    return ioctl(fd, request, value);
}

// Wrapper for ioctl that takes a pointer to uint64
int ioctl_uint64(int fd, unsigned long request, unsigned long *value)
{
    return ioctl(fd, request, value);
}

// Wrapper for ioctl that takes no argument
int ioctl_no_arg(int fd, unsigned long request)
{
    return ioctl(fd, request);
}
