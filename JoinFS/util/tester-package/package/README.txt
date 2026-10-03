JoinFS test version - formation position logging
=================================================

Version: {{VERSION}}

This test version of JoinFS records how accurately the other aircraft are shown in your
simulator. Everything else works like your normal JoinFS, with the same settings and model
matching. The recordings help make close formation flying smoother.


FIRST TIME ONLY
---------------
1. Right-click the zip file > Properties > tick "Unblock" (bottom right) > OK.
2. Extract the whole zip to a folder of your choice, for example Documents\JoinFS-test.
3. JoinFS needs the free Microsoft ".NET 8 Desktop Runtime". If it is missing, the start
   script tells you and opens the download page.


EACH TEST FLIGHT
----------------
1. Close any JoinFS that is already running.
2. Double-click the start file for your simulator:
{{STARTFILES}}
   It checks your PC's clock (a few seconds), then starts JoinFS.
3. Use JoinFS as usual: join the same session or hub as the rest of the team.
   Everyone in the formation should fly with this test version.
4. Fly. Formation turns, rejoins and close formation are the most useful.


AFTER TESTING
-------------
1. Close JoinFS.
2. Double-click "Collect test logs.bat" and type your name or callsign.
   It packs the logs into a zip file on your Desktop.
3. Send the logs: {{UPLOAD}}

It collects the logs of the last 7 days, so it is fine to collect after several flights.


GOOD TO KNOW
------------
- Disk space: about 40 MB per hour for each other aircraft in the session (the collected
  zip is several times smaller).
- What the logs contain: the positions and movements of the aircraft in the session, their
  callsigns and JoinFS names, timing of the network messages, and the normal JoinFS log
  (which includes network addresses). They are used only to improve JoinFS.
- Windows may warn that the program is from an unknown publisher: choose "More info" >
  "Run anyway". The program is not signed.
- To go back to your normal JoinFS, just start it as before.
