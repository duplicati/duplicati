The binaries here are obtained by checking out the source for e12204c6:
https://github.com/weltkante/managed-lzma/commit/3e12204c60ada8164561a841bb8dcb4c96078c99

The upgrading the project "master" to .Net8 with the `upgrade-assistant` tool, and finally building in release mode with "dotnet build master.2012.csproj /p:Configuration=Release"

This is a workaround from an earlier version of the project, and the support should either use the published `0.2.0-alpha-7` version from NuGet, or perhaps be deprecated.

Full instructions for building:
```
git clone https://github.com/weltkante/managed-lzma
cd managed-lzma
git checkout 3e12204c60ada8164561a841bb8dcb4c96078c99
upgrade-assistant upgrade master/master.2012.csproj --operation Inplace --targetFramework net8.0
cd master
dotnet build /p:Configuration=Release master.2012.csproj
```

Result file is in `bin/Release/net8.0/managed-lzma.dll`