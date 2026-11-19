# 11. Completing the engine

The app already builds the thrust chamber: the cooled body, the injector plate,
and the bolt flange. That is the part that makes thrust. A chamber you can
actually plumb and light needs a few more solids on the same engine.

Showerhead and impinging plates use a 3 mm port in the center of the face.
The coaxial swirl plate uses a 4 mm port. A spark torch sits in that port.

## 11.1 What is already in the model

| Solid | What it is |
|---|---|
| Body | Chamber, throat, and bell, cooling channels, fuel-inlet nipple, chamber-pressure boss, and coolant taps |
| Injector | The face, both propellant domes, the igniter port, and the pipe from the cooling jacket into the fuel dome |
| Igniter | Spark torch: nozzle into the chamber, two inlets, and a spark-plug boss |
| Flange | Bolt circle that mounts the engine on a test stand |

The rings inside the wall are the cooling manifolds. They connect the channels
to each other. The fuel-inlet nipple opens into the nozzle-end ring. The
outlet pipe runs from the injector-end ring into the fuel dome. The injector
orifices open into the fuel dome or the oxidizer dome.

## 11.2 Parts worth adding, in this order

1. **Fuel dome and oxidizer dome.** Done. The oxidizer volume sits against the
   back of the plate. The fuel volume sits behind a splitter. Each fuel hole
   crosses the oxidizer volume inside a post, so the two propellants meet only
   in the chamber. The igniter port runs through the center and stays sealed
   from both volumes.
2. **Coolant fittings.** Done. A fuel-inlet nipple sits on the nozzle-end
   ring. A pipe leaves the injector-end ring, runs back outside the flange,
   and opens into the fuel dome. The nipple and the short stub are on the
   body. The pipe is on the injector. Their bores overlap when the engine is
   assembled.
3. **Spark-torch igniter.** Done. A separate solid sits in the center port.
   Its nozzle aims into the chamber. Two small inlets feed the torch, and a
   spark-plug boss has a blind hole so the plug seals it. A pyrotechnic
   cartridge is something you buy and slide into the port. The app does not
   design the charge.
4. **Sensor ports.** Done. A boss on the chamber cylinder opens into the hot
   gas between two cooling channels. A tap on the inlet nipple and a tap on
   the outlet stub open into the coolant. The sensor screws into the boss and
   seals it.

Those are the parts from this list. Tanks, valves, and the spark exciter stay
bought hardware.

## 11.3 Leave these as bought hardware

Tanks, the pressurant bottle, regulators, valves, burst disks, hoses, the
spark exciter, and the load cell complete a test stand. They are not part of
the computed engine, and printing them would not make the design better.

These extra solids would still be a design model. A mesh of an igniter does
not make a firing legal or safe. Hot fire stays outside this tool.
