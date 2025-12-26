Sufni Suspension Telemetry
==========================

![dashboard](pics/dashboard.png)

Sufni\* Suspension Telemetry is a mountain bike suspension telemetry system that
was born out of sheer curiosity. The [data acquisition unit](https://github.com/sghctoma/sst/wiki/02-Data-Acquisition-Unit) is built around the
Raspberry Pi Pico W and uses affordable off-the-shelf components, so anybody
with a bit of soldering skills can build it.

Contrary to most (all?) suspension telemetry systems, Sufni uses rotary encoders
to measure movement. They are cheap, reasonably accurate, and can reach pretty
high sample rates. An additional benefit is that on some frames they are easier
to attach to rear shocks than linear sensors, because they can fit into much
tigther spaces.

The application retrieves recorded sessions from the DAQ either over Wi-Fi or via its mass-storage device mode. Both the mobile and desktop apps can import data from the DAQ and synchronize sessions between them. A typical workflow is to transfer sessions to the mobile app on the trail for a quick review, then sync them to the desktop application later for more in-depth analysis.

The user interface provides plots that help with setting spring rate, damping, and overall bike balance. In the desktop app, GPX tracks can be uploaded and synchronized with the travel plot, making it easy to see how the suspension behaved at specific sections of the trail.

\* *The word "sufni" (pronounced SHOOF-nee) means tool shed in Hungarian, but
also used as an adjective denoting something as DIY, garage hack, etc.*
