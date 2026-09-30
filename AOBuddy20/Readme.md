### Some thoughts on the structure of AUBuddy20

1. Multithreaded - movement cycle completely separated from the rest, other controllers/components only give the movement controller their goal (vector3+playfield) and the rest should be done by the movement component
2. Rest of the packets are also distributed by the PacketRouter, there are 2 kinds: one which cannot end the sequence (all of them will see the packet) and the ones which really 'consume' the packet and other controllers/components won't see it if another did set the end flag
3. Priority system can be expanded - it's just numbers
4. Separate BotLoop and Movement loop. Attack/Use etc on Botloop
5. Other components read only from Movement controller/movementcomponent or set their desired destination. Every CharDCMove goes out from there (separation of concerns). On reaching (check the currentposition property on movementcontroller/movementcomponent they can advance their inner state/or not). Each priority can set its own desired destination.
6. Each new class should be decorated with [MinDebugLevel(LogEventLevel.Debug)] at first until we know it works properly. Then change to LogEventLevel.Information.
7. Do use the logging/debug logging!
8. Also, Awareness class should be running on a separate thread to collect and store enemy movement/attack status so other controllers/components can use the aggregate data.
9. If AOSharp handlers are insufficient, don't change them if possible and implement it in our own class as asynchronous.
10. Main goal is to port the functionality from ..\AOBuddy10\ project to AOBuddy20
11. Look at AOBuddy10 project and analyze the functionality. Disregard its RESTRUCTURE.md.
12. If AOSharp is insufficient to handle this, tell me!
13. Write short but concise commit messages for each step/functionality you port over.