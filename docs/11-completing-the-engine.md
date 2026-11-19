# 11. Completing the engine

The app already builds the thrust chamber: the cooled body, the injector plate,
and the bolt flange. That is the part that makes thrust. A chamber you can
actually plumb and light needs a few more solids on the same engine.

The injector already has a hole for an igniter. Showerhead and impinging plates
use a 3 mm port in the center of the face. The coaxial swirl plate uses a 4 mm
port. The app does not build the igniter itself.

## 11.1 What is already in the model

| Solid | What it is |
|---|---|
| Body | Chamber, throat, and bell, with helical cooling channels and the two rings inside the wall that join those channels |
| Injector | The face that meters fuel and oxidizer, the fuel dome, the oxidizer dome, and the center igniter port |
| Flange | Bolt circle that mounts the engine on a test stand |

The rings inside the wall are the cooling manifolds. They connect the channels
to each other. They do not yet have a nipple a hose can screw onto. The
injector orifices open into the fuel dome or the oxidizer dome.

## 11.2 Parts worth adding, in this order

1. **Fuel dome and oxidizer dome.** Done. The oxidizer volume sits against the
   back of the plate. The fuel volume sits behind a splitter. Each fuel hole
   crosses the oxidizer volume inside a post, so the two propellants meet only
   in the chamber. The igniter port runs through the center and stays sealed
   from both volumes.
2. **Coolant fittings.** A fuel-in boss at the nozzle end of the cooling
   jacket, and a fuel-out boss near the injector, where the fuel leaves the
   wall and goes into the fuel dome. The channels and the rings are already
   there. The fittings are the missing connections.
3. **Spark-torch igniter.** A small torch that sits in the existing center
   port: two tiny inlets, a spark-plug boss, and a short nozzle aimed into the
   chamber. This is the usual igniter for a small liquid engine. A pyrotechnic
   cartridge is something you buy and slide into the port. The app should not
   design the charge.
4. **Sensor ports.** One small boss for a chamber-pressure sensor, and taps on
   the coolant inlet and outlet. The test checklist already calls for these.
   The geometry does not have the fittings yet.

The two coolant fittings are what still has to join the cooling jacket to the
fuel dome. The igniter comes after that, because the port it mounts in is
already in the injector.

## 11.3 Leave these as bought hardware

Tanks, the pressurant bottle, regulators, valves, burst disks, hoses, the
spark exciter, and the load cell complete a test stand. They are not part of
the computed engine, and printing them would not make the design better.

These extra solids would still be a design model. A mesh of an igniter does
not make a firing legal or safe. Hot fire stays outside this tool.
