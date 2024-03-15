mkdir /published/
chmod +S /published/
dotnet publish -c Release --runtime=linux-x64 -o /published/ /sources/Duplicati.sln

zip /sources/linux-release.zip /published/