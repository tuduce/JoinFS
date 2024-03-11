g++ -o ../JoinFS/lin.xpl Common.cpp Link.cpp JoinFS-XP.cpp XPMP2/lib/lin/libXPMP2.a -Os -s -shared -rdynamic -nodefaultlibs -undefined_warning -fPIC -std=c++17 -I SDK/CHeaders/XPLM -I XPMP2/inc
echo Done.
