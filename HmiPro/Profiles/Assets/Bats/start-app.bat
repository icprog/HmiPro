@ECHO off
ping 1.1.1.1 -n 1 -w 3000 > nul
start "" .\HmiPro.exe %*