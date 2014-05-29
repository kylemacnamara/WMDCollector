###WMD: Non-Intrusive Host-Based Malware Detection

This software is the client component of a larger work aimed at developing a host-based, non-intrusive monitoring framework for Windows. Through the use of an already existing kernel-level logging facility, our system WMD is able to passively monitor for interesting system events without the need for API hooking or kernel-level drivers.  This tracing facility allows us to efficiently listen for various events that are buffered and then handled via callbacks. As a result, we are able to create a user-mode monitor that does not rely on any custom kernel component.

####Events Collected
WMD currently monitors for five different event types: process creations and terminations, file creations, network connections, and the appearance of a GUI.

#####Process and file activity
Process creation and termination events are collected. Each event includes information such as the image path, the MD5 hash, and the digital signature. The creation of new files is also recorded. This event includes information such as the file type, the file path, the MD5 hash, as well as the zone identifier which identifies if the file was downloaded from the Internet. The file type is determined by analyzing the first 256 bytes of the data for known signatures of common file headers.

Hashing new files is not as straight-forward as it was in the previous section. The main challenge is choosing when a file hashing should occur. Hashing a file while it is still being modified results in useless data. In addition, files can be created with an exclusive lock which prevents additional processes from reading and writing. In fact, creating a process using the Win32 API will result in the creation of an exclusive lock by default. Our hashing scheme therefore tracks the file handle that is used to create the file and waits until that handle is closed before hashing. In case the file was created without the exclusive lock or was created by using the .NET library, the file is also hashed after a timeout starting from the time the file was last modified.

To know the effectiveness of our hashing scheme when a system becomes infected, we considered various ways the hashing scheme could be circumvented. One potential problem is that malware might open a file with an exclusive lock and then never properly close that file. The consequence of this is that the file would not get be hashed until that process terminates. Fortunately, in Windows, a process cannot be started from a file when that file is already locked. Since we are interested in the hashes of executables, this fact increases the likelihood of a file being successfully hashed. Another potential problem is that a file could be deleted before our hashing begins and is a consequence of our logging being asynchronous. Although our current hashing scheme is not guaranteed to always successfully hash new files, it should be robust enough to handle most real world cases.

#####Network activity
The metadata associated with each network connection is logged. This includes information such as ports and IP addresses involved and the number of bytes sent and received. The direction, whether it is an inbound or outbound, of a connection is also logged since malware often listens on a particular port in order to listen for additional commands from a remote user.

#####GUI activity
Since malware often tries to hide its presence from users, it is important to know which programs a user is interacting with. Thus, we log events corresponding to the appearance of a GUI. 

####Implementation
This monitoring software is built upon a kernel-level logging system, [Event Tracing for Windows (ETW)](http://msdn.microsoft.com/en-us/library/windows/desktop/bb968803%28v=vs.85%29.aspx). ETW follows the publisher subscriber pattern in which various various system components publish events related to their activity. 

All of above events were collected using ETW except GUI events.  GUI  events were collected using the [Microsoft UI Automation Framework](http://msdn.microsoft.com/en-us/library/ms747327.aspx).

####How to Use
In order to change the URL of the server used, modify the static variables located in the file Exporer/HttpCollector.cs. Events are currently sent to the server by gzipping a json representation of the list of events. The json can be modified by modifying the ToString method of the events located in the Events package. 
