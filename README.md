JoinFS is an advanced multiplayer client for flight simulators including Microsoft Flight Simulator 2020, FSX, X-Plane and Prepar3D. Allows players to fly together across different simulators.

Disclaimer
==========

This SOFTWARE is provided "as is" and without warranties as to performance of merchantability or any other warranties whether expressed or implied. Because of the various hardware and software environments into which the SOFTWARE may be put, no warranty of fitness for a particular purpose is offered.

To the maximum extent permitted by applicable law, in no event shall the author be liable for any damages whatsoever (including without limitation, direct or indirect damages for personal injury, loss of profit, business interruption, loss of information, or any other pecuniary loss) arising out of the use, or inability to use this SOFTWARE, even if the author has been advised of the possibility of such damages.

You are solely responsible for all costs and expenses associated with rectification, repair or damage caused by such errors.

You must assume the entire risk of using the SOFTWARE.

Visual Studio
=============

The code will build under Microsoft Visual Studio 2022 or later. The 'Community' version of Visual Studio is free to download from Microsoft.

JoinFS
======

The folder 'JoinFS' contains the main source code for the JoinFS application.

The solution 'JoinFS.sln' contains four projects for building the specific simulator versions of JoinFS. These projects build with .Net Framework 4.7

The solution 'JoinFS-dotnet.sln' contains two projects for the console and server versions. These projects build with .Net 6.0 and can run under other operating systems that support .Net 6.0 or later.

In these solutions there are also Setup projects for creating Windows installation executables. Install the Wix Toolset Visual Studio 2022 Extension to build these.

To make a new build that is incompatible with other versions of JoinFS, change JoinFS.LocalNode.VERSION in Node.cs and copy the low byte value to Link::NODE_VERSION in JoinFS-XP/Link.h, for example if the number is 0x1234, set NODE_VERSION to 0x0034. This is due to the way the plugin code confirms the version number when communicating with JoinFS.

JoinFS X-Plane Plugin
=====================

The folder 'JoinFS-XP' contains the source code for the X-Plane plugin.

The JoinFS-XP 64-bit project will generate the X-Plane plugin 'win.xpl'. Modify the Post-Build event to place 'win.xpl' to an appropriate location, such as the JoinFS build folder.

Use the linux shell script JoinFS-XP\Build-Linux.sh on a linux system to generate the X-Plane plugin 'lin.xpl'.

When building a new plugin increment DATA_VERSION in JoinFS/XPlane.cs and JoinFS-XP/Link.cpp. This will ask the user to install the new plugin.
