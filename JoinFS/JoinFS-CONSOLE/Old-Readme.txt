JoinFS-CONSOLE

Install:

Unzip this archive to a suitable location on your system.
Requires .NET 6.0 Runtime (not the SDK)
  Windows/Linux       https://dotnet.microsoft.com/en-us/download/dotnet/6.0
  Debian              https://docs.microsoft.com/en-us/dotnet/core/install/linux-debian
  Raspberry Pi        https://www.petecodes.co.uk/install-and-use-microsoft-dot-net-6-with-the-raspberry-pi/


Usage: dotnet JoinFS-CONSOLE.dll [options]

  --create               Create a new session if hub mode is not enabled
  --join <address>       Join a session at the specified address
  --rejoin               Join the previous session.
  --global               Join the global session immediately.
  --nickname <name>      Choose a nickname. Others will be able to see this in the aircraft and user lists.
  --port                 Use the specified IP port for connections to other sessions. The default port is 6112.
  --hub                  Create a public session that will appear in everyone's hub list. Port Forwarding required. See manual.
  --hubdomain <domain>   Optional domain name address, for example, 'myserver.com'
  --hubname <name>       The public name of this hub that will be shown in everyone's hub list
  --hubabout <details>   A short description for this hub that will appear in everyone's hub list
  --hubvoip <details>    Details of a voice server associated with this hub. For example, "ts3.myserver.com"
  --hubevent <details>   Optional information about upcoming events or meeting dates and times
  --password <password>  Protect your session with a password
  --play <.jfs file>     Open a JoinFS recorder file and play back the recording in the current session. Other users will need to enable 'Allow Multiple Objects'. See Manual.
  --record               Immediately start recording.
  --loop                 Keep looping back to the start of the recording during play back.
  --activitycircle <nm>  Sets the maximum distance in nautical miles that aircraft will be injected into the simulator.
  --follow <nm>          Sets the distance in meters at which your aircraft will be positioned behind another object. Applies to follow option in the aircraft list.
  --atc                  Enable ATC mode. Other users will see you marked as ATC.
  --airport <code>       Specifies the airport this ATC is controlling from. For example, 'EGLL'.
  --lowbandwidth         Reduce the amount of network data used by JoinFS - may affect performance
  --whazzup              Update the file 'whazzup.txt' with aircraft data to use in your own online maps
  --whazzup-public       Include aircraft from all public hubs in the 'whazzup.txt' file
  --minimize             Launch JoinFS minimized on the Windows task bar. Quiet mode.
  --background           For JoinFS-CONSOLE, run as a background task with no keyboard input.
  --nosim                Do not connect to the simulator at launch.
  --nogui                Run JoinFS as a background process without any windows or UI.
  --multiobjects         Allow this client to receive more than one aircraft from any other client in a session.
  --simfolder "<folder>" Sets the simulator root folder.
  --scan                 Scan For Models
  --generatecsl          Generate CSL objects for installed X-Plane aircraft during a scan.
  --skipcsldone          Skip CSL objects already done during a scan.
  --xplane               Enable connection to the X-Plane flight simulator
  --installplugin        Install the X-Plane plugin for JoinFS
  --tcas                 Enable TCAS in X-Plane to allow map support. Aircraft will be acquired by the plugin which may interfere with other plugins.
  --quit                 Terminate all instances of JoinFS currently running on this machine.
  --help                 Show usage details.

Interactive key commands:

S                        Show session details
A                        Show aircraft details
Escape                   Close JoinFS
Ctrl + N                 Toggle the network connection
Ctrl + S                 Toggle the simulator connection
Ctrl + Q                 Scan for models


Install the X-Plane plugin:

dotnet JoinFS-CONSOLE.dll --installplugin --simfolder "<folder>"
