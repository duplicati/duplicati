The files in this folder are ONLY used for building.
They will not work by themselves because they are merely wrappers for native libraries.
By placing them here, it is possible to build the solutions without having the respective frameworks present on the developer machine.
They are not placed in "thirdparty" because they should never be distributed.
Instead, the target system should have the required toolkits installed.

Much of the logic can handle missing frameworks and will do some fallback if the libraries are missing.