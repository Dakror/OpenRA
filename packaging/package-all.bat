@echo off
rd /S /Q built
del OpenRA.Setup.exe
pushd ..
sh packaging/package-all.sh test_0.1 test_0.1
popd
"C:\Program Files (x86)\NSIS\makensis.exe" -V2 -DSRCDIR="E:/Programmieren/Dune/OpenRA/packaging/built" -DDEPSDIR="E:/Programmieren/Dune/OpenRA/thirdparty/windows" windows/OpenRA.nsi
mv windows/OpenRA.Setup.exe OpenRA.Setup.exe
del osx/bash.exe.stackdump
del linux/bash.exe.stackdump
